using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using TeacherScheduleApp.Services;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Controls;
using System.Collections.Generic;
using System.Reactive.Linq;
using TeacherScheduleApp.Messages;
using System.Linq;
using Avalonia.Controls.Documents;

namespace TeacherScheduleApp.ViewModels
{
    public class WeekViewModel : ViewModelBase
    {
        private CalendarPanel? _calendarPanel;
        private readonly EventService _eventService = new EventService();
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
        public List<int> GridCells { get; set; }
        public ObservableCollection<WeekDayInfo> WeekDays { get; } = new ObservableCollection<WeekDayInfo>();
        public ObservableCollection<string> Hours { get; } = new ObservableCollection<string>();

        private bool _isDialogOpen = false;
        public ReactiveCommand<Unit, Unit> PreviousWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> NextWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }

        private Action<DateTime> _onDateChanged;

        private DateTime _currentDate;
        public DateTime CurrentDate
        {
            get => _currentDate;
            set => this.RaiseAndSetIfChanged(ref _currentDate, value);
        }

        public WeekViewModel(DateTime date, Action<DateTime> onDateChanged)
        {
            CurrentDate = date.Date;
            _onDateChanged = onDateChanged;

            for (int i = 0; i < 24; i++)
            {
                Hours.Add($"{i:00}:00");
            }

            int delta = DayOfWeekNumber(CurrentDate.DayOfWeek) - 1;
            StartOfWeek = CurrentDate.AddDays(-delta).Date;
            EndOfWeek = StartOfWeek.AddDays(6);

            FillWeekDays();
            MessageBus.Current
     .Listen<AutoEventsGeneratedMessage>()
     .ObserveOn(RxApp.MainThreadScheduler)   
     .Subscribe(_ => LoadEvents()); 
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
            GridCells = new List<int>();
            for (int i = 0; i < 48 * 7; i++)
                GridCells.Add(i);
            TodayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = DateTime.Now;
                int diff = DayOfWeekNumber(CurrentDate.DayOfWeek) - 1;
                StartOfWeek = CurrentDate.AddDays(-diff).Date;
                EndOfWeek = StartOfWeek.AddDays(6);
                _onDateChanged?.Invoke(CurrentDate);
                FillWeekDays();
                LoadEvents();
               
            });
        }

        public async void OnEmptySpaceClicked(int dayIndex, double hour)
        {
            if (_isDialogOpen)
                return;

            _isDialogOpen = true;
            try
            {
                int hourInt = (int)Math.Floor(hour);
                DateTime eventStart = StartOfWeek.AddDays(dayIndex).AddHours(hourInt);
                DateTime eventEnd = eventStart.AddHours(1);

                Window mainWindow = Helpers.Helper.GetMainWindow();
                if (mainWindow == null)
                    return;
                var existingEvent = _eventService.FindEventByStartTime(eventStart);
                var dialog = new Views.CreateEventDialog();
                dialog.Closed += (_, __) =>
                {
                    _isDialogOpen = false;
                };
                CreateEventDialogViewModel dialogVm;
                if (existingEvent != null)
                {
                    dialogVm = new CreateEventDialogViewModel(existingEvent.StartTime)
                    {
                        Id = existingEvent.Id,
                        Title = existingEvent.Title,
                        Description = existingEvent.Description,
                        AllDay = existingEvent.AllDay,
                        StartDate = existingEvent.StartTime.Date,
                        StartTime = existingEvent.StartTime.TimeOfDay,
                        EndDate = existingEvent.EndTime.Date,
                        EndTime = existingEvent.EndTime.TimeOfDay
                    };
                }
                else
                {
                    dialogVm = new CreateEventDialogViewModel(eventStart)
                    {
                        EndDate = eventEnd.Date,
                        EndTime = eventEnd.TimeOfDay
                    };
                }
                dialog.DataContext = dialogVm;
                var resultEvent = await dialog.ShowDialog<Event>(mainWindow);
                if (resultEvent != null)
                {
                    if (resultEvent.IsDeleted)
                    {
                        _eventService.DeleteEvent(resultEvent.Id);
                        MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                    }
                    else if (resultEvent.Id != 0)
                    {
                        resultEvent.IsAutoGenerated = false;
                        _eventService.UpdateEvent(resultEvent);
                        MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                    }
                    else
                    {
                        _eventService.CreateEvent(resultEvent);
                        MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                    }
                    LoadEvents();
                }
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        private async void OnEventClicked(Event ev)
        {
            if (_isDialogOpen)
                return;

            _isDialogOpen = true;
            try
            {
                Window mainWindow = Helpers.Helper.GetMainWindow();
                if (mainWindow == null)
                    return;

                var dialog = new Views.CreateEventDialog();
                dialog.Closed += (_, __) =>
                {
                    _isDialogOpen = false;
                };
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
                    {
                        _eventService.DeleteEvent(updatedEvent.Id);
                        MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                    }
                    else
                    {
                        updatedEvent.IsAutoGenerated = false;
                        _eventService.UpdateEvent(updatedEvent);
                        MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                    }
                    LoadEvents();
                }
            }
            finally
            {
                _isDialogOpen = false;
            }
        }
        public void AttachCalendarPanel(CalendarPanel panel)
        {
            _calendarPanel = panel;
            _calendarPanel.DayHourClicked += (dayIndex, hour) => OnEmptySpaceClicked(dayIndex, hour);
            LoadEvents();
        }


        public void LoadEvents()
        {
            if (_calendarPanel == null) return;
            _calendarPanel.Children.Clear();

            var events = _eventService.GetEventsForWeek(CurrentDate);

            var position = new Dictionary<Event, (int Index, int Count)>();
            var collisions = new Dictionary<Event, bool>();

            var eventsByDay = events
                .GroupBy(e => (e.StartTime.Date - StartOfWeek.Date).Days);

            foreach (var dayGroup in eventsByDay)
            {
                var manual = dayGroup
                    .Where(e => !e.IsAutoGenerated && e.EventType == EventType.Work)
                    .OrderBy(e => e.StartTime)
                    .ToList();

                var colliding = new HashSet<Event>();
                for (int i = 0; i < manual.Count; i++)
                {
                    for (int j = i + 1; j < manual.Count; j++)
                    {
                        var a = manual[i];
                        var b = manual[j];
                        if (a.StartTime < b.EndTime && b.StartTime < a.EndTime)
                        {
                            colliding.Add(a);
                            colliding.Add(b);
                        }
                    }
                }

                foreach (var ev in dayGroup)
                    collisions[ev] = colliding.Contains(ev);

                var overlapGroups = new List<List<Event>>();
                foreach (var e in colliding)
                {
                    var g = overlapGroups
                        .FirstOrDefault(gr => gr.Any(x =>
                            x.StartTime < e.EndTime && e.StartTime < x.EndTime));
                    if (g != null) g.Add(e);
                    else overlapGroups.Add(new List<Event> { e });
                }

                foreach (var grp in overlapGroups)
                {
                    for (int k = 0; k < grp.Count; k++)
                        position[grp[k]] = (k, grp.Count);
                }
            }

            foreach (var ev in events)
            {
                int startDay = (ev.StartTime.Date - StartOfWeek.Date).Days;
                int endDay = (ev.EndTime.Date - StartOfWeek.Date).Days;
                if (endDay < 0 || startDay > 6)
                    continue;

                for (int day = Math.Max(0, startDay); day <= Math.Min(6, endDay); day++)
                {
                    double sh = day == startDay
                        ? ev.StartTime.Hour + ev.StartTime.Minute / 60.0
                        : 0;
                    double eh = day == endDay
                        ? ev.EndTime.Hour + ev.EndTime.Minute / 60.0
                        : 24;

                    if (eh <= sh)
                        continue;

                    ev.HasCollision = collisions.TryGetValue(ev, out var c) && c;

                    var ctl = new CalendarEventControl(ev)
                    {
                        DayIndex = day,
                        StartHour = sh,
                        EndHour = eh,
                        OverlapCount = 1,
                        OverlapIndex = 0
                    };

                    if (position.TryGetValue(ev, out var p))
                    {
                        ctl.OverlapIndex = p.Index;
                        ctl.OverlapCount = p.Count;
                    }

                    ctl.PointerPressed += (s, e2) =>
                    {
                        e2.Handled = true;
                        OnEventClicked(ev);
                    };

                    _calendarPanel.Children.Add(ctl);
                }
            }
        }



        private void FillWeekDays()
        {
            WeekDays.Clear();
            var dayNames = new[] { "Po", "Út", "St", "Čt", "Pá", "So", "Ne" };
            var temp = StartOfWeek;
            for (int i = 0; i < 7; i++)
            {
                bool isToday = (temp.Date == DateTime.Now.Date);
                WeekDays.Add(new WeekDayInfo
                {
                    DayName = dayNames[i],
                    Date = temp,
                    IsToday = isToday
                });
                temp = temp.AddDays(1);
            }
        }

        private int DayOfWeekNumber(DayOfWeek day)
        {
            return day switch
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
        }

        public class WeekDayInfo
        {
            public string DayName { get; set; }
            public DateTime Date { get; set; }
            public bool IsToday { get; set; }
        }         
    }
}
