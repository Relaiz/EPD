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

namespace TeacherScheduleApp.ViewModels
{
    public class CreateEventDialogViewModel : ViewModelBase
    {
        public bool ShowAllDay => !IsExisting || AllDay;
        private int _id;
        public int Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
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
            set => this.RaiseAndSetIfChanged(ref _allDay, value);
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
            set => this.RaiseAndSetIfChanged(ref _eventType, value);
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
            var global = GlobalSettingsService.LoadGlobalSettings(sem);
            var user = SettingsService.GetUserSettingsForDate(_startDate);

            var (arr, dep, lunchFrom, lunchTo) = GetDaySpans(global, user, _startDate.DayOfWeek);
            ArrivalTime = _startDate + arr;
            DepartureTime = _startDate + dep;
            LunchStart = _startDate + lunchFrom;
            LunchEnd = _startDate + lunchTo;

            this.WhenAnyValue(vm => vm.EventType)
                .Subscribe(t =>
                {
                    if (t != EventType.Work && t != EventType.Lunch)
                        AllDay = true;
                });

            CreateCommand = ReactiveCommand.CreateFromTask<Event>(async () =>
            {
                if (string.IsNullOrWhiteSpace(Title))
                {
                    await ShowValidationMessage.Handle("Titul je povinný!");
                    return null;
                }

                sem = GlobalSettingsService.GetSemesterForDate(StartDate.Date);
                global = GlobalSettingsService.LoadGlobalSettings(sem);
                user = SettingsService.GetUserSettingsForDate(StartDate.Date);
                (arr, dep, lunchFrom, lunchTo) = GetDaySpans(global, user, StartDate.DayOfWeek);

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

            DeleteCommand = ReactiveCommand.CreateFromTask<Event>(async () =>
            {
                if (!IsExisting)
                    return null;

                bool confirmed = await RequestDeleteConfirmation.Handle("Jsou si jisti, že chcete smazat tuto událost?");
                if (!confirmed)
                    return null;

                sem = GlobalSettingsService.GetSemesterForDate(StartDate.Date);
                global = GlobalSettingsService.LoadGlobalSettings(sem);
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
    }
}
