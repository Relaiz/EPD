using ReactiveUI;
using System;
using System.Reactive;
using System.Threading.Tasks;
using TeacherScheduleApp.Services;
using TeacherScheduleApp.Views;
using Avalonia.Controls;
using System.IO;
using TeacherScheduleApp.Models;
using System.Collections.Generic;
using System.Linq;
using TeacherScheduleApp.Messages;
using System.Reactive.Linq;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using System.Reactive.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using TeacherScheduleApp.Helpers;
using static TeacherScheduleApp.Services.EventService;
using System.Collections.ObjectModel;
using Avalonia.Controls.Shapes;
using System.Runtime.Intrinsics.Arm;

namespace TeacherScheduleApp.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private enum ViewKind { Day, Week, Month }
        private ViewKind _previousView;
        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
        }
        private readonly PdfPreviewWindow _pdfWindow;

        private bool _isFullSpecialDay;
        public bool IsFullSpecialDay
        {
            get => _isFullSpecialDay;
            set
            {
                this.RaiseAndSetIfChanged(ref _isFullSpecialDay, value);
                this.RaisePropertyChanged(nameof(IsLunchEnabled));
            }
        }
        public bool IsLunchEnabled => !IsFullSpecialDay;

        public ReactiveCommand<Unit, Task> ShowPdfPreview { get; }
        private DateTime _calendarDisplayDate;
        public DateTime CalendarDisplayDate
        {
            get => _calendarDisplayDate;
            set
            {
                this.RaiseAndSetIfChanged(ref _calendarDisplayDate, value);

                int desiredDay = SelectedMonth?.Day ?? value.Day;

                int daysInMonth = DateTime.DaysInMonth(value.Year, value.Month);

                DateTime target;
                if (desiredDay <= daysInMonth)
                {
                    target = new DateTime(value.Year, value.Month, desiredDay);
                }
                else
                {
                    var firstOfMonth = new DateTime(value.Year, value.Month, 1);
                    int offset = ((int)DayOfWeek.Monday - (int)firstOfMonth.DayOfWeek + 7) % 7;
                    target = firstOfMonth.AddDays(offset);
                }

                SelectedMonth = target;
                SelectedDate = target;
            }
        }
        private DateTime? _selectedDate;

        private DateTime? _selectedWeek;

        private DateTime? _selectedMonth;
        public DateTime? SelectedDate
        {
            get => _selectedDate;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedDate, value);
                this.RaiseAndSetIfChanged(ref _selectedWeek, value);
                this.RaiseAndSetIfChanged(ref _selectedMonth, value);
                if (value.HasValue)
                {
                    LoadUserSettingsForDate(value.Value);
                    CurrentViewModel = null;
                    OpenDayView();

                    IsDayViewVisible = true;
                    IsWeekViewVisible = false;
                    IsMonthViewVisible = false;
                    this.RaisePropertyChanged(nameof(IsDayViewVisible));
                    this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                    this.RaisePropertyChanged(nameof(IsMonthViewVisible));
                    RecalculateWorkingHours();
                }
            }
        }
        public DateTime? SelectedWeek
        {
            get => _selectedWeek;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedWeek, value);
                if (value.HasValue)
                {                
                    CurrentViewModel = null;
                    OpenWeekView();

                    IsDayViewVisible = false;
                    IsWeekViewVisible = true;
                    IsMonthViewVisible = false;

                    RecalculateWorkingHours();
                }
            }
        }

        public DateTime? SelectedMonth
        {
            get => _selectedMonth;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedMonth, value);
                if (value.HasValue)
                {
                    CurrentViewModel = null;
                    OpenMonthView();

                    IsDayViewVisible = false;
                    IsWeekViewVisible = false;
                    IsMonthViewVisible = true;

          
                    RecalculateWorkingHours();
                }
            }
        }

        private double _dailyHours;
        public double DailyHours
        {
            get => _dailyHours;
            set => this.RaiseAndSetIfChanged(ref _dailyHours, value);
        }

        private double _weeklyHours;
        public double WeeklyHours
        {
            get => _weeklyHours;
            set => this.RaiseAndSetIfChanged(ref _weeklyHours, value);
        }

        private double _monthlyHours;
        public double MonthlyHours
        {
            get => _monthlyHours;
            set => this.RaiseAndSetIfChanged(ref _monthlyHours, value);
        }

        private string _arrivalTime = "08:00";
        public string ArrivalTime
        {
            get => _arrivalTime;
            set
            {
                this.RaiseAndSetIfChanged(ref _arrivalTime, value);
                this.RaisePropertyChanged(nameof(DayHours));
            }
        }

        private string _departureTime = "16:30";
        public string DepartureTime
        {
            get => _departureTime;
            set
            {
                this.RaiseAndSetIfChanged(ref _departureTime, value);
                this.RaisePropertyChanged(nameof(DayHours));
            }
        }

        private string _lunchStartTime = "14:30";
        public string LunchStartTime
        {
            get => _lunchStartTime;
            set
            {
                this.RaiseAndSetIfChanged(ref _lunchStartTime, value);
                this.RaisePropertyChanged(nameof(LunchMinutes));
                this.RaisePropertyChanged(nameof(DayHours));
            }
        }

        private string _lunchEndTime = "15:00";
        public string LunchEndTime
        {
            get => _lunchEndTime;
            set
            {
                this.RaiseAndSetIfChanged(ref _lunchEndTime, value);
                this.RaisePropertyChanged(nameof(LunchMinutes));
                this.RaisePropertyChanged(nameof(DayHours));
            }
        }
        private CalendarMode _calendarMode = CalendarMode.Month;
        public CalendarMode CalendarMode
        {
            get => _calendarMode;
            set
            {
                var wasYear = _calendarMode == CalendarMode.Year;
                this.RaiseAndSetIfChanged(ref _calendarMode, value);

               
                if (wasYear && value == CalendarMode.Month)
                {                
                    SelectedMonth = CalendarDisplayDate;
                    CurrentViewModel = null;
                    OpenMonthView();

                    IsDayViewVisible = false;
                    IsWeekViewVisible = false;
                    IsMonthViewVisible = true;
                    this.RaisePropertyChanged(nameof(IsDayViewVisible));
                    this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                    this.RaisePropertyChanged(nameof(IsMonthViewVisible));
                    RecalculateWorkingHours();
                }
            }
        }
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }
        public bool IsDayViewVisible { get; set; } = false;
        public bool IsWeekViewVisible { get; set; } = false;
        public bool IsMonthViewVisible { get; set; } = false;
        private List<Event> _events = new List<Event>();
        private readonly WorkingHoursCalculatorService _hoursCalculator;
        public ReactiveCommand<Unit, Unit> CreateEventCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDayCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> GenerateEPDCommand { get; }
        public ReactiveCommand<Unit, Unit> EnsureUserSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveUserSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenGlobalSettingsCommand { get; }
        public Interaction<string,bool> ShowCollisionMessage { get; } = new Interaction<string, bool>();
        public ReactiveCommand<Unit, Unit> DayTappedCommand { get; }
        public ReactiveCommand<Unit, Unit> RegenerateAllCommand { get; }
        private readonly EventService _eventService;
        public ObservableCollection<ImportBatchItemViewModel> ImportBatches { get; } = new();
        public MainWindowViewModel()
        {
            _eventService = new EventService();
           

            _hoursCalculator = new WorkingHoursCalculatorService();
            _selectedDate = DateTime.Now;
            _selectedWeek = DateTime.Now;
            _selectedMonth = DateTime.Now;
            CalendarDisplayDate = DateTime.Today;
            var pdfSvc = new PdfService();
            var evSvc = new EventService();
            var sem = GlobalSettingsService.GetSemesterForDate(SelectedDate.Value);

            var global = GlobalSettingsService.LoadGlobalSettings(SelectedDate.Value.Year, sem);

            if (global == null)
            {
                global = GlobalSettingsService.GetDefaultSettings(SelectedDate.Value.Year, sem);
                GlobalSettingsService.SaveGlobalSettingsAsync(SelectedDate.Value.Year, sem, global);
            }

            DayTappedCommand = ReactiveCommand.Create(() =>
            {
                if (!SelectedDate.HasValue) return;
                LoadUserSettingsForDate(SelectedDate.Value);
                CurrentViewModel = null;
                OpenDayView();

                IsDayViewVisible = true;
                IsWeekViewVisible = false;
                IsMonthViewVisible = false;
                this.RaisePropertyChanged(nameof(IsDayViewVisible));
                this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                this.RaisePropertyChanged(nameof(IsMonthViewVisible));
                RecalculateWorkingHours();
            });
            ShowPdfPreview = ReactiveCommand.Create(async() =>
            {
                var owner = Helpers.Helper.GetMainWindow();
                var win = new PdfPreviewWindow
                {
                    DataContext = new PdfPreviewViewModel(pdfSvc, evSvc, SelectedMonth!.Value),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                await win.ShowDialog(owner);
            });
            RegenerateAllCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                IsBusy = true;
                try
                {
                    var generator = new AutomaticEventsGeneratorService(new EventService(),
                    prompt => this.ShowCollisionMessage.Handle(prompt).FirstAsync().ToTask()
                    );
                    var yearStart = new DateTime(DateTime.Now.Year, 1, 1);
                    var yearEnd = new DateTime(DateTime.Now.Year, 12, 31);
                    await generator.RegenerateRangeEventsAsync(yearStart, yearEnd);
                    MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                }
                finally
                {
                    IsBusy = false;
                }
            }, outputScheduler: RxApp.TaskpoolScheduler);
            ShowCollisionMessage.RegisterHandler(async interaction =>
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var mainWindow = Helpers.Helper.GetMainWindow();
                    var msgParams = new MessageBoxStandardParams
                    {
                        ButtonDefinitions = ButtonEnum.YesNo,
                        Icon = Icon.Question,
                        ContentHeader = "Kolize s obědem",
                        ContentMessage = interaction.Input,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    var msgBox = MessageBoxManager.GetMessageBoxStandard(msgParams);
                    var result = await msgBox.ShowWindowDialogAsync(mainWindow);
                    interaction.SetOutput(result == ButtonResult.Yes);
                });
            });
            var generator = new AutomaticEventsGeneratorService(
                new EventService(),
                prompt => this.ShowCollisionMessage.Handle(prompt).FirstAsync().ToTask());
            RxApp.TaskpoolScheduler.ScheduleAsync(async (_, __) =>
            {
                var yearStart = new DateTime(DateTime.Now.Year, 1, 1);
                var yearEnd = new DateTime(DateTime.Now.Year, 12, 31);
                await generator.RegenerateRangeEventsAsync(yearStart, yearEnd);
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                return Disposable.Empty;
            });
            MessageBus.Current
             .Listen<AutoEventsGeneratedMessage>()
             .ObserveOn(RxApp.MainThreadScheduler)
             .Subscribe(_ =>
             {
                 _events = _eventService.LoadEvents();
                 RecalculateWorkingHours();
                 CurrentViewModel = CurrentViewModel;
             });
            GenerateEPDCommand = ReactiveCommand.CreateFromTask(GenerateEPDAsync);
            MessageBus.Current
              .Listen<UserSettingsChangedMessage>()
              .Where(m => SelectedDate.HasValue && m.Date == SelectedDate.Value)
              .ObserveOn(RxApp.MainThreadScheduler)
              .Subscribe(m =>
              {
                  LoadUserSettingsForDate(m.Date);
              });
            MessageBus.Current
            .Listen<EpdGeneratedMessage>()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => {
                RefreshImportBatches();
                if (IsDayViewVisible) OpenDayView();
                else if (IsWeekViewVisible) OpenWeekView();
                else if (IsMonthViewVisible) OpenMonthView();
            });

            CurrentViewModel = new DayViewModel(DateTime.Now, newDate =>
            {
                SelectedDate = newDate;
            });
            IsDayViewVisible = true;
            _events = _eventService.LoadEvents();
            RecalculateWorkingHours();
            LoadUserSettingsForDate(DateTime.Today);
            CreateEventCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (!SelectedDate.HasValue)
                    return;

                var day = SelectedDate.Value.Date;
                var sem = GlobalSettingsService.GetSemesterForDate(day);
                var global = GlobalSettingsService.LoadGlobalSettings(day.Year,sem)
                             ?? GlobalSettingsService.GetDefaultSettings(day.Year, sem);
                var user = SettingsService.GetUserSettingsForDate(day);
                (TimeSpan arr, TimeSpan dep, TimeSpan ls, TimeSpan le) =
                    user != null
                      ? (user.ArrivalTime, user.DepartureTime, user.LunchStart, user.LunchEnd)
                      : PdfService.GetWeekdayDefaults(global, day.DayOfWeek);

                var vm = new CreateEventDialogViewModel(day + arr)
                {
                    EndDate = day,
                    EndTime = dep,
                    ArrivalTime = DateTime.Today + arr,
                    DepartureTime = DateTime.Today + dep,
                    LunchStart = DateTime.Today + ls,
                    LunchEnd = DateTime.Today + le,
                };

                vm.SelectedEventTypePair = vm.LocalizedEventTypes
                    .First(kvp => kvp.Key == EventType.Work);

                var dlg = new CreateEventDialog
                {
                    DataContext = vm,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                var main = Helpers.Helper.GetMainWindow();
                if (main == null)
                    return;

                var ev = await dlg.ShowDialog<Event>(main);
                if (ev == null)
                    return;

                if (ev.IsDeleted)
                    _eventService.DeleteEvent(ev.Id);
                else if (ev.Id != 0)
                {
                    ev.IsAutoGenerated = false;
                    _eventService.UpdateEvent(ev);
                }
                else
                {
                    _eventService.CreateEvent(ev);
                }
                var generator = new AutomaticEventsGeneratorService(_eventService,
                prompt => ShowCollisionMessage.Handle(prompt).FirstAsync().ToTask());

                await generator.RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);
                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(day));
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                _events = _eventService.LoadEvents();
                RecalculateWorkingHours();
                CurrentViewModel = CurrentViewModel;
            });
            foreach (var b in _eventService.GetImportBatches().OrderByDescending(b => b.RangeStart))
                ImportBatches.Add(new ImportBatchItemViewModel(b, this));




            ShowDayCommand = ReactiveCommand.Create(() =>
            {
                OpenDayView();
                IsDayViewVisible = true;
                IsWeekViewVisible = false;
                IsMonthViewVisible = false;
                this.RaisePropertyChanged(nameof(IsDayViewVisible));
                this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                this.RaisePropertyChanged(nameof(IsMonthViewVisible));
                _events = _eventService.LoadEvents();
                RecalculateWorkingHours();
            });

            ShowWeekCommand = ReactiveCommand.Create(() =>
            {
                OpenWeekView();
                IsDayViewVisible = false;
                IsMonthViewVisible = false;
                IsWeekViewVisible = true;
                this.RaisePropertyChanged(nameof(IsDayViewVisible));
                this.RaisePropertyChanged(nameof(IsMonthViewVisible));
                this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                _events = _eventService.LoadEvents();
                RecalculateWorkingHours();

            });

            ShowMonthCommand = ReactiveCommand.Create(() =>
            {
                OpenMonthView();
                IsDayViewVisible = false;
                IsMonthViewVisible = true;
                IsWeekViewVisible = false;
                this.RaisePropertyChanged(nameof(IsDayViewVisible));
                this.RaisePropertyChanged(nameof(IsMonthViewVisible));
                this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                _events = _eventService.LoadEvents();
                RecalculateWorkingHours();
            });
            OpenGlobalSettingsCommand = ReactiveCommand.Create(() =>
            {
                _previousView = IsDayViewVisible ? ViewKind.Day
                   : IsWeekViewVisible ? ViewKind.Week
                   : ViewKind.Month;
                var year = (SelectedMonth ?? DateTime.Today).Year;
                var sem = GlobalSettingsService.GetSemesterForDate(SelectedDate ?? DateTime.Today);
                CurrentViewModel = new GlobalUserSettingsViewModel(year, sem, closeRequested: () =>
                {
                    switch (_previousView)
                    {
                        case ViewKind.Day: OpenDayView(); IsDayViewVisible = true; IsWeekViewVisible = false; IsMonthViewVisible = false; break;
                        case ViewKind.Week: OpenWeekView(); IsDayViewVisible = false; IsWeekViewVisible = true; IsMonthViewVisible = false; break;
                        default: OpenMonthView(); IsDayViewVisible = false; IsWeekViewVisible = false; IsMonthViewVisible = true; break;
                    }
                    this.RaisePropertyChanged(nameof(IsDayViewVisible));
                    this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                    this.RaisePropertyChanged(nameof(IsMonthViewVisible));
                });
                IsDayViewVisible = false;
                IsWeekViewVisible = false;
                IsMonthViewVisible = false;
                this.RaisePropertyChanged(nameof(IsDayViewVisible));
                this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                this.RaisePropertyChanged(nameof(IsMonthViewVisible));
            });
            GenerateEPDCommand = ReactiveCommand.CreateFromTask(GenerateEPDAsync);
            SaveUserSettingsCommand = ReactiveCommand.CreateFromTask(async () =>
            {

                if (!SelectedDate.HasValue) return;
                var date = SelectedDate.Value.Date;
                if (IsFullSpecialDay && TimeSpan.TryParse(ArrivalTime, out var a)
                     && TimeSpan.TryParse(DepartureTime, out var d))
                {
                    SettingsService.SaveUserSettingsForDate(SelectedDate.Value, a, d, TimeSpan.Zero, TimeSpan.Zero);
                    MessageBus.Current.SendMessage(new UserSettingsChangedMessage(SelectedDate.Value));
                    return;
                }
                if (string.IsNullOrWhiteSpace(ArrivalTime)
                    || string.IsNullOrWhiteSpace(DepartureTime)
                    || string.IsNullOrWhiteSpace(LunchStartTime)
                    || string.IsNullOrWhiteSpace(LunchEndTime))
                {
                    SettingsService.DeleteUserSettingsForDate(date);
                }
                else if (TimeSpan.TryParse(ArrivalTime, out var arr)
                      && TimeSpan.TryParse(DepartureTime, out var dep)
                      && TimeSpan.TryParse(LunchStartTime, out var l0)
                      && TimeSpan.TryParse(LunchEndTime, out var l1))
                {
                    SettingsService.SaveUserSettingsForDate(date, arr, dep, l0, l1);
                }  
                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(date));
                IsBusy = true;
                var generator = new AutomaticEventsGeneratorService(
                    _eventService,
                    prompt => ShowCollisionMessage.Handle(prompt).FirstAsync().ToTask());
                await generator.RegenerateDailyEventsAsync(date);
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                _events = _eventService.LoadEvents();
                RecalculateWorkingHours();
                CurrentViewModel = CurrentViewModel;
                IsBusy = false;
            });
            MessageBus.Current.Listen<GlobalSettingsChangedMessage>()
            .Subscribe(_ =>
            {
                _events = _eventService.LoadEvents();
                RecalculateWorkingHours();
            });
        }
        public double LunchMinutes
        {
            get
            {
                if (TimeSpan.TryParse(LunchEndTime, out var end) && TimeSpan.TryParse(LunchStartTime, out var start))
                {
                    return (end - start).TotalMinutes;
                }
                return 0;
            }
        }

        public double DayHours
        {
            get
            {
                if (!TimeSpan.TryParse(ArrivalTime, out var arrival) ||
                    !TimeSpan.TryParse(DepartureTime, out var departure))
                    return 0;
                double total = (departure - arrival).TotalHours;
                double lunchDuration = LunchMinutes / 60.0;
                double netHours = total - lunchDuration;
                return netHours < 0 ? 0 : netHours;
            }
        }

        public string DayDisplay
        {
            get
            {
                return $"{DailyHours:F1} / 8";
            }
        }

        public string WeekDisplay
        {
            get
            {
                if (!SelectedWeek.HasValue)
                    return string.Empty;

                var weekEvents = _eventService.GetEventsForWeek(SelectedWeek.Value);
                if (!weekEvents.Any())
                    return string.Empty;

                var (actual, expected, overtime, undertime)
                    = _hoursCalculator.CalculateWeeklyStats(SelectedWeek.Value, weekEvents);

                return $"{actual:F1} / {expected:F0}";
            }
        }

        public string MonthDisplay
        {
            get
            {
                if (!SelectedMonth.HasValue) return string.Empty;
                var norm = MonthlyNorm; 
                return $"{MonthlyHours:F1} / {norm:F1}";
            }
        }

        public void OpenDayView()
        {
            DateTime date = SelectedDate ?? DateTime.Now;
            CurrentViewModel = new DayViewModel(date, newDate =>
            {
                SelectedDate = newDate;
            });
            MessageBus.Current.SendMessage(new UserSettingsChangedMessage(date));
            this.RaisePropertyChanged(nameof(IsDayViewVisible));
            this.RaisePropertyChanged(nameof(IsMonthViewVisible));
            this.RaisePropertyChanged(nameof(IsWeekViewVisible));
        }

        public void OpenWeekView()
        {
            DateTime date = SelectedWeek ?? DateTime.Now;
            CurrentViewModel = new WeekViewModel(date, newDate =>
            {
                SelectedWeek = newDate;
            });

            this.RaisePropertyChanged(nameof(IsDayViewVisible));
            this.RaisePropertyChanged(nameof(IsMonthViewVisible));
            this.RaisePropertyChanged(nameof(IsWeekViewVisible));
        }

        public void OpenMonthView()
        {
            DateTime date = SelectedMonth ?? DateTime.Now;
            CurrentViewModel = new MonthViewModel(date, newDate =>
            {
                SelectedMonth = newDate;
            });

            this.RaisePropertyChanged(nameof(IsDayViewVisible));
            this.RaisePropertyChanged(nameof(IsMonthViewVisible));
            this.RaisePropertyChanged(nameof(IsWeekViewVisible));
        }
        private void RecalculateWorkingHours()
        {
            if (!SelectedDate.HasValue)
                return;

            DateTime selected = SelectedDate.Value;
            DateTime selectedWeek = SelectedWeek.Value;
            DateTime selectedMonth = SelectedMonth.Value;

            DailyHours = _hoursCalculator.CalculateDailyHours(selected, _events);

            DateTime weekStart = selectedWeek.AddDays(-(int)(selectedWeek.DayOfWeek - DayOfWeek.Monday));
            var (actual, expected, overtime, undertime) = _hoursCalculator.CalculateWeeklyStats(weekStart, _events);
            WeeklyHours = actual;

            MonthlyHours = _hoursCalculator.CalculateMonthlyRedistributedHours(SelectedMonth.Value.Year,SelectedMonth.Value.Month,_events);

            this.RaisePropertyChanged(nameof(DayDisplay));
            this.RaisePropertyChanged(nameof(WeekDisplay));
            this.RaisePropertyChanged(nameof(MonthDisplay));
        }


        private void LoadUserSettingsForDate(DateTime date)
        {
            var dayEvents = _eventService.GetEventsForDay(date);

            var sem = GlobalSettingsService.GetSemesterForDate(date);
            var gl = GlobalSettingsService.LoadGlobalSettings(date.Year, sem)
                      ?? GlobalSettingsService.GetDefaultSettings(date.Year, sem);
            var (defArr, defDep, defLs, defLe) = PdfService.GetWeekdayDefaults(gl, date.DayOfWeek);
            var us = SettingsService.GetUserSettingsForDate(date); var eight = TimeSpan.FromHours(8);
            var netNorm = (defDep - defArr) - (defLe - defLs);
            if (netNorm < TimeSpan.Zero) netNorm = TimeSpan.Zero;

            DateTime dayArr = date.Date + defArr;
            DateTime dayDep = date.Date + defDep;

            TimeSpan IntersectLen(Event e)
            {
                var s = e.StartTime < dayArr ? dayArr : e.StartTime;
                var ee = e.EndTime > dayDep ? dayDep : e.EndTime;
                return ee > s ? (ee - s) : TimeSpan.Zero;
            }

            bool IsSpecial(Event e) => e.EventType != EventType.Work && e.EventType != EventType.Lunch;

            var specials = dayEvents.Where(IsSpecial).ToList();

            bool fullSpecialDay =
                specials.Any(e => e.AllDay) ||
                specials.Any(e => IntersectLen(e) >= (e.EventType == EventType.Vacation ? eight : netNorm));

            if (fullSpecialDay)
            {
                var sMin = specials.Select(e => e.StartTime < dayArr ? dayArr : e.StartTime).Min();
                var eMax = specials.Select(e => e.EndTime > dayDep ? dayDep : e.EndTime).Max();

                ArrivalTime = sMin.TimeOfDay.ToString(@"hh\:mm");
                DepartureTime = eMax.TimeOfDay.ToString(@"hh\:mm");
                LunchStartTime = "00:00";
                LunchEndTime = "00:00";
                return;
            }

            if (HolidayHelper.IsCzechHoliday(date))
            {
                ArrivalTime = defArr.ToString(@"hh\:mm");
                DepartureTime = defDep.ToString(@"hh\:mm");
                LunchStartTime = defLs.ToString(@"hh\:mm");
                LunchEndTime = defLe.ToString(@"hh\:mm");
                return;
            }

            if (us != null)
            {
                ArrivalTime = us.ArrivalTime.ToString(@"hh\:mm");
                DepartureTime = us.DepartureTime.ToString(@"hh\:mm");
                LunchStartTime = us.LunchStart.ToString(@"hh\:mm");
                LunchEndTime = us.LunchEnd.ToString(@"hh\:mm");
            }
            else
            {
                ArrivalTime = defArr.ToString(@"hh\:mm");
                DepartureTime = defDep.ToString(@"hh\:mm");
                LunchStartTime = defLs.ToString(@"hh\:mm");
                LunchEndTime = defLe.ToString(@"hh\:mm");
            }
        }

        private async Task GenerateEPDAsync()
        {
            try
            {
                var mainWindow = Helpers.Helper.GetMainWindow();
                if (mainWindow == null)
                {
                    Console.WriteLine("Hlavní okno nebylo nalezeno.");
                    return;
                }
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Vyberte soubor pro generování EPD",
                    AllowMultiple = false,
                    Filters =
                        {
                            new FileDialogFilter
                            {
                                Name = "CSV soubory",
                                Extensions = { "csv" }
                            },
                            new FileDialogFilter
                            {
                                Name = "Všechny soubory",
                                Extensions = { "*" }
                            }
                        }
                };             
                var result = await openFileDialog.ShowAsync(mainWindow);
                if (result == null || result.Length == 0)
                {
                   
                    return;
                }               
                var teacherScheduleCsvPath = result[0];
                if (!File.Exists(teacherScheduleCsvPath))
                {
                    Console.WriteLine("Soubor neexistuje.");
                    return;
                }
                var epdGenerator = new EPDGenerator(
                    _eventService,
                      prompt => this.ShowCollisionMessage
                     .Handle(prompt)          
                     .FirstAsync()            
                     .ToTask()                
);
                var events = await epdGenerator.GenerateEPDEventsAsync(teacherScheduleCsvPath);

                if (events != null && events.Count > 0)
                {
                    Console.WriteLine("EPD úspěšně vygenerováno.");
                }
                else
                {
                    Console.WriteLine("Chyba při generování EPD.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Výjimka: {ex.Message}");
            }
        }
        public async Task DeleteImportByIdAsync(ImportBatchItemViewModel item)
        {
            var main = Helpers.Helper.GetMainWindow();
            var box = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard(
                new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                    Icon = MsBox.Avalonia.Enums.Icon.Question,
                    ContentHeader = "Smazání načteného rozvrhu",
                    ContentMessage = $"Smazat načtený rozvrh «{item.Label}» ({item.EventsCount} událostí)?",
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            var res = await box.ShowWindowDialogAsync(main);
            if (res != MsBox.Avalonia.Enums.ButtonResult.Yes) return;

            _eventService.DeleteEventsByImportId(item.Id);

            RefreshImportBatches();

            var generator = new AutomaticEventsGeneratorService(
                _eventService,
                prompt => this.ShowCollisionMessage.Handle(prompt).FirstAsync().ToTask());

            await generator.RegenerateRangeEventsAsync(item.RangeStart, item.RangeEnd);

            _events = _eventService.LoadEvents();
            RecalculateWorkingHours();
            CurrentViewModel = CurrentViewModel;
        }

        public void RefreshImportBatches()
        {
            ImportBatches.Clear();
            foreach (var b in _eventService.GetImportBatches()
                                           .OrderByDescending(x => x.RangeStart))
            {
                ImportBatches.Add(new ImportBatchItemViewModel(b, this));
            }
        }
        private int GetWorkingDaysInMonth(int year, int month)
        {
            int daysInMonth = DateTime.DaysInMonth(year, month);
           
            int count = 0;
            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
               
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday && !HolidayHelper.IsCzechHoliday(date))
                {
                    count++;
                }
            }
            return count;
        }

        public double MonthlyNorm
        {
            get
            {
                if (!SelectedMonth.HasValue)
                    return 0;

                int workingDays = GetWorkingDaysInMonth(SelectedMonth.Value.Year, SelectedMonth.Value.Month);
                return workingDays * 8;
            }
        }
        private (TimeSpan arr, TimeSpan dep, TimeSpan ls, TimeSpan le)
        GetDaySpans(GlobalSettings g, UserSettings u, DayOfWeek wd)
        {
            
            TimeSpan arr, dep, ls, le;
            (arr, dep, ls, le) = PdfService.GetWeekdayDefaults(g, wd);
            if (u != null)
            {
                arr = u.ArrivalTime;
                dep = u.DepartureTime;
                ls = u.LunchStart;
                le = u.LunchEnd;
            }
            return (arr, dep, ls, le);
        }
      
    }
}
