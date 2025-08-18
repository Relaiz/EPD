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

            var events = _eventService.GetEventsForDay(CurrentDate);
            var specials = events
               .Where(e => e.EventType != EventType.Work && e.EventType != EventType.Lunch)
               .Select(e => (e.StartTime, e.EndTime))
               .ToList();
            foreach (var e in events)
            {
                bool isSpecial = e.EventType != EventType.Work && e.EventType != EventType.Lunch;
                e.IsInactive = !isSpecial && specials.Any(sp => Intersects(e.StartTime, e.EndTime, sp.StartTime, sp.EndTime));
            }
            var manual = events
                .Where(e => !e.IsAutoGenerated)
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

        /// <summary>Uloží uživatelské nastavení dne</summary>
        private void SaveDaySettingsFromEvents()
        {
            var evs = _eventService.GetEventsForDay(CurrentDate)
                                   .Where(e => !e.IsDeleted)
                                   .OrderBy(e => e.StartTime)
                                   .ToList();

            var sem = GlobalSettingsService.GetSemesterForDate(CurrentDate);
            var global = GlobalSettingsService.LoadGlobalSettings(CurrentDate.Year, sem)
                         ?? GlobalSettingsService.GetDefaultSettings(CurrentDate.Year, sem);
            var (defArr, defDep, defLs, defLe) = PdfService.GetWeekdayDefaults(global, CurrentDate.DayOfWeek);

            if (!evs.Any())
            {
                SettingsService.SaveUserSettingsForDate(CurrentDate, defArr, defDep, defLs, defLe);
                return;
            }

            var gross = defDep - defArr;
            var netNorm = gross - (defLe - defLs);
            if (netNorm < TimeSpan.Zero) netNorm = TimeSpan.Zero;
            var eight = TimeSpan.FromHours(8);

            bool IsSpecial(Event e) => e.EventType != EventType.Work && e.EventType != EventType.Lunch;

            var specials = evs.Where(IsSpecial).ToList();

            bool fullSpecialDay =
                specials.Any(e => e.AllDay) ||
                specials.Any(e => e.StartTime.TimeOfDay <= defArr && e.EndTime.TimeOfDay >= defDep) ||
                specials.Any(e =>
                {
                    var len = e.EndTime - e.StartTime;
                    return e.EventType == EventType.Vacation ? len >= eight : len >= netNorm;
                });

            if (fullSpecialDay)
            {
                var start = specials.Min(e => e.StartTime).TimeOfDay;
                var end = specials.Max(e => e.EndTime).TimeOfDay;
                SettingsService.SaveUserSettingsForDate(CurrentDate, start, end, TimeSpan.Zero, TimeSpan.Zero);
                return;
            }

            var existingUser = SettingsService.GetUserSettingsForDate(CurrentDate);
            if (existingUser != null) return;

            var manualWL = evs.Where(e => !e.IsAutoGenerated && (e.EventType == EventType.Work || e.EventType == EventType.Lunch)).ToList();

            var arrival = defArr;
            var departure = defDep;
            if (manualWL.Any())
            {
                var minStart = manualWL.Min(e => e.StartTime).TimeOfDay;
                var maxEnd = manualWL.Max(e => e.EndTime).TimeOfDay;
                if (minStart < arrival) arrival = minStart;
                if (maxEnd > departure) departure = maxEnd;
            }

            TimeSpan lunchStart, lunchEnd;
            var lunchManual = manualWL.FirstOrDefault(e => e.EventType == EventType.Lunch);
            if (lunchManual != null)
            {
                lunchStart = lunchManual.StartTime.TimeOfDay;
                lunchEnd = lunchManual.EndTime.TimeOfDay;
            }
            else
            {
                var lunchNonAuto = evs.FirstOrDefault(e => !e.IsAutoGenerated && e.EventType == EventType.Lunch);
                if (lunchNonAuto != null)
                {
                    lunchStart = lunchNonAuto.StartTime.TimeOfDay;
                    lunchEnd = lunchNonAuto.EndTime.TimeOfDay;
                }
                else
                {
                    lunchStart = defLs;
                    lunchEnd = defLe;
                }
            }

            SettingsService.SaveUserSettingsForDate(CurrentDate, arrival, departure, lunchStart, lunchEnd);
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
