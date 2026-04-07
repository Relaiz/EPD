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

            foreach (var cell in Days)
            {
                var specials = cell.Events
                    .Where(e => e.EventType != EventType.Work && e.EventType != EventType.Lunch)
                    .Select(e => (e.StartTime, e.EndTime))
                    .ToList();

                foreach (var e in cell.Events)
                {
                    bool isSpecial = e.EventType != EventType.Work && e.EventType != EventType.Lunch;
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

                ev.EmployeeId = _employeeId;

                if (ev.IsDeleted)
                {
                    _eventService.DeleteEvent(ev.Id, _employeeId);

                    await new AutomaticEventsGeneratorService(
                        _eventService,
                        _ => System.Threading.Tasks.Task.FromResult(true),
                        _employeeId)
                        .RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
                }
                else if (ev.Id != 0)
                {
                    _eventService.UpdateEvent(ev);

                    await new AutomaticEventsGeneratorService(
                        _eventService,
                        _ => System.Threading.Tasks.Task.FromResult(true),
                        _employeeId)
                        .RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
                }
                else
                {
                    _eventService.CreateEvent(ev);

                    await new AutomaticEventsGeneratorService(
                        _eventService,
                        _ => System.Threading.Tasks.Task.FromResult(true),
                        _employeeId)
                        .RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
                }

                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(ev.StartTime.Date));
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                LoadEvents();
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

                updated.EmployeeId = _employeeId;

                if (updated.IsDeleted)
                {
                    if (updated.ParentEventId == null)
                        _eventService.DeleteEventCascadeAndCleanup(updated.Id, _employeeId);
                    else
                        _eventService.DeleteEvent(updated.Id, _employeeId);
                }
                else if (updated.Id != 0)
                {
                    _eventService.UpdateEvent(updated);

                    var from = oldStart < updated.StartTime.Date ? oldStart : updated.StartTime.Date;
                    var to = oldEnd > updated.EndTime.Date ? oldEnd : updated.EndTime.Date;

                    var generator = new AutomaticEventsGeneratorService(
                        _eventService,
                        _ => System.Threading.Tasks.Task.FromResult(true),
                        _employeeId);

                    await generator.RegenerateRangeEventsAsync(from, to);
                }
                else
                {
                    _eventService.CreateEvent(updated);

                    var generator = new AutomaticEventsGeneratorService(
                        _eventService,
                        _ => System.Threading.Tasks.Task.FromResult(true),
                        _employeeId);

                    await generator.RegenerateRangeEventsAsync(updated.StartTime.Date, updated.EndTime.Date);
                }

                MessageBus.Current.SendMessage(new UserSettingsChangedMessage((updated ?? ev).StartTime.Date));
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                LoadEvents();
            }
            finally
            {
                _isDialogOpen = false;
            }
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