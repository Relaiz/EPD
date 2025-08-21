using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;
using Avalonia.Media;
using System.Collections.Generic;
using TeacherScheduleApp.Helpers;
using System.Linq;
using System.Reactive.Linq;
using TeacherScheduleApp.Messages;

namespace TeacherScheduleApp.ViewModels
{
    public class MonthViewModel : ViewModelBase
    {
        private readonly EventService _eventService = new EventService();
        private DateTime _currentMonth;
        public DateTime CurrentMonth
        {
            get => _currentMonth;
            set => this.RaiseAndSetIfChanged(ref _currentMonth, value);
        }

        public ObservableCollection<MonthDayInfo> Days { get; } = new();

        public ReactiveCommand<Unit, Unit> PreviousMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> NextMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }

        private bool _isDialogOpen;
        private readonly Action<DateTime> _onDateChanged;

        private DateTime _currentDate;
        public DateTime CurrentDate
        {
            get => _currentDate;
            set => this.RaiseAndSetIfChanged(ref _currentDate, value);
        }

        public MonthViewModel(DateTime date, Action<DateTime> onDateChanged)
        {
            CurrentDate = date;
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
                .Subscribe(_ => { FillDays(); LoadEvents(); });

            MessageBus.Current
                .Listen<AutoEventsGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => { FillDays(); LoadEvents(); });

            FillDays();
            LoadEvents();
        }

        public void FillDays()
        {
            Days.Clear();

            var firstDay = CurrentMonth;
            int offset = (int)firstDay.DayOfWeek;
            if (offset == 0) offset = 7;
            var startDate = firstDay.AddDays(-(offset - 1));

            for (int i = 0; i < 42; i++)
            {
                var date = startDate.AddDays(i);
                Days.Add(new MonthDayInfo
                {
                    Date = date,
                    IsCurrentMonth = (date.Month == CurrentMonth.Month),
                    IsWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    IsHoliday = HolidayHelper.IsCzechHoliday(date),
                    HasEvent = false
                });
            }
        }

        public void LoadEvents()
        {
            var gridStart = Days.First().Date.Date;
            var gridEndExclusive = Days.Last().Date.AddDays(1).Date;

            var events = _eventService.GetEventsForRange(gridStart, gridEndExclusive);

            foreach (var day in Days)
            {
                day.Events.Clear();
                day.HasEvent = false;
            }

            foreach (var ev in events.Where(e => !e.IsDeleted))
            {
                foreach (var cell in Days)
                {
                    if (cell.IsWeekend) continue;
                    if (ev.StartTime.Date <= cell.Date && ev.EndTime.Date >= cell.Date)
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
            if (_isDialogOpen) return;
            _isDialogOpen = true;
            try
            {
                var main = Helpers.Helper.GetMainWindow();
                if (main == null) return;

                var dlg = new Views.CreateEventDialog();
                var vm = new CreateEventDialogViewModel(dayInfo.Date.AddHours(8));
                dlg.DataContext = vm;

                var ev = await dlg.ShowDialog<Event>(main);
                if (ev == null) return;

                if (ev.IsDeleted)
                {
                    _eventService.DeleteEvent(ev.Id);
                    await new AutomaticEventsGeneratorService(_eventService, _ => System.Threading.Tasks.Task.FromResult(true))
                        .RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
                }
                else if (ev.Id != 0)
                {
                    _eventService.UpdateEvent(ev);
                    await new AutomaticEventsGeneratorService(_eventService, _ => System.Threading.Tasks.Task.FromResult(true))
                        .RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
                }
                else
                {
                    _eventService.CreateEvent(ev);
                    await new AutomaticEventsGeneratorService(_eventService, _ => System.Threading.Tasks.Task.FromResult(true))
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
            if (_isDialogOpen) return;
            _isDialogOpen = true;
            try
            {
                var main = Helpers.Helper.GetMainWindow();
                if (main == null) return;

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
                    EventType = ev.EventType,
                    ArrivalTime = ev.ArrivalTime,
                    DepartureTime = ev.DepartureTime,
                    LunchStart = ev.LunchStart,
                    LunchEnd = ev.LunchEnd
                };
                vm.SelectedEventTypePair = vm.LocalizedEventTypes.First(kvp => kvp.Key == ev.EventType);
                dlg.DataContext = vm;

                var updated = await dlg.ShowDialog<Event>(main);
                if (updated == null) return;

                if (updated.IsDeleted)
                {
                    if (updated.ParentEventId == null)
                        _eventService.DeleteEventCascadeAndCleanup(updated.Id);
                    else
                        _eventService.DeleteEvent(updated.Id);
                }
                else if (updated.Id != 0)
                {
                    _eventService.UpdateEvent(updated);
                    var from = oldStart < ev.StartTime.Date ? oldStart : ev.StartTime.Date;
                    var to = oldEnd > ev.EndTime.Date ? oldEnd : ev.EndTime.Date;

                    var generator = new AutomaticEventsGeneratorService(
                        _eventService, _ => System.Threading.Tasks.Task.FromResult(true));
                    await generator.RegenerateRangeEventsAsync(from, to);
                }
                else
                {
                    _eventService.CreateEvent(updated);
                    var generator = new AutomaticEventsGeneratorService(
                     _eventService, _ => System.Threading.Tasks.Task.FromResult(true));
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

        public class MonthDayInfo
        {
            public DateTime Date { get; set; }
            public bool IsCurrentMonth { get; set; }
            public bool IsWeekend { get; set; }
            public bool IsHoliday { get; set; }
            public bool HasEvent { get; set; }
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
