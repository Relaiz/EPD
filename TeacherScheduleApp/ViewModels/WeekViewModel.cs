using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Controls;
using TeacherScheduleApp.Services;
using TeacherScheduleApp.Models;
using System.Collections.Generic;
using System.Reactive.Linq;
using TeacherScheduleApp.Messages;
using System.Linq;
using TeacherScheduleApp.Controls;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace TeacherScheduleApp.ViewModels
{
    public class WeekViewModel : ViewModelBase
    {
        private CalendarPanel? _calendarPanel;
        private readonly EventService _eventService = new EventService();

        public DateTime StartOfWeek { get => _startOfWeek; set => this.RaiseAndSetIfChanged(ref _startOfWeek, value); }
        private DateTime _startOfWeek;

        public DateTime EndOfWeek { get => _endOfWeek; set => this.RaiseAndSetIfChanged(ref _endOfWeek, value); }
        private DateTime _endOfWeek;

        public List<int> GridCells { get; set; } = new();
        public ObservableCollection<WeekDayInfo> WeekDays { get; } = new();
        public ObservableCollection<string> Hours { get; } = new();

        private bool _isDialogOpen = false;

        public ReactiveCommand<Unit, Unit> PreviousWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> NextWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> TodayCommand { get; }
        public string WeekTitle => BuildWeekTitle(StartOfWeeks(CurrentDate), CultureInfo.CurrentUICulture);
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

        public WeekViewModel(DateTime date, Action<DateTime> onDateChanged)
        {
            CurrentDate = date.Date;
            _onDateChanged = onDateChanged;

            for (int i = 0; i < 24; i++) Hours.Add($"{i:00}:00");

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

            LoadEvents();
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
                return $"{weekStart.ToString("dd", culture)}–{weekEnd.ToString("dd", culture)} {weekEnd.ToString("MMM yyyy", culture)}";

            if (weekStart.Year == weekEnd.Year)
                return $"{weekStart.ToString("dd MMM", culture)}–{weekEnd.ToString("dd MMM yyyy", culture)}";

            return $"{weekStart.ToString("dd MMM yyyy", culture)}–{weekEnd.ToString("dd MMM yyyy", culture)}";
        }
        public void AttachCalendarPanel(Controls.CalendarPanel panel)
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
                var existing = _eventService.FindEventByStartTime(eventStart);

                var dlg = new Views.CreateEventDialog();
                CreateEventDialogViewModel vm;

                if (existing != null)
                {
                    vm = new CreateEventDialogViewModel(existing.StartTime)
                    {
                        Id = existing.Id,
                        Title = existing.Title,
                        Description = existing.Description!,
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
                }
                else
                {
                    vm = new CreateEventDialogViewModel(eventStart)
                    {
                        EndDate = eventEnd.Date,
                        EndTime = eventEnd.TimeOfDay
                    };
                }

                dlg.DataContext = vm;
                var ev = await dlg.ShowDialog<Event>(main);
                if (ev == null) return;

                if (ev.IsDeleted)
                {
                    if (ev.ParentEventId == null)
                        _eventService.DeleteEventCascadeAndCleanup(ev.Id);
                    else
                        _eventService.DeleteEvent(ev.Id);
                }
                else if (ev.Id != 0)
                {
                    var oldStart = existing?.StartTime.Date ?? ev.StartTime.Date;
                    var oldEnd = existing?.EndTime.Date ?? ev.EndTime.Date;

                    _eventService.UpdateEvent(ev);
                    var from = oldStart < ev.StartTime.Date ? oldStart : ev.StartTime.Date;
                    var to = oldEnd > ev.EndTime.Date ? oldEnd : ev.EndTime.Date;

                    var generator = new AutomaticEventsGeneratorService(
                        _eventService, _ => System.Threading.Tasks.Task.FromResult(true));
                    await generator.RegenerateRangeEventsAsync(from, to);
                }
                else
                {
                    _eventService.CreateEvent(ev);
                    var generator = new AutomaticEventsGeneratorService(
                        _eventService, _ => System.Threading.Tasks.Task.FromResult(true));
                    await generator.RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
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

        public void LoadEvents()
        {
            if (_calendarPanel == null) return;
            _calendarPanel.Children.Clear();
            for (int day = 0; day < 7; day++)
            {
                var d = StartOfWeek.AddDays(day);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || TeacherScheduleApp.Helpers.HolidayHelper.IsCzechHoliday(d))
                {
                    _calendarPanel.Children.Add(new CalendarBackgroundBlock
                    {
                        DayIndex = day,
                        StartHour = 0,
                        EndHour = 24
                    });
                    continue;
                }
                var sem = GlobalSettingsService.GetSemesterForDate(d);
                var global = GlobalSettingsService.LoadGlobalSettings(d.Year, sem)
                             ?? GlobalSettingsService.GetDefaultSettings(d.Year, sem);
                var user = SettingsService.GetUserSettingsForDate(d);
                var (defArr, defDep, _, _) = PdfService.GetWeekdayDefaults(global, d.DayOfWeek);

                double arr = (user?.ArrivalTime ?? defArr).TotalHours;
                double dep = (user?.DepartureTime ?? defDep).TotalHours;

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
            var events = _eventService.GetEventsForWeek(StartOfWeek).ToList();

            var parentsWithChildren = events
                .Where(e => e.ParentEventId != null)
                .Select(e => e.ParentEventId!.Value)
                .ToHashSet();

            events = events
                .Where(e => !(e.ParentEventId == null && parentsWithChildren.Contains(e.Id)))
                .ToList();

            var specials = events
                .Where(e => e.EventType != EventType.Work && e.EventType != EventType.Lunch)
                .Select(e => (e.StartTime, e.EndTime))
                .ToList();

            foreach (var e in events)
            {
                bool isSpecial = e.EventType != EventType.Work && e.EventType != EventType.Lunch;
                e.IsInactive = !isSpecial && specials.Any(sp => e.StartTime < sp.EndTime && sp.StartTime < e.EndTime);
            }

            for (int day = 0; day < 7; day++)
            {
                var d = StartOfWeek.AddDays(day);
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var sem = GlobalSettingsService.GetSemesterForDate(d);
                var global = GlobalSettingsService.LoadGlobalSettings(d.Year, sem)
                             ?? GlobalSettingsService.GetDefaultSettings(d.Year, sem);
                var user = SettingsService.GetUserSettingsForDate(d);
                var segments = new List<(Event ev, double sh, double eh)>();
                foreach (var ev in events)
                {
                    int startDay = (ev.StartTime.Date - StartOfWeek.Date).Days;
                    int endDay = (ev.EndTime.Date - StartOfWeek.Date).Days;
                    if (endDay < 0 || startDay > 6) continue;
                    if (day < Math.Max(0, startDay) || day > Math.Min(6, endDay)) continue;

                    double sh, eh;
                    if (ev.AllDay)
                    {
                        sh = ev.ArrivalTime.TimeOfDay.TotalHours;
                        eh = ev.DepartureTime.TimeOfDay.TotalHours;
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

                    if (eh > sh) segments.Add((ev, sh, eh));
                }

                if (segments.Count == 0) continue;

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
                        int overlapCount = (colCount <= 1) ? 1 : colCount;
                        int overlapIndex = (colCount <= 1) ? 0 : colIndex[seg.ev];

                        seg.ev.HasCollision = colCount > 1;

                        var ctl = new CalendarEventControl(seg.ev)
                        {
                            DayIndex = day,
                            StartHour = seg.sh,
                            EndHour = seg.eh,
                            OverlapCount = overlapCount,
                            OverlapIndex = overlapIndex
                        };
                        ctl.PointerPressed += (_, a) => { a.Handled = true; OnEventClicked(seg.ev); };
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
            public string DayName { get; set; }
            public DateTime Date { get; set; }
            public bool IsToday { get; set; }
        }
    }
}
