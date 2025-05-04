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

        public ObservableCollection<MonthDayInfo> Days { get; } = new ObservableCollection<MonthDayInfo>();

        public ReactiveCommand<Unit, Unit> PreviousMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> NextMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }

        private bool _isDialogOpen;
        private Action<DateTime> _onDateChanged;

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
                CurrentDate = CurrentMonth;
                CurrentMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                _onDateChanged?.Invoke(CurrentMonth);
                FillDays();
                LoadEvents();
            });
            MessageBus.Current
                .Listen<AutoEventsGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => LoadEvents());
            FillDays();
            LoadEvents();
        }

        public void FillDays()
        {
            Days.Clear();
            var firstDay = CurrentMonth;
            int offset = (int)firstDay.DayOfWeek;
            if (offset == 0)
                offset = 7;
            var startDate = firstDay.AddDays(-(offset - 1));

            for (int i = 0; i < 42; i++)
            {
                var date = startDate.AddDays(i);
                bool isCurrent = (date.Month == CurrentMonth.Month);

                Days.Add(new MonthDayInfo
                {
                    Date = date,
                    IsCurrentMonth = isCurrent,
                    IsWeekend = (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday),
                    IsHoliday = HolidayHelper.IsCzechHoliday(date),
                    HasEvent = false
                    
                });
            }
        }

        public void LoadEvents()
        {
            var startOfMonth = CurrentMonth;
            var endOfMonth = startOfMonth.AddMonths(1);
            var events = _eventService.GetEventsForRange(startOfMonth, endOfMonth);

            foreach (var day in Days)
            {
                day.Events.Clear();
                day.HasEvent = false;
            }

            foreach (var ev in events)
            {
                foreach (var day in Days)
                {
                    if (ev.StartTime.Date <= day.Date && ev.EndTime.Date >= day.Date && !ev.IsDeleted)
                    {
                        day.Events.Add(ev);
                        day.HasEvent = true;
                    }
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
                var mainWindow = Helpers.Helper.GetMainWindow();
                if (mainWindow == null)
                    return;

                var dialog = new Views.CreateEventDialog();
                var dialogVm = new CreateEventDialogViewModel(dayInfo.Date);
                dialog.DataContext = dialogVm;

                var resultEvent = await dialog.ShowDialog<Event>(mainWindow);
                if (resultEvent != null)
                {
                    if (resultEvent.Id != 0)
                    {
                        resultEvent.IsAutoGenerated = false;
                        _eventService.UpdateEvent(resultEvent);
                    }
                    else
                        _eventService.CreateEvent(resultEvent);
                    LoadEvents();
                }
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
                var mainWindow = Helpers.Helper.GetMainWindow();
                if (mainWindow == null)
                    return;

                var dialog = new Views.CreateEventDialog();
                dialog.Closed += (_, __) => { _isDialogOpen = false; };

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
                vm.SelectedEventTypePair = vm.LocalizedEventTypes
                           .First(kvp => kvp.Key == ev.EventType);

                dialog.DataContext = vm;
                var updatedEvent = await dialog.ShowDialog<Event>(mainWindow);
                if (updatedEvent != null)
                {
                    if (updatedEvent.IsDeleted)
                        _eventService.DeleteEvent(updatedEvent.Id);
                    else
                        updatedEvent.IsAutoGenerated = false;
                        _eventService.UpdateEvent(updatedEvent);
                    LoadEvents();
                }
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

            public ObservableCollection<Event> Events { get; } = new ObservableCollection<Event>();

            public IBrush DayBackground
            {
                get
                {
                    if (IsToday)
                        return Brushes.LightGray;
                    else if (!IsCurrentMonth)
                        return Brushes.DarkGray;
                    else if (IsWeekend)
                         return new SolidColorBrush(Color.Parse("#EEEEEE"));
                    else
                        return Brushes.White;
                }
            }

            public IBrush DayNumberForeground
            {
                get
                {
                    if (!IsCurrentMonth)
                        return Brushes.Gray;
                    else if (IsHoliday)
                        return Brushes.Red;
                    else
                        return Brushes.Black;
                }
            }
        }
    }
}
