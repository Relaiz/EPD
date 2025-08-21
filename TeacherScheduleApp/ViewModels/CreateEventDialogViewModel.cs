using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using ReactiveUI;
using System;
using System.Reactive;
using TeacherScheduleApp.Models;
using System.Reactive.Linq;
using System.Collections.Generic;
using System.Linq;
using TeacherScheduleApp.Services;
using System.Threading.Tasks;

namespace TeacherScheduleApp.ViewModels
{
    public class CreateEventDialogViewModel : ViewModelBase
    {
        public bool ShowAllDay => true;
        private int _id;
        public int Id
        {
            get => _id;
            set
            {
                this.RaiseAndSetIfChanged(ref _id, value);
                this.RaisePropertyChanged(nameof(IsExisting));
                this.RaisePropertyChanged(nameof(ShowAllDay));
            }
        }

        private string _title;
        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        private DateTime _startDate;
        public DateTime StartDate
        {
            get => _startDate;
            set => this.RaiseAndSetIfChanged(ref _startDate, value);
        }

        private TimeSpan _startTime;
        public TimeSpan StartTime
        {
            get => _startTime;
            set => this.RaiseAndSetIfChanged(ref _startTime, value);
        }

        private DateTime _endDate;
        public DateTime EndDate
        {
            get => _endDate;
            set => this.RaiseAndSetIfChanged(ref _endDate, value);
        }

        private TimeSpan _endTime;
        public TimeSpan EndTime
        {
            get => _endTime;
            set => this.RaiseAndSetIfChanged(ref _endTime, value);
        }

        private bool _allDay;
        public bool AllDay
        {
            get => _allDay;
            set
            {
                this.RaiseAndSetIfChanged(ref _allDay, value);
                this.RaisePropertyChanged(nameof(ShowAllDay));
            }
        }
        private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
        private static readonly TimeSpan EightHours = TimeSpan.FromHours(8);
        private static readonly TimeSpan SnapEps = TimeSpan.FromMinutes(1);
        private bool IsVacation(EventType t) => t.ToString().Equals("Vacation", StringComparison.OrdinalIgnoreCase)
                                             || t.ToString().Equals("Dovolená", StringComparison.OrdinalIgnoreCase);
        private DateTime _arrivalTime;
        public DateTime ArrivalTime
        {
            get => _arrivalTime;
            set => this.RaiseAndSetIfChanged(ref _arrivalTime, value);
        }

        private DateTime _departureTime;
        public DateTime DepartureTime
        {
            get => _departureTime;
            set => this.RaiseAndSetIfChanged(ref _departureTime, value);
        }

        private DateTime _lunchStart;
        public DateTime LunchStart
        {
            get => _lunchStart;
            set => this.RaiseAndSetIfChanged(ref _lunchStart, value);
        }

        private DateTime _lunchEnd;
        public DateTime LunchEnd
        {
            get => _lunchEnd;
            set => this.RaiseAndSetIfChanged(ref _lunchEnd, value);
        }

        private EventType _eventType = EventType.Work;
        public EventType EventType
        {
            get => _eventType;
            set => this.RaiseAndSetIfChanged(ref _eventType, value);
        }
        private string _dialogTitle;
        public string DialogTitle
        {
            get => _dialogTitle;
            private set => this.RaiseAndSetIfChanged(ref _dialogTitle, value);
        }

        private string _primaryButtonText;
        public string PrimaryButtonText
        {
            get => _primaryButtonText;
            private set => this.RaiseAndSetIfChanged(ref _primaryButtonText, value);
        }
        public IEnumerable<EventType> EventTypes => Enum.GetValues(typeof(EventType)).Cast<EventType>();
        public IEnumerable<KeyValuePair<EventType, string>> LocalizedEventTypes =>
            Enum.GetValues(typeof(EventType))
            .Cast<EventType>()
            .Select(e => KeyValuePair.Create(e, e.ToDisplayName()))
            .ToList();
        private KeyValuePair<EventType, string> _selectedEventTypePair;
        public KeyValuePair<EventType, string> SelectedEventTypePair
        {
            get => _selectedEventTypePair;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedEventTypePair, value);
                EventType = value.Key;
            }
        }
        public double StartHour => StartTime.Hours + StartTime.Minutes / 60.0;
        public double EndHour => EndTime.Hours + EndTime.Minutes / 60.0;

        private string _description;
        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }
        public bool IsExisting => Id != 0;
        public Interaction<string, bool> RequestDeleteConfirmation { get; } = new Interaction<string, bool>();
        public Interaction<Event, Unit> RequestClose { get; } = new Interaction<Event, Unit>();

        public Interaction<string, Unit> ShowValidationMessage { get; } = new Interaction<string, Unit>();


        public ReactiveCommand<Unit, Event> CreateCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public ReactiveCommand<Unit, Event> DeleteCommand { get; }

        public CreateEventDialogViewModel(DateTime slotStart)
        {
            _id = 0;
            _startDate = slotStart.Date;
            _startTime = slotStart.TimeOfDay;
            _endDate = slotStart.Date;
            _endTime = slotStart.TimeOfDay.Add(TimeSpan.FromHours(1));
            SelectedEventTypePair = LocalizedEventTypes.First(kvp => kvp.Key == this.EventType);

            var sem = GlobalSettingsService.GetSemesterForDate(_startDate);
            var global = GlobalSettingsService.LoadGlobalSettings(_startDate.Year, sem) ?? GlobalSettingsService.GetDefaultSettings(_startDate.Year, sem);
            var user = SettingsService.GetUserSettingsForDate(_startDate);

            var (arr, dep, lunchFrom, lunchTo) = GetDaySpans(global, user, _startDate.DayOfWeek);
            ArrivalTime = _startDate + arr;
            DepartureTime = _startDate + dep;
            LunchStart = _startDate + lunchFrom;
            LunchEnd = _startDate + lunchTo;

            CreateCommand = ReactiveCommand.CreateFromTask<Event>(async () =>
            {
                if (string.IsNullOrWhiteSpace(Title))
                {
                    await ShowValidationMessage.Handle("Název je povinný!");
                    return null;
                }

                if (!AllDay)
                {
                    var startDt = StartDate.Date + StartTime;
                    var endDt = EndDate.Date + EndTime;

                    if (endDt <= startDt)
                    {
                        await ShowValidationMessage.Handle("Konec nesmí být před (nebo roven) začátku.");
                        return null;
                    }
                }
                else
                {
                    if (EndDate.Date < StartDate.Date)
                    {
                        await ShowValidationMessage.Handle("Konec data nesmí být před začátkem.");
                        return null;
                    }
                }

                sem = GlobalSettingsService.GetSemesterForDate(StartDate.Date);
                global = GlobalSettingsService.LoadGlobalSettings(StartDate.Date.Year, sem)
                         ?? GlobalSettingsService.GetDefaultSettings(StartDate.Date.Year, sem);
                user = SettingsService.GetUserSettingsForDate(StartDate.Date);
                (arr, dep, lunchFrom, lunchTo) = GetDaySpans(global, user, StartDate.DayOfWeek);
                if (SelectedEventTypePair.Key == EventType.Lunch)
                {
                    var day = StartDate.Date;
                    var svc = new EventService();
                    var collisions = svc.GetEventsForDay(day)
                        .Where(e => !e.IsDeleted && !e.IsAutoGenerated)
                        .Where(e => e.EventType != EventType.Lunch)
                        .Any(e =>
                            Intersects(
                                (day + StartTime).TimeOfDay,
                                (day + EndTime).TimeOfDay,
                                e.StartTime.TimeOfDay,
                                e.EndTime.TimeOfDay));

                    if (collisions)
                    {
                        await ShowValidationMessage.Handle("Oběd se nesmí překrývat s výukou nebo zvláštní událostí.");
                        return null;
                    }
                }
                var ev = new Event
                {
                    Id = this.Id,
                    Title = this.Title,
                    Description = this.Description,
                    AllDay = this.AllDay,
                    EventType = this.EventType,
                    ArrivalTime = StartDate.Date + arr,
                    DepartureTime = StartDate.Date + dep,
                    LunchStart = StartDate.Date + lunchFrom,
                    LunchEnd = StartDate.Date + lunchTo
                };

                if (AllDay)
                {
                    var dayNorm = (dep - arr) - (lunchTo - lunchFrom);

                    if (IsVacation(EventType))
                    {
                        ev.StartTime = StartDate.Date + arr;
                        ev.EndTime = EndDate.Date + (arr + EightHours);
                    }
                    else
                    {
                        ev.StartTime = StartDate.Date + arr;
                        ev.EndTime = EndDate.Date + (arr + dayNorm);
                    }
                }
                else
                {
                    ev.StartTime = StartDate.Date + StartTime;
                    ev.EndTime = EndDate.Date + EndTime;

                    if (IsVacation(EventType))
                    {
                        var perDay = EndTime - StartTime;
                        if (perDay != FourHours && perDay != EightHours)
                        {
                            await ShowValidationMessage.Handle("Dovolená může mít pouze délku 4 hodiny nebo 8 hodin za den.");
                            return null;
                        }
                    }
                }

                var ok = await ValidateSpecialAcrossRangeAsync(
                    StartDate.Date, EndDate.Date, AllDay, EventType, StartTime, EndTime);
                if (!ok) return null;

                await RequestClose.Handle(ev);
                return ev;
            });

            DeleteCommand = ReactiveCommand.CreateFromTask<Event>(async () =>
            {
                if (!IsExisting)
                    return null;

                bool confirmed = await RequestDeleteConfirmation.Handle("Jsou si jisti, že chcete smazat tuto událost?");
                if (!confirmed)
                    return null;

                sem = GlobalSettingsService.GetSemesterForDate(StartDate.Date);
                global = GlobalSettingsService.LoadGlobalSettings(StartDate.Date.Year, sem) ?? GlobalSettingsService.GetDefaultSettings(StartDate.Date.Year, sem);
                user = SettingsService.GetUserSettingsForDate(StartDate.Date);
                (arr, dep, lunchFrom, lunchTo) = GetDaySpans(global, user, StartDate.DayOfWeek);

                var ev = new Event
                {
                    Id = this.Id,
                    Title = this.Title,
                    Description = this.Description,
                    AllDay = this.AllDay,
                    IsDeleted = true,
                    ArrivalTime = StartDate.Date + arr,
                    DepartureTime = StartDate.Date + dep,
                    LunchStart = StartDate.Date + lunchFrom,
                    LunchEnd = StartDate.Date + lunchTo
                };

                if (AllDay)
                {
                    ev.StartTime = StartDate.Date + arr;
                    ev.EndTime = StartDate.Date + dep;
                }
                else
                {
                    ev.StartTime = StartDate.Date + StartTime;
                    ev.EndTime = EndDate.Date + EndTime;
                }

                await RequestClose.Handle(ev);
                return ev;
            });

            CancelCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                await RequestClose.Handle(null);
            });
            this.WhenAnyValue(vm => vm.Id).Select(id => id != 0).Subscribe(isExisting => UpdateTitles(isExisting));
        }

        private void UpdateTitles(bool isExisting)
        {
            DialogTitle = isExisting ? "Upravit událost" : "Nová událost";
            PrimaryButtonText = isExisting ? "Upravit" : "Vytvořit";
        }
        private (TimeSpan arr, TimeSpan dep, TimeSpan lunchStart, TimeSpan lunchEnd)
        GetDaySpans(GlobalSettings g, UserSettings u, DayOfWeek wd)
        {
            string sa, sd, s0, s1;
            switch (wd)
            {
                case DayOfWeek.Monday:
                    (sa, sd, s0, s1) =
                      (g.MondayArrival, g.MondayDeparture,
                       g.MondayLunchStart, g.MondayLunchEnd);
                    break;
                case DayOfWeek.Tuesday:
                    (sa, sd, s0, s1) =
                      (g.TuesdayArrival, g.TuesdayDeparture,
                       g.TuesdayLunchStart, g.TuesdayLunchEnd);
                    break;
                case DayOfWeek.Wednesday:
                    (sa, sd, s0, s1) =
                      (g.WednesdayArrival, g.WednesdayDeparture,
                       g.WednesdayLunchStart, g.WednesdayLunchEnd);
                    break;
                case DayOfWeek.Thursday:
                    (sa, sd, s0, s1) =
                      (g.ThursdayArrival, g.ThursdayDeparture,
                       g.ThursdayLunchStart, g.ThursdayLunchEnd);
                    break;
                case DayOfWeek.Friday:
                    (sa, sd, s0, s1) =
                      (g.FridayArrival, g.FridayDeparture,
                       g.FridayLunchStart, g.FridayLunchEnd);
                    break;
                default:
                    sa = g.GlobalStartTime;
                    sd = g.GlobalEndTime;
                    s0 = g.MondayLunchStart;
                    s1 = g.MondayLunchEnd;
                    break;
            }

            var arr = TimeSpan.Parse(sa);
            var dep = TimeSpan.Parse(sd);
            var lunchStart = TimeSpan.Parse(s0);
            var lunchEnd = TimeSpan.Parse(s1);

            if (u != null)
            {
                arr = u.ArrivalTime;
                dep = u.DepartureTime;
                lunchStart = u.LunchStart;
                lunchEnd = u.LunchEnd;
            }

            return (arr, dep, lunchStart, lunchEnd);
        }

        private static readonly HashSet<EventType> SpecialTypes = new()
        {
            EventType.DayOff, EventType.Illness, EventType.Vacation,
            EventType.Ocr, EventType.Doctor, EventType.BusinessTrip, EventType.Holiday
        };
        private static bool IsSpecial(EventType t) => SpecialTypes.Contains(t);

        private static bool IsWorkingDay(DateTime d)
            => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
               && !TeacherScheduleApp.Helpers.HolidayHelper.IsCzechHoliday(d);

        private TimeSpan GetDailyNorm(DateTime date)
        {
            var sem = GlobalSettingsService.GetSemesterForDate(date);
            var g = GlobalSettingsService.LoadGlobalSettings(date.Year, sem)
                    ?? GlobalSettingsService.GetDefaultSettings(date.Year, sem);
            var u = SettingsService.GetUserSettingsForDate(date);

            var (arr, dep, ls, le) = GetDaySpans(g, u, date.DayOfWeek);
            var norm = (dep - arr) - (le - ls);
            return norm < TimeSpan.Zero ? TimeSpan.Zero : norm;
        }

        private static bool Intersects(TimeSpan aS, TimeSpan aE, TimeSpan bS, TimeSpan bE)
            => aS < bE && bS < aE;

        private async Task<bool> ValidateSpecialAcrossRangeAsync(DateTime startDate, DateTime endDate, bool allDay, EventType type, TimeSpan startTod, TimeSpan endTod)
        {
            if (!IsSpecial(type)) return true;

            var evSvc = new EventService();

            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                if (!IsWorkingDay(day)) continue;

                var sem = GlobalSettingsService.GetSemesterForDate(day);
                var g = GlobalSettingsService.LoadGlobalSettings(day.Year, sem)
                            ?? GlobalSettingsService.GetDefaultSettings(day.Year, sem);
                var u = SettingsService.GetUserSettingsForDate(day);
                var (arrTod, depTod, lsTod, leTod) = GetDaySpans(g, u, day.DayOfWeek);

                var grossSpan = depTod - arrTod;
                var netNorm = grossSpan - (leTod - lsTod);
                if (netNorm < TimeSpan.Zero) netNorm = TimeSpan.Zero;

                var limit = (type == EventType.Vacation) ? EightHours : netNorm;

                var effectiveAllDay = allDay;
                if (!effectiveAllDay && IsSpecial(type))
                {
                    var len = endTod - startTod;
                    if (type == EventType.Vacation)
                    {
                        if (len.Duration() == EightHours)
                            effectiveAllDay = true;
                    }
                    else
                    {
                        if ((len - netNorm).Duration() <= SnapEps || len > netNorm)
                            effectiveAllDay = true;
                    }
                }

                TimeSpan pStartTod, pEndTod, proposedLen;

                if (effectiveAllDay)
                {
                    if (type == EventType.Vacation)
                    {
                        if (grossSpan < EightHours)
                        {
                            await ShowValidationMessage.Handle(
                                $"Pro {day:dd.MM.yyyy} je pracovní rozsah {grossSpan:hh\\:mm}, celodenní dovolená vyžaduje 8:00.");
                            return false;
                        }
                        pStartTod = arrTod;
                        pEndTod = arrTod + EightHours;
                        proposedLen = EightHours;
                    }
                    else
                    {
                        pStartTod = arrTod;
                        pEndTod = arrTod + netNorm;
                        proposedLen = netNorm;
                    }
                }
                else
                {
                    if (endTod <= startTod)
                    {
                        await ShowValidationMessage.Handle($"Neplatný čas {day:dd.MM.yyyy}.");
                        return false;
                    }
                    if (type == EventType.Vacation &&
                        (endTod - startTod != FourHours && endTod - startTod != EightHours))
                    {
                        await ShowValidationMessage.Handle(
                            "Dovolená může mít pouze délku 4 hodiny nebo 8 hodin za den.");
                        return false;
                    }
                    pStartTod = startTod;
                    pEndTod = endTod;
                    proposedLen = pEndTod - pStartTod;
                }

                var existing = evSvc.GetEventsForDay(day)
                .Where(e => IsSpecial(e.EventType) && !e.IsDeleted && !e.IsAutoGenerated)
                .Where(e => e.Id != this.Id && e.ParentEventId != this.Id)
                .Select(e => (S: e.StartTime.TimeOfDay,E: e.EndTime.TimeOfDay,T: e.EventType,Title: e.Title))
                .ToList();

                if (existing.Any(x => x.T == EventType.Vacation))
                    limit = EightHours;
                var conflicts = existing.Where(iv => Intersects(iv.S, iv.E, pStartTod, pEndTod)).ToList();
                if (conflicts.Count > 0)
                {
                    var list = string.Join(", ",
                        conflicts.Select(x => $"„{x.Title}“ {x.S:hh\\:mm}–{x.E:hh\\:mm}"));
                    await ShowValidationMessage.Handle(
                        $"Zvláštní událost se překrývá s: {list} na {day:dd.MM.yyyy}. " +
                        $"Vkládaný interval: {pStartTod:hh\\:mm}–{pEndTod:hh\\:mm}.");
                    return false;
                }

                var used = TimeSpan.FromTicks(existing.Sum(iv => (iv.E - iv.S).Ticks));
                if (used + proposedLen > limit)
                {
                    var left = limit - used;
                    if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                    await ShowValidationMessage.Handle(
                        $"Pro {day:dd.MM.yyyy} zbývá {left:hh\\:mm} pro zvláštní události (limit {limit:hh\\:mm}).");
                    return false;
                }
            }
            return true;
        }
    }
}