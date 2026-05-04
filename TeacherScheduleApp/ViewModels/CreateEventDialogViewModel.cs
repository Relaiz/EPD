using MsBox.Avalonia;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;

namespace TeacherScheduleApp.ViewModels
{
    public class CreateEventDialogViewModel : ViewModelBase
    {
        private readonly int _employeeId;
        private readonly EventService _eventService;

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

        private string _title = string.Empty;
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
            set
            {
                this.RaiseAndSetIfChanged(ref _eventType, value);
                this.RaisePropertyChanged(nameof(ShowAllDay));
            }
        }

        private string _dialogTitle = string.Empty;
        public string DialogTitle
        {
            get => _dialogTitle;
            private set => this.RaiseAndSetIfChanged(ref _dialogTitle, value);
        }

        private string _primaryButtonText = string.Empty;
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

                if (value.Key == EventType.Lunch && string.IsNullOrWhiteSpace(Title))
                    Title = "Oběd";
            }
        }

        public double StartHour => StartTime.Hours + StartTime.Minutes / 60.0;
        public double EndHour => EndTime.Hours + EndTime.Minutes / 60.0;

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        public bool IsExisting => Id != 0;

        public Interaction<string, bool> RequestDeleteConfirmation { get; } = new();
        public Interaction<Event?, Unit> RequestClose { get; } = new();
        public Interaction<string, Unit> ShowValidationMessage { get; } = new();

        public ReactiveCommand<Unit, Event?> CreateCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Event?> DeleteCommand { get; }

        private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
        private static readonly TimeSpan EightHours = TimeSpan.FromHours(8);
        private static readonly TimeSpan SnapEps = TimeSpan.FromMinutes(1);

        public CreateEventDialogViewModel(DateTime slotStart, int employeeId = EventService.DefaultEmployeeId)
        {
            _employeeId = employeeId;
            _eventService = new EventService();

            _id = 0;
            _startDate = slotStart.Date;
            _startTime = slotStart.TimeOfDay;
            _endDate = slotStart.Date;
            _endTime = slotStart.TimeOfDay.Add(TimeSpan.FromHours(1));

            SelectedEventTypePair = LocalizedEventTypes.First(kvp => kvp.Key == EventType);

            var (arr, dep, lunchFrom, lunchTo) = GetDaySpans(_startDate);
            ArrivalTime = _startDate + arr;
            DepartureTime = _startDate + dep;
            LunchStart = _startDate + lunchFrom;
            LunchEnd = _startDate + lunchTo;

            CreateCommand = ReactiveCommand.CreateFromTask<Event?>(CreateAsync);
            DeleteCommand = ReactiveCommand.CreateFromTask<Event?>(DeleteAsync);
            CancelCommand = ReactiveCommand.CreateFromTask(async () => await RequestClose.Handle(null));

            this.WhenAnyValue(vm => vm.Id)
                .Select(id => id != 0)
                .Subscribe(UpdateTitles);
        }

        private async Task<Event?> CreateAsync()
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

            var (arr, dep, lunchFrom, lunchTo) = GetDaySpans(StartDate.Date);

            if (SelectedEventTypePair.Key == EventType.Lunch)
            {
                if (StartDate.Date != EndDate.Date)
                {
                    await ShowValidationMessage.Handle("Oběd musí být v rámci jednoho dne.");
                    return null;
                }

                var day = StartDate.Date;
                var resolvedForLunch = SettingsService.GetResolvedDaySettings(day, _employeeId);

                var duration = EndTime - StartTime;
                if (duration <= TimeSpan.Zero)
                {
                    await ShowValidationMessage.Handle("Délka oběda musí být kladná.");
                    return null;
                }

                var maxLunch = TimeSpan.TryParse(resolvedForLunch.MaxBreakDuration, out var maxBreak)
                    ? maxBreak
                    : TimeSpan.FromMinutes(30);

                if (duration > maxLunch)
                {
                    await ShowValidationMessage.Handle(
                        $"Oběd nesmí být delší než {maxLunch:hh\\:mm}.");
                    return null;
                }

            }

            if (AllDay && EventType == EventType.Lunch)
            {
                await ShowValidationMessage.Handle("Oběd nemůže být celodenní.");
                return null;
            }

            var ev = new Event
            {
                Id = Id,
                EmployeeId = _employeeId,
                Title = Title,
                Description = Description,
                AllDay = AllDay,
                EventType = EventType,
                IsDeleted = false
            };

            if (AllDay)
            {
                var dayNorm = (dep - arr) - (lunchTo - lunchFrom);
                if (dayNorm < TimeSpan.Zero)
                    dayNorm = TimeSpan.Zero;

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
                StartDate.Date,
                EndDate.Date,
                AllDay,
                EventType,
                StartTime,
                EndTime);

            if (!ok)
                return null;

            await RequestClose.Handle(ev);
            return ev;
        }

        private async Task<Event?> DeleteAsync()
        {
            if (!IsExisting)
                return null;

            bool confirmed = await RequestDeleteConfirmation.Handle("Jsou si jisti, že chcete smazat tuto událost?");
            if (!confirmed)
                return null;

            var ev = new Event
            {
                Id = Id,
                EmployeeId = _employeeId,
                Title = Title,
                Description = Description,
                AllDay = AllDay,
                EventType = EventType,
                IsDeleted = true
            };

            if (AllDay)
            {
                var (arr, dep, _, _) = GetDaySpans(StartDate.Date);
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
        }

        private void UpdateTitles(bool isExisting)
        {
            DialogTitle = isExisting ? "Upravit událost" : "Nová událost";
            PrimaryButtonText = isExisting ? "Upravit" : "Vytvořit";
        }

        private (TimeSpan arr, TimeSpan dep, TimeSpan lunchStart, TimeSpan lunchEnd) GetDaySpans(DateTime date)
        {
            var resolved = SettingsService.GetResolvedDaySettings(date, _employeeId);
            return (
                resolved.ArrivalTime,
                resolved.DepartureTime,
                resolved.LunchStart,
                resolved.LunchEnd
            );
        }

        private static bool IsSpecial(EventType t) => t.IsSpecialAbsence();

        private static bool IsWorkingDay(DateTime d)
            => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
               && !HolidayHelper.IsCzechHoliday(d);

        private bool IsVacation(EventType t)
            => t == EventType.Vacation;

        private TimeSpan GetDailyNorm(DateTime date)
        {
            var (arr, dep, ls, le) = GetDaySpans(date);
            var norm = (dep - arr) - (le - ls);
            return norm < TimeSpan.Zero ? TimeSpan.Zero : norm;
        }

        private static bool Intersects(TimeSpan aS, TimeSpan aE, TimeSpan bS, TimeSpan bE)
            => aS < bE && bS < aE;

        private async Task<bool> ValidateSpecialAcrossRangeAsync(
            DateTime startDate,
            DateTime endDate,
            bool allDay,
            EventType type,
            TimeSpan startTod,
            TimeSpan endTod)
        {
            if (!IsSpecial(type))
                return true;

            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                if (!IsWorkingDay(day))
                    continue;

                var (arrTod, depTod, lsTod, leTod) = GetDaySpans(day);

                var grossSpan = depTod - arrTod;
                var netNorm = grossSpan - (leTod - lsTod);
                if (netNorm < TimeSpan.Zero)
                    netNorm = TimeSpan.Zero;

                var limit = type switch
                {
                    EventType.Vacation => EightHours,
                    _ => netNorm
                };

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

                    var span = endTod - startTod;

                    if (type == EventType.Vacation && span != FourHours && span != EightHours)
                    {
                        await ShowValidationMessage.Handle("Dovolená může mít pouze délku 4 hodiny nebo 8 hodin za den.");
                        return false;
                    }

                    pStartTod = startTod;
                    pEndTod = endTod;
                    proposedLen = span;
                }

                var existing = _eventService.GetEventsForDay(_employeeId, day)
                    .Where(e => IsSpecial(e.EventType) && !e.IsDeleted && !e.IsAutoGenerated)
                    .Where(e => e.Id != Id && e.ParentEventId != Id)
                    .Select(e => new
                    {
                        S = e.StartTime.TimeOfDay,
                        E = e.EndTime.TimeOfDay,
                        T = e.EventType,
                        e.Title
                    })
                    .ToList();

                if (existing.Any(x => x.T == EventType.Vacation))
                    limit = EightHours;

                var conflicts = existing
                    .Where(iv => Intersects(iv.S, iv.E, pStartTod, pEndTod))
                    .ToList();

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
                    if (left < TimeSpan.Zero)
                        left = TimeSpan.Zero;

                    await ShowValidationMessage.Handle(
                        $"Pro {day:dd.MM.yyyy} zbývá {left:hh\\:mm} pro zvláštní události (limit {limit:hh\\:mm}).");
                    return false;
                }
            }

            return true;
        }

        public void LoadFromEvent(Event ev)
        {
            Id = ev.Id;
            Title = ev.Title;
            Description = ev.Description ?? string.Empty;
            AllDay = ev.AllDay;
            EventType = ev.EventType;
            SelectedEventTypePair = LocalizedEventTypes.First(x => x.Key == ev.EventType);

            StartDate = ev.StartTime.Date;
            StartTime = ev.StartTime.TimeOfDay;
            EndDate = ev.EndTime.Date;
            EndTime = ev.EndTime.TimeOfDay;

            var (arr, dep, ls, le) = GetDaySpans(StartDate);
            ArrivalTime = StartDate.Date + arr;
            DepartureTime = StartDate.Date + dep;
            LunchStart = StartDate.Date + ls;
            LunchEnd = StartDate.Date + le;
        }
    }
}
