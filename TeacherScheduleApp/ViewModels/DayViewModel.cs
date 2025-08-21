using DynamicData.Aggregation;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection.Emit;
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
        private readonly EventService _eventService = new EventService();
        private CalendarPanel? _calendarPanel;
        private bool _isDialogOpen;
        static bool Intersects(DateTime a0, DateTime a1, DateTime b0, DateTime b1) => a0 < b1 && b0 < a1;
        public DateTime CurrentDate { get => _currentDate; set => this.RaiseAndSetIfChanged(ref _currentDate, value); }
        private DateTime _currentDate;

        public ObservableCollection<string> Hours { get; } = new();
        public ObservableCollection<CellInfo> GridCells { get; } = new ObservableCollection<CellInfo>();


        public ReactiveCommand<Unit, Unit> PreviousDayCommand { get; }
        public ReactiveCommand<Unit, Unit> NextDayCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }

        private readonly Action<DateTime> _onDateChanged;

        /// <summary>Inicializace view modelu dne</summary>
        public DayViewModel(DateTime date, Action<DateTime> onDateChanged)
        {
            CurrentDate = date.Date;
            _onDateChanged = onDateChanged;

            for (int i = 0; i < 24; i++)
                Hours.Add($"{i:00}:00");
            RebuildAll();
            MessageBus.Current
                .Listen<AutoEventsGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    RebuildAll();
                });

            MessageBus.Current
                .Listen<UserSettingsChangedMessage>()
                .Where(m => m.Date == CurrentDate)
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
                LoadEvents();
            });

            NextDayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = CurrentDate.AddDays(1);
                _onDateChanged(CurrentDate);
                LoadEvents();
            });

            TodayCommand = ReactiveCommand.Create(() =>
            {
                CurrentDate = DateTime.Today;
                _onDateChanged(CurrentDate);
                LoadEvents();
            });
        }

        /// <summary>Připojí panel kalendáře</summary>
        public void AttachCalendarPanel(CalendarPanel panel)
        {
            _calendarPanel = panel;
            _calendarPanel.DayHourClicked += (_, hour) => OnEmptySpaceClicked(hour);
            RebuildAll();
        }

        /// <summary>Přestaví mřížku a načte události</summary>
        private void RebuildAll()
        {
            GridCells.Clear();

            var sem = GlobalSettingsService.GetSemesterForDate(CurrentDate);
            var global = GlobalSettingsService.LoadGlobalSettings(CurrentDate.Year, sem)
                        ?? GlobalSettingsService.GetDefaultSettings(CurrentDate.Year, sem);
            var user = SettingsService.GetUserSettingsForDate(CurrentDate);

            string defArr, defDep;
            switch (CurrentDate.DayOfWeek)
            {
                case DayOfWeek.Monday: defArr = global.MondayArrival; defDep = global.MondayDeparture; break;
                case DayOfWeek.Tuesday: defArr = global.TuesdayArrival; defDep = global.TuesdayDeparture; break;
                case DayOfWeek.Wednesday: defArr = global.WednesdayArrival; defDep = global.WednesdayDeparture; break;
                case DayOfWeek.Thursday: defArr = global.ThursdayArrival; defDep = global.ThursdayDeparture; break;
                case DayOfWeek.Friday: defArr = global.FridayArrival; defDep = global.FridayDeparture; break;
                default: defArr = "00:00"; defDep = "00:00"; break;
            }

            var arrival = user?.ArrivalTime ?? TimeSpan.Parse(defArr);
            var departure = user?.DepartureTime ?? TimeSpan.Parse(defDep);
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

        /// <summary>Načte a vykreslí události dne</summary>
        public void LoadEvents()
        {
            if (_calendarPanel == null) return;
            _calendarPanel.Children.Clear();

            var sem = GlobalSettingsService.GetSemesterForDate(CurrentDate);
            var global = GlobalSettingsService.LoadGlobalSettings(CurrentDate.Year, sem)
                        ?? GlobalSettingsService.GetDefaultSettings(CurrentDate.Year, sem);
            var user = SettingsService.GetUserSettingsForDate(CurrentDate);

            string defArr, defDep;
            switch (CurrentDate.DayOfWeek)
            {
                case DayOfWeek.Monday: defArr = global.MondayArrival; defDep = global.MondayDeparture; break;
                case DayOfWeek.Tuesday: defArr = global.TuesdayArrival; defDep = global.TuesdayDeparture; break;
                case DayOfWeek.Wednesday: defArr = global.WednesdayArrival; defDep = global.WednesdayDeparture; break;
                case DayOfWeek.Thursday: defArr = global.ThursdayArrival; defDep = global.ThursdayDeparture; break;
                case DayOfWeek.Friday: defArr = global.FridayArrival; defDep = global.FridayDeparture; break;
                default: defArr = "00:00"; defDep = "00:00"; break;
            }

            double arr = (user?.ArrivalTime ?? TimeSpan.Parse(defArr)).TotalHours;
            double dep = (user?.DepartureTime ?? TimeSpan.Parse(defDep)).TotalHours;

            if (arr > 0)
                _calendarPanel.Children.Add(new CalendarBackgroundBlock { DayIndex = 0, StartHour = 0, EndHour = arr });
            if (dep < 24)
                _calendarPanel.Children.Add(new CalendarBackgroundBlock { DayIndex = 0, StartHour = dep, EndHour = 24 });

            var events = _eventService.GetEventsForDay(CurrentDate).Where(e => !e.IsDeleted).ToList();

            var specials = events
                .Where(e => e.EventType != EventType.Work && e.EventType != EventType.Lunch)
                .Select(e => (e.StartTime, e.EndTime))
                .ToList();

            foreach (var e in events)
            {
                bool isSpecial = e.EventType != EventType.Work && e.EventType != EventType.Lunch;
                var eS = e.AllDay ? e.ArrivalTime : e.StartTime;
                var eE = e.AllDay ? e.DepartureTime : e.EndTime;
                e.IsInactive = !isSpecial && specials.Any(sp => eS < sp.EndTime && sp.StartTime < eE);
            }

            var segments = new List<(Event ev, double sh, double eh)>();
            foreach (var ev in events)
            {
                double sh = ev.AllDay ? ev.ArrivalTime.TimeOfDay.TotalHours : ev.StartTime.TimeOfDay.TotalHours;
                double eh = ev.AllDay ? ev.DepartureTime.TimeOfDay.TotalHours : ev.EndTime.TimeOfDay.TotalHours;
                if (eh > sh) segments.Add((ev, sh, eh));
            }
            if (segments.Count == 0) return;

            var clusters = new List<List<(Event ev, double sh, double eh)>>();
            foreach (var seg in segments.OrderBy(s => s.sh))
            {
                var cluster = clusters.FirstOrDefault(c => c.Any(x => x.sh < seg.eh && seg.sh < x.eh));
                if (cluster == null) { cluster = new List<(Event, double, double)>(); clusters.Add(cluster); }
                cluster.Add(seg);
            }

            foreach (var cluster in clusters)
            {
                var columns = new List<double>();
                var colIndex = new Dictionary<Event, int>();

                foreach (var seg in cluster.OrderBy(s => s.sh))
                {
                    int idx = columns.FindIndex(end => end <= seg.sh);
                    if (idx < 0) { columns.Add(seg.eh); idx = columns.Count - 1; }
                    else { columns[idx] = seg.eh; }
                    colIndex[seg.ev] = idx;
                }

                int colCount = columns.Count;
                foreach (var seg in cluster)
                {
                    seg.ev.HasCollision = colCount > 1;

                    var ctrl = new CalendarEventControl(seg.ev)
                    {
                        DayIndex = 0,
                        StartHour = seg.sh,
                        EndHour = seg.eh,
                        OverlapCount = (colCount <= 1) ? 1 : colCount,
                        OverlapIndex = (colCount <= 1) ? 0 : colIndex[seg.ev]
                    };
                    ctrl.PointerPressed += (_, a) => { a.Handled = true; OnEventClicked(seg.ev); };
                    _calendarPanel.Children.Add(ctrl);
                }
            }
        }

        /// <summary>Vytvoří novou událost</summary>
        private async void OnEmptySpaceClicked(double hour)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;

            double snapped = Math.Round(hour);
            var start = CurrentDate.AddHours(snapped);
            var end = start.AddHours(1);
            var win = Helper.GetMainWindow();
            if (win == null) { _isDialogOpen = false; return; }

            var dlg = new CreateEventDialog
            {
                DataContext = new CreateEventDialogViewModel(start)
                {
                    EndDate = end.Date,
                    EndTime = end.TimeOfDay
                }
            };
            dlg.Closed += (_, __) => _isDialogOpen = false;
            var ev = await dlg.ShowDialog<Event>(win);
            if (ev == null) { _isDialogOpen = false; return; }

            if (ev.IsDeleted)
            {
                if (ev.ParentEventId == null)
                {
                    var all = _eventService.GetAllEvents()
                            .Where(x => x.Id == ev.Id || x.ParentEventId == ev.Id)
                            .ToList();
                    foreach (var e in all)
                        _eventService.DeleteEvent(e.Id);
                }
                else
                {
                    _eventService.DeleteEvent(ev.Id);
                }
            }
            else if (ev.Id != 0)
            {
                var old = _eventService.GetEventById(ev.Id);
                _eventService.UpdateEvent(ev);

               
            }
            else
            {
                _eventService.CreateEvent(ev);
            }

            var generator = new AutomaticEventsGeneratorService(_eventService, _ => System.Threading.Tasks.Task.FromResult(true));
            await generator.RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
            
            MessageBus.Current.SendMessage(new UserSettingsChangedMessage(CurrentDate));
            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
            RebuildAll();
            _isDialogOpen = false;
        }

        /// <summary>Upraví existující událost</summary>
        private async void OnEventClicked(Event existing)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;

            var win = Helper.GetMainWindow();
            if (win == null) { _isDialogOpen = false; return; }

            var vm = new CreateEventDialogViewModel(existing.StartTime)
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                AllDay = existing.AllDay,
                StartTime = existing.StartTime.TimeOfDay,
                EndTime = existing.EndTime.TimeOfDay,
                ArrivalTime = existing.ArrivalTime,
                DepartureTime = existing.DepartureTime,
                LunchStart = existing.LunchStart,
                LunchEnd = existing.LunchEnd,
                EventType = existing.EventType
            };
            vm.StartDate = existing.StartTime.Date;
            vm.EndDate = existing.EndTime.Date;

            vm.SelectedEventTypePair = vm.LocalizedEventTypes.First(kvp => kvp.Key == existing.EventType);

            var dlg = new CreateEventDialog { DataContext = vm };
            dlg.Closed += (_, __) => _isDialogOpen = false;
            var ev = await dlg.ShowDialog<Event>(win);
            if (ev == null) { _isDialogOpen = false; return; }
            var oldStart = existing.StartTime.Date;
            var oldEnd = existing.EndTime.Date;

            if (ev.IsDeleted)
            {
                if (ev.ParentEventId == null)
                {
                    var all = _eventService.GetAllEvents().Where(x => x.Id == ev.Id || x.ParentEventId == ev.Id).ToList();
                    foreach (var e2 in all)
                        _eventService.DeleteEvent(e2.Id);
                }
                else
                {
                    _eventService.DeleteEvent(ev.Id);
                }
            }
            else if (ev.Id != 0)
            {
                _eventService.UpdateEvent(ev);

                var from = oldStart < ev.StartTime.Date ? oldStart : ev.StartTime.Date;
                var to = oldEnd > ev.EndTime.Date ? oldEnd : ev.EndTime.Date;

                var generator = new AutomaticEventsGeneratorService(
                    _eventService, _ => System.Threading.Tasks.Task.FromResult(true));
                await generator.RegenerateRangeEventsAsync(from, to);

            }
            else
            {
                var parent = new Event
                {
                    Title = ev.Title,
                    Description = ev.Description,
                    EventType = ev.EventType,
                    AllDay = ev.AllDay,
                    StartTime = ev.StartTime,
                    EndTime = ev.EndTime,
                    ArrivalTime = ev.ArrivalTime,
                    DepartureTime = ev.DepartureTime,
                    LunchStart = ev.LunchStart,
                    LunchEnd = ev.LunchEnd,
                    ParentEventId = null,
                    IsAutoGenerated = false
                };
                _eventService.CreateEvent(parent);
                var generator = new AutomaticEventsGeneratorService(
                _eventService, _ => System.Threading.Tasks.Task.FromResult(true));
                await generator.RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);

            }
            MessageBus.Current.SendMessage(new UserSettingsChangedMessage(CurrentDate));
            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
            RebuildAll();

            _isDialogOpen = false;
        }

        public class CellInfo
        {
            public int DayIndex { get; init; }
            public int HourIndex { get; init; }
            public double WorkStart { get; init; }
            public double WorkEnd { get; init; }
            public bool IsHoliday { get; init; }
            public bool IsWorkingHour => !IsHoliday
                && HourIndex >= (int)Math.Floor(WorkStart)
                && HourIndex < (int)Math.Ceiling(WorkEnd);
        }
    }
}
