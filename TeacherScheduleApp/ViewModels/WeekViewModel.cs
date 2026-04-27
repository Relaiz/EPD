using Avalonia.Controls;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Controls;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace TeacherScheduleApp.ViewModels
{
    public class WeekViewModel : ViewModelBase
    {
        private CalendarPanel? _calendarPanel;
        private readonly EventService _eventService = new EventService();
        private readonly int _employeeId;

        private DateTime _startOfWeek;
        public DateTime StartOfWeek
        {
            get => _startOfWeek;
            set => this.RaiseAndSetIfChanged(ref _startOfWeek, value);
        }

        private DateTime _endOfWeek;
        public DateTime EndOfWeek
        {
            get => _endOfWeek;
            set => this.RaiseAndSetIfChanged(ref _endOfWeek, value);
        }

        public List<int> GridCells { get; set; } = new();
        public ObservableCollection<WeekDayInfo> WeekDays { get; } = new();
        public ObservableCollection<string> Hours { get; } = new();

        private bool _isDialogOpen;

        public ReactiveCommand<Unit, Unit> PreviousWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> NextWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }

        private readonly Action<DateTime> _onDateChanged;

        private DateTime _currentDate;
        public DateTime CurrentDate
        {
            get => _currentDate;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentDate, value);
                this.RaisePropertyChanged(nameof(WeekTitle));
            }
        }

        public string WeekTitle => BuildWeekTitle(StartOfWeeks(CurrentDate), CultureInfo.CurrentUICulture);

        public WeekViewModel(
            DateTime date,
            Action<DateTime> onDateChanged,
            int employeeId = EventService.DefaultEmployeeId)
        {
            _employeeId = employeeId;
            _onDateChanged = onDateChanged;
            CurrentDate = date.Date;

            for (int i = 0; i < 24; i++)
                Hours.Add($"{i:00}:00");

            int delta = DayOfWeekNumber(CurrentDate.DayOfWeek) - 1;
            StartOfWeek = CurrentDate.AddDays(-delta).Date;
            EndOfWeek = StartOfWeek.AddDays(6);

            FillWeekDays();

            PreviousWeekCommand = ReactiveCommand.Create(() =>
            {
                StartOfWeek = StartOfWeek.AddDays(-7);
                EndOfWeek = EndOfWeek.AddDays(-7);
                CurrentDate = StartOfWeek;
                _onDateChanged?.Invoke(CurrentDate);
                FillWeekDays();
                LoadEvents();
            });

            NextWeekCommand = ReactiveCommand.Create(() =>
            {
                StartOfWeek = StartOfWeek.AddDays(7);
                EndOfWeek = EndOfWeek.AddDays(7);
                CurrentDate = StartOfWeek;
                _onDateChanged?.Invoke(CurrentDate);
                FillWeekDays();
                LoadEvents();
            });

            TodayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = DateTime.Today;
                int diff = DayOfWeekNumber(CurrentDate.DayOfWeek) - 1;
                StartOfWeek = CurrentDate.AddDays(-diff).Date;
                EndOfWeek = StartOfWeek.AddDays(6);
                _onDateChanged?.Invoke(CurrentDate);
                FillWeekDays();
                LoadEvents();
            });

            GridCells = Enumerable.Range(0, 48 * 7).ToList();

            MessageBus.Current
                .Listen<UserSettingsChangedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => LoadEvents());

            MessageBus.Current
                .Listen<AutoEventsGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => LoadEvents());

            MessageBus.Current
                .Listen<GlobalSettingsChangedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => LoadEvents());

            LoadEvents();
        }

        public void AttachCalendarPanel(CalendarPanel panel)
        {
            _calendarPanel = panel;
            _calendarPanel.DayHourClicked += (dayIndex, hour) => OnEmptySpaceClicked(dayIndex, hour);
            LoadEvents();
        }

        public async void OnEmptySpaceClicked(int dayIndex, double hour)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;

            try
            {
                double snapped = Math.Round(hour);
                DateTime eventStart = StartOfWeek.AddDays(dayIndex).AddHours(snapped);
                DateTime eventEnd = eventStart.AddHours(1);

                var main = Helpers.Helper.GetMainWindow();
                if (main == null) return;

                var existing = _eventService.FindEventByStartTime(eventStart, _employeeId);

                var dlg = new Views.CreateEventDialog();
                CreateEventDialogViewModel vm;

                if (existing != null)
                {
                    vm = new CreateEventDialogViewModel(existing.StartTime, _employeeId)
                    {
                        Id = existing.Id,
                        Title = existing.Title,
                        Description = existing.Description ?? string.Empty,
                        AllDay = existing.AllDay,
                        StartDate = existing.StartTime.Date,
                        StartTime = existing.StartTime.TimeOfDay,
                        EndDate = existing.EndTime.Date,
                        EndTime = existing.EndTime.TimeOfDay,
                        EventType = existing.EventType
                    };

                    vm.SelectedEventTypePair = vm.LocalizedEventTypes.First(kvp => kvp.Key == existing.EventType);
                }
                else
                {
                    vm = new CreateEventDialogViewModel(eventStart, _employeeId)
                    {
                        EndDate = eventEnd.Date,
                        EndTime = eventEnd.TimeOfDay
                    };
                }

                dlg.DataContext = vm;
                var ev = await dlg.ShowDialog<Event>(main);
                if (ev == null) return;

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

        private async void OnEventClicked(Event ev)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;

            try
            {
                var main = Helpers.Helper.GetMainWindow();
                if (main == null) return;

                var oldStart = ev.StartTime.Date;
                var oldEnd = ev.EndTime.Date;

                var dlg = new Views.CreateEventDialog();
                var vm = new CreateEventDialogViewModel(ev.StartTime, _employeeId)
                {
                    Id = ev.Id,
                    Title = ev.Title,
                    Description = ev.Description ?? string.Empty,
                    AllDay = ev.AllDay,
                    StartDate = ev.StartTime.Date,
                    StartTime = ev.StartTime.TimeOfDay,
                    EndDate = ev.EndTime.Date,
                    EndTime = ev.EndTime.TimeOfDay,
                    EventType = ev.EventType
                };

                vm.SelectedEventTypePair = vm.LocalizedEventTypes.First(kvp => kvp.Key == ev.EventType);
                dlg.DataContext = vm;

                var updated = await dlg.ShowDialog<Event>(main);
                if (updated == null) return;
                if (updated.EventType == EventType.Lunch && !updated.IsDeleted)
                {
                    var generator = new AutomaticEventsGeneratorService(
                        _eventService,
                        prompt => AskCollisionAsync(prompt),
                        _employeeId);

                    var prep = await generator.PrepareManualLunchAsync(updated);
                    if (!prep.Ok || prep.Event == null)
                    {
                        await ShowErrorAsync(prep.Error ?? "Oběd se nepodařilo uložit.");
                        return;
                    }

                    updated = prep.Event;
                }
                await ApplyEventChangeAsync(updated, ev);
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

        public void LoadEvents()
        {
            if (_calendarPanel == null) return;

            _calendarPanel.Children.Clear();

            for (int day = 0; day < 7; day++)
            {
                var d = StartOfWeek.AddDays(day);

                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || HolidayHelper.IsCzechHoliday(d))
                {
                    _calendarPanel.Children.Add(new CalendarBackgroundBlock
                    {
                        DayIndex = day,
                        StartHour = 0,
                        EndHour = 24
                    });
                    continue;
                }

                var resolved = SettingsService.GetResolvedDaySettings(d, _employeeId);
                double arr = resolved.ArrivalTime.TotalHours;
                double dep = resolved.DepartureTime.TotalHours;

                if (arr > 0)
                {
                    _calendarPanel.Children.Add(new CalendarBackgroundBlock
                    {
                        DayIndex = day,
                        StartHour = 0,
                        EndHour = arr
                    });
                }

                if (dep < 24)
                {
                    _calendarPanel.Children.Add(new CalendarBackgroundBlock
                    {
                        DayIndex = day,
                        StartHour = dep,
                        EndHour = 24
                    });
                }
            }

            var events = _eventService.GetEventsForWeek(_employeeId, StartOfWeek).ToList();

            var parentsWithChildren = events
                .Where(e => e.ParentEventId != null)
                .Select(e => e.ParentEventId!.Value)
                .ToHashSet();

            events = events
                .Where(e => !(e.ParentEventId == null && parentsWithChildren.Contains(e.Id)))
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

            for (int day = 0; day < 7; day++)
            {
                var d = StartOfWeek.AddDays(day);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                var resolved = SettingsService.GetResolvedDaySettings(d, _employeeId);
                var segments = new List<(Event ev, double sh, double eh)>();

                foreach (var ev in events)
                {
                    int startDay = (ev.StartTime.Date - StartOfWeek.Date).Days;
                    int endDay = (ev.EndTime.Date - StartOfWeek.Date).Days;

                    if (endDay < 0 || startDay > 6)
                        continue;

                    if (day < Math.Max(0, startDay) || day > Math.Min(6, endDay))
                        continue;

                    double sh, eh;

                    if (ev.AllDay)
                    {
                        sh = resolved.ArrivalTime.TotalHours;
                        eh = resolved.DepartureTime.TotalHours;
                    }
                    else
                    {
                        if (day == startDay && day == endDay)
                        {
                            sh = ev.StartTime.TimeOfDay.TotalHours;
                            eh = ev.EndTime.TimeOfDay.TotalHours;
                        }
                        else if (day == startDay)
                        {
                            sh = ev.StartTime.TimeOfDay.TotalHours;
                            eh = 24;
                        }
                        else if (day == endDay)
                        {
                            sh = 0;
                            eh = ev.EndTime.TimeOfDay.TotalHours;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    if (eh > sh)
                        segments.Add((ev, sh, eh));
                }

                if (segments.Count == 0)
                    continue;

                var clusters = new List<List<(Event ev, double sh, double eh)>>();

                foreach (var seg in segments.OrderBy(s => s.sh))
                {
                    var cluster = clusters.FirstOrDefault(c => c.Any(x => x.sh < seg.eh && seg.sh < x.eh));
                    if (cluster == null)
                    {
                        cluster = new List<(Event, double, double)>();
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
                        int overlapCount = colCount <= 1 ? 1 : colCount;
                        int overlapIndex = colCount <= 1 ? 0 : colIndex[seg.ev];

                        seg.ev.HasCollision = cluster.Any(other =>
                            !ReferenceEquals(other.ev, seg.ev) &&
                            other.sh < seg.eh &&
                            seg.sh < other.eh &&
                            seg.ev.EventType.ShouldShowCollisionAgainst(other.ev.EventType));

                        var ctl = new CalendarEventControl(seg.ev)
                        {
                            DayIndex = day,
                            StartHour = seg.sh,
                            EndHour = seg.eh,
                            OverlapCount = overlapCount,
                            OverlapIndex = overlapIndex
                        };

                        ctl.PointerPressed += (_, a) =>
                        {
                            a.Handled = true;
                            OnEventClicked(seg.ev);
                        };

                        _calendarPanel.Children.Add(ctl);
                    }
                }
            }
        }

        private void FillWeekDays()
        {
            WeekDays.Clear();

            var names = new[] { "Po", "Út", "St", "Čt", "Pá", "So", "Ne" };
            for (int i = 0; i < 7; i++)
            {
                var d = StartOfWeek.AddDays(i);
                WeekDays.Add(new WeekDayInfo
                {
                    DayName = names[i],
                    Date = d,
                    IsToday = d.Date == DateTime.Today
                });
            }
        }

        private async Task ApplyEventChangeAsync(Event edited, Event? original)
        {
            edited.EmployeeId = _employeeId;

            ChangedRange changedRange;

            if (edited.IsDeleted)
            {
                bool cascadeDelete = original?.ParentEventId == null;
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

        private static DateTime StartOfWeeks(DateTime date)
        {
            int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return date.Date.AddDays(-diff);
        }

        private static string BuildWeekTitle(DateTime weekStart, CultureInfo culture)
        {
            var weekEnd = weekStart.AddDays(6);

            if (weekStart.Month == weekEnd.Month && weekStart.Year == weekEnd.Year)
                return $"{weekStart:dd}–{weekEnd:dd} {weekEnd.ToString("MMM yyyy", culture)}";

            if (weekStart.Year == weekEnd.Year)
                return $"{weekStart.ToString("dd MMM", culture)}–{weekEnd.ToString("dd MMM yyyy", culture)}";

            return $"{weekStart.ToString("dd MMM yyyy", culture)}–{weekEnd.ToString("dd MMM yyyy", culture)}";
        }

        private static int DayOfWeekNumber(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            DayOfWeek.Sunday => 7,
            _ => 1
        };

        public class WeekDayInfo
        {
            public string DayName { get; set; } = string.Empty;
            public DateTime Date { get; set; }
            public bool IsToday { get; set; }
        }
    }
}
