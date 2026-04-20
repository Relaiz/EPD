using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;
using Avalonia.Media;
using System.Linq;
using System.Reactive.Linq;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Helpers;
using Avalonia.Controls;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace TeacherScheduleApp.ViewModels
{
    public class MonthViewModel : ViewModelBase
    {
        private readonly EventService _eventService = new EventService();
        private readonly int _employeeId = EventService.DefaultEmployeeId;

        private DateTime _currentMonth;
        public DateTime CurrentMonth
        {
            get => _currentMonth;
            set => this.RaiseAndSetIfChanged(ref _currentMonth, value);
        }

        private DateTime _currentDate;
        public DateTime CurrentDate
        {
            get => _currentDate;
            set => this.RaiseAndSetIfChanged(ref _currentDate, value);
        }

        public ObservableCollection<MonthDayInfo> Days { get; } = new();

        public ReactiveCommand<Unit, Unit> PreviousMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> NextMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }

        private bool _isDialogOpen;
        private readonly Action<DateTime> _onDateChanged;

        public MonthViewModel(DateTime date, Action<DateTime> onDateChanged)
        {
            CurrentDate = date.Date;
            _onDateChanged = onDateChanged;
            CurrentMonth = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);

            PreviousMonthCommand = ReactiveCommand.Create(() =>
            {
                CurrentMonth = CurrentMonth.AddMonths(-1);
                _onDateChanged?.Invoke(CurrentMonth);
                FillDays();
                LoadEvents();
            });

            NextMonthCommand = ReactiveCommand.Create(() =>
            {
                CurrentMonth = CurrentMonth.AddMonths(1);
                _onDateChanged?.Invoke(CurrentMonth);
                FillDays();
                LoadEvents();
            });

            TodayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = DateTime.Today;
                CurrentMonth = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
                _onDateChanged?.Invoke(CurrentMonth);
                FillDays();
                LoadEvents();
            });

            MessageBus.Current
                .Listen<UserSettingsChangedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    FillDays();
                    LoadEvents();
                });

            MessageBus.Current
                .Listen<AutoEventsGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    FillDays();
                    LoadEvents();
                });

            MessageBus.Current
                .Listen<GlobalSettingsChangedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    FillDays();
                    LoadEvents();
                });

            FillDays();
            LoadEvents();
        }

        public void FillDays()
        {
            Days.Clear();

            var firstDay = CurrentMonth;
            int offset = ((int)firstDay.DayOfWeek + 6) % 7; // Monday = 0
            var startDate = firstDay.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                var date = startDate.AddDays(i);

                Days.Add(new MonthDayInfo
                {
                    Date = date,
                    IsCurrentMonth = date.Month == CurrentMonth.Month,
                    IsWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    IsHoliday = HolidayHelper.IsCzechHoliday(date),
                    HasEvent = false
                });
            }
        }

        public void LoadEvents()
        {
            if (Days.Count == 0)
                return;

            var gridStart = Days.First().Date.Date;
            var gridEndExclusive = Days.Last().Date.AddDays(1).Date;

            var events = _eventService.GetEventsForRange(_employeeId, gridStart, gridEndExclusive);

            foreach (var day in Days)
            {
                day.Events.Clear();
                day.HasEvent = false;
            }

            foreach (var ev in events.Where(e => !e.IsDeleted))
            {
                foreach (var cell in Days)
                {
                    if (cell.IsWeekend)
                        continue;

                    if (ev.StartTime.Date <= cell.Date.Date && ev.EndTime.Date >= cell.Date.Date)
                    {
                        cell.Events.Add(ev);
                        cell.HasEvent = true;
                    }
                }
            }

            bool IsWorkLike(Event e) => e.EventType == EventType.Work || e.EventType == EventType.BusinessTrip;
            bool IsSpecial(Event e) => e.EventType != EventType.Lunch && !IsWorkLike(e);

            foreach (var cell in Days)
            {
                var specials = cell.Events
                    .Where(IsSpecial)
                    .Select(e => (e.StartTime, e.EndTime))
                    .ToList();

                foreach (var e in cell.Events)
                {
                    bool isSpecial = IsSpecial(e);
                    e.IsInactive = !isSpecial && specials.Any(sp => e.StartTime < sp.EndTime && sp.StartTime < e.EndTime);
                }
            }
        }

        public async void OnEmptySpaceClicked(MonthDayInfo dayInfo)
        {
            if (_isDialogOpen)
                return;

            _isDialogOpen = true;

            try
            {
                var main = Helpers.Helper.GetMainWindow();
                if (main == null)
                    return;

                var resolved = SettingsService.GetResolvedDaySettings(dayInfo.Date, _employeeId);

                var dlg = new Views.CreateEventDialog();
                var vm = new CreateEventDialogViewModel(dayInfo.Date + resolved.ArrivalTime);

                vm.EndDate = dayInfo.Date;
                vm.EndTime = (resolved.ArrivalTime + TimeSpan.FromHours(1) <= resolved.DepartureTime)
                    ? resolved.ArrivalTime + TimeSpan.FromHours(1)
                    : resolved.DepartureTime;

                dlg.DataContext = vm;

                var ev = await dlg.ShowDialog<Event>(main);
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
                await ApplyEventChangeAsync(ev, null);
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        public async void OnEventClicked(Event ev)
        {
            if (_isDialogOpen)
                return;

            _isDialogOpen = true;

            try
            {
                var main = Helpers.Helper.GetMainWindow();
                if (main == null)
                    return;

                var oldStart = ev.StartTime.Date;
                var oldEnd = ev.EndTime.Date;

                var dlg = new Views.CreateEventDialog();
                var vm = new CreateEventDialogViewModel(ev.StartTime)
                {
                    Id = ev.Id,
                    Title = ev.Title,
                    Description = ev.Description,
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
                if (updated == null)
                    return;
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

        public class MonthDayInfo : ReactiveObject
        {
            private bool _hasEvent;

            public DateTime Date { get; set; }
            public bool IsCurrentMonth { get; set; }
            public bool IsWeekend { get; set; }
            public bool IsHoliday { get; set; }

            public bool HasEvent
            {
                get => _hasEvent;
                set => this.RaiseAndSetIfChanged(ref _hasEvent, value);
            }

            public int DayNumber => Date.Day;
            public bool IsToday => Date.Date == DateTime.Today;

            public ObservableCollection<Event> Events { get; } = new();

            public IBrush DayBackground
            {
                get
                {
                    if (IsToday) return Brushes.LightGray;
                    if (!IsCurrentMonth) return Brushes.DarkGray;
                    if (IsWeekend) return new SolidColorBrush(Color.Parse("#EEEEEE"));
                    return Brushes.White;
                }
            }

            public IBrush DayNumberForeground
            {
                get
                {
                    if (!IsCurrentMonth) return Brushes.Gray;
                    if (IsHoliday) return Brushes.Red;
                    return Brushes.Black;
                }
            }
        }
    }
}