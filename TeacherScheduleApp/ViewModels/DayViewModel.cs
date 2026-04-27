using Avalonia.Controls;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Controls;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;
using TeacherScheduleApp.Views;

namespace TeacherScheduleApp.ViewModels
{
    public class DayViewModel : ViewModelBase
    {
        private readonly EventService _eventService = new();
        private readonly int _employeeId;
        private CalendarPanel? _calendarPanel;
        private bool _isDialogOpen;

        public DateTime CurrentDate
        {
            get => _currentDate;
            set => this.RaiseAndSetIfChanged(ref _currentDate, value);
        }
        private DateTime _currentDate;

        public ObservableCollection<string> Hours { get; } = new();
        public ObservableCollection<CellInfo> GridCells { get; } = new();

        public ReactiveCommand<Unit, Unit> PreviousDayCommand { get; }
        public ReactiveCommand<Unit, Unit> NextDayCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }

        private readonly Action<DateTime> _onDateChanged;

        public DayViewModel(
            DateTime date,
            Action<DateTime> onDateChanged,
            int employeeId = EventService.DefaultEmployeeId)
        {
            _employeeId = employeeId;
            _onDateChanged = onDateChanged;
            CurrentDate = date.Date;

            for (int i = 0; i < 24; i++)
                Hours.Add($"{i:00}:00");

            RebuildAll();

            MessageBus.Current
                .Listen<AutoEventsGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RebuildAll());

            MessageBus.Current
                .Listen<UserSettingsChangedMessage>()
                .Where(m => m.Date.Date == CurrentDate.Date)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RebuildAll());

            MessageBus.Current
                .Listen<GlobalSettingsChangedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RebuildAll());

            PreviousDayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = CurrentDate.AddDays(-1);
                _onDateChanged(CurrentDate);
                RebuildAll();
            });

            NextDayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = CurrentDate.AddDays(1);
                _onDateChanged(CurrentDate);
                RebuildAll();
            });

            TodayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = DateTime.Today;
                _onDateChanged(CurrentDate);
                RebuildAll();
            });
        }

        public void AttachCalendarPanel(CalendarPanel panel)
        {
            _calendarPanel = panel;
            _calendarPanel.DayHourClicked += (_, hour) => OnEmptySpaceClicked(hour);
            RebuildAll();
        }

        private void RebuildAll()
        {
            GridCells.Clear();

            var resolved = SettingsService.GetResolvedDaySettings(CurrentDate, _employeeId);
            var arrival = resolved.ArrivalTime;
            var departure = resolved.DepartureTime;
            var isHoliday = HolidayHelper.IsCzechHoliday(CurrentDate);

            for (int hr = 0; hr < 24; hr++)
            {
                GridCells.Add(new CellInfo
                {
                    DayIndex = 0,
                    HourIndex = hr,
                    WorkStart = arrival.TotalHours,
                    WorkEnd = departure.TotalHours,
                    IsHoliday = isHoliday
                });
            }

            LoadEvents();
        }

        public void LoadEvents()
        {
            if (_calendarPanel == null)
                return;

            _calendarPanel.Children.Clear();

            var resolved = SettingsService.GetResolvedDaySettings(CurrentDate, _employeeId);
            double arr = resolved.ArrivalTime.TotalHours;
            double dep = resolved.DepartureTime.TotalHours;

            if (arr > 0)
            {
                _calendarPanel.Children.Add(new CalendarBackgroundBlock
                {
                    DayIndex = 0,
                    StartHour = 0,
                    EndHour = arr
                });
            }

            if (dep < 24)
            {
                _calendarPanel.Children.Add(new CalendarBackgroundBlock
                {
                    DayIndex = 0,
                    StartHour = dep,
                    EndHour = 24
                });
            }

            var events = _eventService
                .GetEventsForDay(_employeeId, CurrentDate)
                .Where(e => !e.IsDeleted)
                .ToList();

            foreach (var e in events)
            {
                if (e.EventType == EventType.Lunch)
                {
                    e.IsInactive = false;
                    continue;
                }

                e.IsInactive = events.Any(other =>
                    other.Id != e.Id &&
                    other.StartTime < e.EndTime &&
                    e.StartTime < other.EndTime &&
                    other.EventType.GetOverlayPriority() > e.EventType.GetOverlayPriority());
            }

            var segments = new List<(Event ev, double sh, double eh)>();
            foreach (var ev in events)
            {
                double sh = ev.StartTime.TimeOfDay.TotalHours;
                double eh = ev.EndTime.TimeOfDay.TotalHours;

                if (eh > sh)
                    segments.Add((ev, sh, eh));
            }

            if (segments.Count == 0)
                return;

            var clusters = new List<List<(Event ev, double sh, double eh)>>();

            foreach (var seg in segments.OrderBy(s => s.sh))
            {
                var cluster = clusters.FirstOrDefault(c => c.Any(x => x.sh < seg.eh && seg.sh < x.eh));
                if (cluster == null)
                {
                    cluster = new List<(Event ev, double sh, double eh)>();
                    clusters.Add(cluster);
                }

                cluster.Add(seg);
            }

            foreach (var cluster in clusters)
            {
                var columns = new List<double>();
                var colIndex = new Dictionary<Event, int>();

                foreach (var seg in cluster.OrderBy(s => s.sh))
                {
                    int idx = columns.FindIndex(end => end <= seg.sh);
                    if (idx < 0)
                    {
                        columns.Add(seg.eh);
                        idx = columns.Count - 1;
                    }
                    else
                    {
                        columns[idx] = seg.eh;
                    }

                    colIndex[seg.ev] = idx;
                }

                int colCount = columns.Count;

                foreach (var seg in cluster)
                {
                    seg.ev.HasCollision = cluster.Any(other =>
                        !ReferenceEquals(other.ev, seg.ev) &&
                        other.sh < seg.eh &&
                        seg.sh < other.eh &&
                        seg.ev.EventType.ShouldShowCollisionAgainst(other.ev.EventType));

                    var ctrl = new CalendarEventControl(seg.ev)
                    {
                        DayIndex = 0,
                        StartHour = seg.sh,
                        EndHour = seg.eh,
                        OverlapCount = colCount <= 1 ? 1 : colCount,
                        OverlapIndex = colCount <= 1 ? 0 : colIndex[seg.ev]
                    };

                    ctrl.PointerPressed += (_, a) =>
                    {
                        a.Handled = true;
                        OnEventClicked(seg.ev);
                    };

                    _calendarPanel.Children.Add(ctrl);
                }
            }
        }

        private async void OnEmptySpaceClicked(double hour)
        {
            if (_isDialogOpen)
                return;

            _isDialogOpen = true;

            try
            {
                double snapped = Math.Round(hour);
                var start = CurrentDate.AddHours(snapped);
                var end = start.AddHours(1);

                var win = Helper.GetMainWindow();
                if (win == null)
                    return;

                var vm = new CreateEventDialogViewModel(start, _employeeId)
                {
                    EndDate = end.Date,
                    EndTime = end.TimeOfDay
                };

                var dlg = new CreateEventDialog
                {
                    DataContext = vm
                };

                var ev = await dlg.ShowDialog<Event?>(win);
                if (ev == null)
                    return;

                if (ev.EventType == EventType.Lunch && !ev.IsDeleted)
                {
                    var generator = new AutomaticEventsGeneratorService(
                        _eventService,
                        prompt => AskCollisionAsync(prompt),
                        _employeeId);

                    var prep = await generator.PrepareManualLunchAsync(ev);
                    if (!prep.Ok || prep.Event == null)
                    {
                        await ShowErrorAsync(prep.Error ?? "Oběd se nepodařilo vytvořit.");
                        return;
                    }

                    ev = prep.Event;
                }

                await ApplyEventChangeAsync(ev, null);
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        private async void OnEventClicked(Event existing)
        {
            if (_isDialogOpen)
                return;

            _isDialogOpen = true;

            try
            {
                var win = Helper.GetMainWindow();
                if (win == null)
                    return;

                var vm = new CreateEventDialogViewModel(existing.StartTime, _employeeId);
                vm.LoadFromEvent(existing);

                var dlg = new CreateEventDialog
                {
                    DataContext = vm
                };

                var ev = await dlg.ShowDialog<Event?>(win);
                if (ev == null)
                    return;

                if (ev.EventType == EventType.Lunch && !ev.IsDeleted)
                {
                    var generator = new AutomaticEventsGeneratorService(
                        _eventService,
                        prompt => AskCollisionAsync(prompt),
                        _employeeId);

                    var prep = await generator.PrepareManualLunchAsync(ev);
                    if (!prep.Ok || prep.Event == null)
                    {
                        await ShowErrorAsync(prep.Error ?? "Oběd se nepodařilo uložit.");
                        return;
                    }

                    ev = prep.Event;
                }

                await ApplyEventChangeAsync(ev, existing);
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        private async Task<bool> AskCollisionAsync(string message)
        {
            var win = Helper.GetMainWindow();
            if (win == null)
                return false;

            var msgBox = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ButtonDefinitions = ButtonEnum.YesNo,
                ContentTitle = "Kolize s obědem",
                ContentMessage = message,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon.Warning
            });

            var result = await msgBox.ShowWindowDialogAsync(win);
            return result == ButtonResult.Yes;
        }

        private async Task ApplyEventChangeAsync(Event edited, Event? original)
        {
            edited.EmployeeId = _employeeId;

            ChangedRange changedRange;

            if (edited.IsDeleted)
            {
                bool cascadeDelete = original != null && original.ParentEventId == null;
                changedRange = cascadeDelete
                    ? _eventService.DeleteEventCascadeRaw(edited.Id, _employeeId)
                    : _eventService.DeleteEventRaw(edited.Id, _employeeId);
            }
            else if (edited.Id != 0)
            {
                edited.IsAutoGenerated = false;
                changedRange = _eventService.UpdateEventRaw(edited);
            }
            else
            {
                changedRange = _eventService.CreateEventRaw(edited);
            }

            var processor = new ScheduleChangeProcessor(
                _eventService,
                prompt => AskCollisionAsync(prompt),
                _employeeId);

            await processor.ApplyAsync(
                changedRange,
                preserveUserSettings: false);
        }

        private async Task ShowErrorAsync(string message)
        {
            var win = Helper.GetMainWindow();
            if (win == null)
                return;

            var msgBox = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ButtonDefinitions = ButtonEnum.Ok,
                ContentTitle = "Chyba",
                ContentMessage = message,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon.Warning
            });

            await msgBox.ShowWindowDialogAsync(win);
        }

        private static bool Intersects(DateTime a0, DateTime a1, DateTime b0, DateTime b1)
            => a0 < b1 && b0 < a1;

        public class CellInfo
        {
            public int DayIndex { get; init; }
            public int HourIndex { get; init; }
            public double WorkStart { get; init; }
            public double WorkEnd { get; init; }
            public bool IsHoliday { get; init; }

            public bool IsWorkingHour =>
                !IsHoliday &&
                HourIndex >= (int)Math.Floor(WorkStart) &&
                HourIndex < (int)Math.Ceiling(WorkEnd);
        }
    }
}
