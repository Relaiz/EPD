using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using TeacherScheduleApp.Controls;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;

namespace TeacherScheduleApp.ViewModels
{
    public class DayViewModel : ViewModelBase
    {
        private readonly EventService _eventService = new EventService();
        private CalendarPanel? _calendarPanel;
        private bool _isDialogOpen;

        public DateTime CurrentDate { get => _currentDate; set => this.RaiseAndSetIfChanged(ref _currentDate, value); }
        private DateTime _currentDate;

        public ObservableCollection<string> Hours { get; } = new();
        public List<CellInfo> GridCells { get; } = new();

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
                    SaveDaySettingsFromEvents();
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
            var global = GlobalSettingsService.LoadGlobalSettings(sem)
                        ?? GlobalSettingsService.GetDefaultSettings(sem);
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

            var events = _eventService.GetEventsForDay(CurrentDate);

            var manual = events
                .Where(e => !e.IsAutoGenerated && e.EventType == EventType.Work)
                .OrderBy(e => e.StartTime)
                .ToList();

            var collisions = new HashSet<Event>();
            for (int i = 0; i < manual.Count; i++)
                for (int j = i + 1; j < manual.Count; j++)
                    if (manual[i].StartTime < manual[j].EndTime &&
                        manual[j].StartTime < manual[i].EndTime)
                    {
                        collisions.Add(manual[i]);
                        collisions.Add(manual[j]);
                    }

            foreach (var e in events)
                e.HasCollision = collisions.Contains(e);

            var groups = new List<List<Event>>();
            foreach (var e in manual.Where(collisions.Contains))
            {
                var grp = groups.FirstOrDefault(g =>
                    g.Any(x => x.StartTime < e.EndTime && e.StartTime < x.EndTime));
                if (grp != null) grp.Add(e);
                else groups.Add(new List<Event> { e });
            }

            var pos = new Dictionary<Event, (int idx, int cnt)>();
            foreach (var g in groups)
                for (int i = 0; i < g.Count; i++)
                    pos[g[i]] = (i, g.Count);

            foreach (var ev in events)
            {
                double sh = ev.AllDay
                    ? ev.ArrivalTime.TimeOfDay.TotalHours
                    : ev.StartHour;
                double eh = ev.AllDay
                    ? ev.DepartureTime.TimeOfDay.TotalHours
                    : ev.EndHour;

                if (eh <= sh) continue;

                var ctrl = new CalendarEventControl(ev)
                {
                    DayIndex = 0,
                    StartHour = sh,
                    EndHour = eh,
                    OverlapCount = pos.TryGetValue(ev, out var p) ? p.cnt : 1,
                    OverlapIndex = pos.TryGetValue(ev, out p) ? p.idx : 0
                };

                ctrl.PointerPressed += (_, args) =>
                {
                    args.Handled = true;
                    OnEventClicked(ev);
                };

                _calendarPanel.Children.Add(ctrl);
            }
        }

        /// <summary>Vytvoří novou událost</summary>
        private async void OnEmptySpaceClicked(double hour)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;

            var start = CurrentDate.AddHours(Math.Floor(hour));
            var end = start.AddHours(1);
            var win = Helper.GetMainWindow();
            if (win == null) { _isDialogOpen = false; return; }

            var dlg = new Views.CreateEventDialog
            {
                DataContext = new CreateEventDialogViewModel(start)
                {
                    EndDate = end.Date,
                    EndTime = end.TimeOfDay
                }
            };
            dlg.Closed += (_, __) => _isDialogOpen = false;

            var ev = await dlg.ShowDialog<Event>(win);
            if (ev != null)
            {
                if (ev.IsDeleted) _eventService.DeleteEvent(ev.Id);
                else if (ev.Id != 0) { ev.IsAutoGenerated = false; _eventService.UpdateEvent(ev); }
                else _eventService.CreateEvent(ev);

                SaveDaySettingsFromEvents();
                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(CurrentDate));
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                RebuildAll();
            }
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
                StartDate = existing.StartTime.Date,
                StartTime = existing.StartTime.TimeOfDay,
                EndDate = existing.EndTime.Date,
                EndTime = existing.EndTime.TimeOfDay,
                EventType = existing.EventType,
                ArrivalTime = existing.ArrivalTime,
                DepartureTime = existing.DepartureTime,
                LunchStart = existing.LunchStart,
                LunchEnd = existing.LunchEnd
            };
            vm.SelectedEventTypePair = vm.LocalizedEventTypes.First(kvp => kvp.Key == existing.EventType);

            var dlg = new Views.CreateEventDialog { DataContext = vm };
            dlg.Closed += (_, __) => _isDialogOpen = false;
            var ev = await dlg.ShowDialog<Event>(win);

            if (ev != null)
            {
                if (ev.IsDeleted) _eventService.DeleteEvent(ev.Id);
                else { ev.IsAutoGenerated = false; _eventService.UpdateEvent(ev); }

                if (ev.EventType != EventType.Work && ev.EventType != EventType.Lunch)
                    SettingsService.DeleteUserSettingsForDate(ev.StartTime.Date);

                SaveDaySettingsFromEvents();
                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(CurrentDate));
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                RebuildAll();
            }
        }

        /// <summary>Uloží uživatelské nastavení dne</summary>
        private void SaveDaySettingsFromEvents()
        {
            var evs = _eventService.GetEventsForDay(CurrentDate)
                                  .Where(e => !e.IsDeleted)
                                  .ToList();
            if (!evs.Any())
            {
                var sem = GlobalSettingsService.GetSemesterForDate(CurrentDate);
                var global = GlobalSettingsService.LoadGlobalSettings(sem)
                             ?? GlobalSettingsService.GetDefaultSettings(sem);
                var (arr, dep, lunchStart, lunchEnd) = PdfService.GetWeekdayDefaults(global, CurrentDate.DayOfWeek);
                SettingsService.SaveUserSettingsForDate(CurrentDate, arr, dep, lunchStart, lunchEnd);
                return;
            }

            var work = evs.Where(e => e.EventType != EventType.Lunch).ToList();
            if (!work.Any()) return;

            var arrival = work.Min(e => e.StartTime).TimeOfDay;
            var departure = work.Max(e => e.EndTime).TimeOfDay;

            var lunches = evs.Where(e => e.EventType == EventType.Lunch)
                             .OrderBy(e => e.StartTime)
                             .ToList();
            TimeSpan ls, le;
            if (lunches.Any())
            {
                ls = lunches.First().StartTime.TimeOfDay;
                le = lunches.Last().EndTime.TimeOfDay;
            }
            else
            {
                var sem = GlobalSettingsService.GetSemesterForDate(CurrentDate);
                var global = GlobalSettingsService.LoadGlobalSettings(sem)
                             ?? GlobalSettingsService.GetDefaultSettings(sem);
                var user = SettingsService.GetUserSettingsForDate(CurrentDate);
                (var defA, var defD, ls, le) = user != null
                  ? (user.ArrivalTime, user.DepartureTime, user.LunchStart, user.LunchEnd)
                  : PdfService.GetWeekdayDefaults(global, CurrentDate.DayOfWeek);
            }

            const double paidHours = 8.0;
            double lunchDur = (le - ls).TotalHours;
            double required = paidHours + lunchDur;
            double actualSpan = (departure - arrival).TotalHours;
            if (actualSpan < required)
                departure = arrival + TimeSpan.FromHours(required);

            SettingsService.SaveUserSettingsForDate(CurrentDate, arrival, departure, ls, le);
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
