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
using System.Collections.ObjectModel;

namespace TeacherScheduleApp.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private enum ViewKind { Day, Week, Month }

        private readonly int _employeeId = EventService.DefaultEmployeeId;

        private ViewKind _previousView;
        private ViewModelBase _currentViewModel;

        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
        }

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

        private int _dayActualMinutes;
        public int DayActualMinutes
        {
            get => _dayActualMinutes;
            set => this.RaiseAndSetIfChanged(ref _dayActualMinutes, value);
        }

        private int _dayExpectedMinutes;
        public int DayExpectedMinutes
        {
            get => _dayExpectedMinutes;
            set => this.RaiseAndSetIfChanged(ref _dayExpectedMinutes, value);
        }

        private int _weekActualMinutes;
        public int WeekActualMinutes
        {
            get => _weekActualMinutes;
            set => this.RaiseAndSetIfChanged(ref _weekActualMinutes, value);
        }

        private int _weekExpectedMinutes;
        public int WeekExpectedMinutes
        {
            get => _weekExpectedMinutes;
            set => this.RaiseAndSetIfChanged(ref _weekExpectedMinutes, value);
        }

        private int _monthActualMinutes;
        public int MonthActualMinutes
        {
            get => _monthActualMinutes;
            set => this.RaiseAndSetIfChanged(ref _monthActualMinutes, value);
        }

        private int _monthExpectedMinutes;
        public int MonthExpectedMinutes
        {
            get => _monthExpectedMinutes;
            set => this.RaiseAndSetIfChanged(ref _monthExpectedMinutes, value);
        }

        private string _busyText = "Načítám…";
        public string BusyText
        {
            get => _busyText;
            set => this.RaiseAndSetIfChanged(ref _busyText, value);
        }
        public bool IsLunchEnabled => !IsFullSpecialDay;

        public ReactiveCommand<Unit, Unit> ShowPdfPreview { get; }

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

                    this.RaisePropertyChanged(nameof(IsDayViewVisible));
                    this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                    this.RaisePropertyChanged(nameof(IsMonthViewVisible));

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

                    this.RaisePropertyChanged(nameof(IsDayViewVisible));
                    this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                    this.RaisePropertyChanged(nameof(IsMonthViewVisible));

                    RecalculateWorkingHours();
                }
            }
        }

        private string _arrivalTime = "08:00";
        public string ArrivalTime
        {
            get => _arrivalTime;
            set
            {
                this.RaiseAndSetIfChanged(ref _arrivalTime, value);
            }
        }

        private string _departureTime = "16:30";
        public string DepartureTime
        {
            get => _departureTime;
            set
            {
                this.RaiseAndSetIfChanged(ref _departureTime, value);
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

        private List<Event> _events = new();

        private readonly WorkingHoursCalculatorService _hoursCalculator;
        private readonly EventService _eventService;

        public ObservableCollection<ImportBatchItemViewModel> ImportBatches { get; } = new();

        public ReactiveCommand<Unit, Unit> CreateEventCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDayCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowWeekCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowMonthCommand { get; }
        public ReactiveCommand<Unit, Unit> GenerateEPDCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveUserSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenGlobalSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> DayTappedCommand { get; }
        public ReactiveCommand<Unit, Unit> RegenerateAllCommand { get; }

        public Interaction<string, bool> ShowCollisionMessage { get; } = new();

        public MainWindowViewModel()
        {
            _eventService = new EventService();
            _hoursCalculator = new WorkingHoursCalculatorService();

            _selectedDate = DateTime.Now;
            _selectedWeek = DateTime.Now;
            _selectedMonth = DateTime.Now;
            CalendarDisplayDate = DateTime.Today;

            EnsureInitialDefaults();

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

            ShowPdfPreview = ReactiveCommand.CreateFromTask(async () =>
            {
                try
                {
                    if (SelectedMonth == null)
                        return;

                    var selectedMonth = SelectedMonth.Value;
                    var year = selectedMonth.Year;
                    var month = selectedMonth.Month;

                    await RunBusyAsync("Připravuji PDF náhled…", async () =>
                    {
                        var eventService = new EventService();

                        await Task.Run(async () =>
                        {
                            var monthDays = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                                .Select(d => new DateTime(year, month, d))
                                .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                                         && !HolidayHelper.IsCzechHoliday(d))
                                .ToList();

                            var weekGroups = monthDays
                                .GroupBy(d => (ISOYear: System.Globalization.ISOWeek.GetYear(d),
                                               ISOWeek: System.Globalization.ISOWeek.GetWeekOfYear(d)))
                                .OrderBy(g => g.Key.ISOYear)
                                .ThenBy(g => g.Key.ISOWeek)
                                .ToList();

                            foreach (var g in weekGroups)
                            {
                                SetBusyTextSafe($"Vyvažuji týden {g.Key.ISOWeek:D2}…");

                                var anyDay = g.First();

                                var fpBefore = Helpers.BalanceFingerprint.ForWeek(
                                    eventService, _employeeId, anyDay);

                                bool isBalanced = Helpers.WeekBalanceStore.IsBalanced(
                                    _employeeId,
                                    g.Key.ISOYear,
                                    g.Key.ISOWeek,
                                    fpBefore);

                                if (!isBalanced)
                                {
                                    await eventService.BalanceWeekForDateAsync(anyDay, _employeeId);

                                    var weekDays = Enumerable.Range(0, 7)
                                        .Select(i =>
                                        {
                                            var d = anyDay.Date;
                                            int delta = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                                            var monday = d.AddDays(-delta);
                                            return monday.AddDays(i).Date;
                                        })
                                        .Where(EventService.IsWorkday)
                                        .ToList();

                                    if (eventService.IsScopeReallyBalanced(weekDays, _employeeId))
                                    {
                                        var fpAfter = Helpers.BalanceFingerprint.ForWeek(eventService, _employeeId, anyDay);

                                        Helpers.WeekBalanceStore.Save(
                                            _employeeId,
                                            System.Globalization.ISOWeek.GetYear(anyDay),
                                            System.Globalization.ISOWeek.GetWeekOfYear(anyDay),
                                            fpAfter);
                                    }
                                    else
                                    {
                                        Helpers.WeekBalanceStore.Invalidate(
                                            _employeeId,
                                            System.Globalization.ISOWeek.GetYear(anyDay),
                                            System.Globalization.ISOWeek.GetWeekOfYear(anyDay));
                                    }
                                }
                            }
                        });

                        SetBusyTextSafe("Generuji náhled PDF…");

                        var owner = Helper.GetMainWindow();

                        var vm = new PdfPreviewViewModel(
                            new PdfService(),
                            eventService,
                            selectedMonth,
                            _employeeId);

                        await vm.LoadInitialAsync();

                        var win = new PdfPreviewWindow
                        {
                            DataContext = vm,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };

                        win.Show(owner);
                    });
                }
                catch (PdfRenderException ex)
                {
                    var msg = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentHeader = "Chyba PDF",
                        ContentMessage = ex.Message,
                        ButtonDefinitions = ButtonEnum.Ok,
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                    await msg.ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());
                }
                catch (Exception ex)
                {
                    var msg = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentHeader = "Neočekávaná chyba",
                        ContentMessage = ex.Message,
                        ButtonDefinitions = ButtonEnum.Ok,
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                    await msg.ShowWindowDialogAsync(Helper.GetMainWindow());
                }
            });

            RegenerateAllCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                await RunBusyBackgroundAsync("Přegenerovávám automatické události pro celý rok…", async () =>
                {
                    var generator = new AutomaticEventsGeneratorService(
                        new EventService(),
                        prompt => AskCollisionOnUiAsync(prompt),
                        _employeeId);

                    var yearStart = new DateTime(DateTime.Now.Year, 1, 1);
                    var yearEnd = new DateTime(DateTime.Now.Year, 12, 31);

                    await generator.RegenerateRangeEventsAsync(yearStart, yearEnd);
                    MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                });
            }, outputScheduler: RxApp.TaskpoolScheduler);

            ShowCollisionMessage.RegisterHandler(async interaction =>
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var owner =
                        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?
                        .Windows?.FirstOrDefault(w => w.IsActive)
                        ?? Helpers.Helper.GetMainWindow();

                    var msgParams = new MessageBoxStandardParams
                    {
                        ButtonDefinitions = ButtonEnum.YesNo,
                        Icon = Icon.Question,
                        ContentHeader = "Kolize s obědem",
                        ContentMessage = interaction.Input,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    var msgBox = MessageBoxManager.GetMessageBoxStandard(msgParams);
                    var result = await msgBox.ShowWindowDialogAsync(owner);
                    interaction.SetOutput(result == ButtonResult.Yes);
                });
            });

            MessageBus.Current
                .Listen<AutoEventsGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    _events = _eventService.LoadEvents(_employeeId);
                
                    RefreshSelectedDaySettingsPanel();
                    RecalculateWorkingHours();
                
                    if (IsDayViewVisible) OpenDayView();
                    else if (IsWeekViewVisible) OpenWeekView();
                    else if (IsMonthViewVisible) OpenMonthView();
                });

            MessageBus.Current
                .Listen<UserSettingsChangedMessage>()
                .Where(m => SelectedDate.HasValue && m.Date.Date == SelectedDate.Value.Date)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(m => LoadUserSettingsForDate(m.Date));

            MessageBus.Current
                .Listen<EpdGeneratedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    RefreshImportBatchesAsync();
                
                    RefreshSelectedDaySettingsPanel();
                    RecalculateWorkingHours();
                
                    if (IsDayViewVisible) OpenDayView();
                    else if (IsWeekViewVisible) OpenWeekView();
                    else if (IsMonthViewVisible) OpenMonthView();
                });

            MessageBus.Current
                .Listen<GlobalSettingsChangedMessage>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    _events = _eventService.LoadEvents(_employeeId);
                    RecalculateWorkingHours();
                });

            CurrentViewModel = new DayViewModel(DateTime.Now, newDate =>
            {
                SelectedDate = newDate;
            });

            IsDayViewVisible = true;
            _events = _eventService.LoadEvents(_employeeId);
            RecalculateWorkingHours();
            LoadUserSettingsForDate(DateTime.Today);

            CreateEventCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (!SelectedDate.HasValue)
                    return;

                var day = SelectedDate.Value.Date;
                var resolved = SettingsService.GetResolvedDaySettings(day, _employeeId);

                var vm = new CreateEventDialogViewModel(day + resolved.ArrivalTime)
                {
                    EndDate = day,
                    EndTime = resolved.DepartureTime,
                    ArrivalTime = day + resolved.ArrivalTime,
                    DepartureTime = day + resolved.DepartureTime,
                    LunchStart = day + resolved.LunchStart,
                    LunchEnd = day + resolved.LunchEnd,
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

                if (ev.EventType == EventType.Lunch)
                {
                    var lunchGenerator = new AutomaticEventsGeneratorService(
                        _eventService,
                        prompt => AskCollisionOnUiAsync(prompt),
                        _employeeId);

                    var lunchPrep = await lunchGenerator.PrepareManualLunchAsync(ev);

                    if (!lunchPrep.Ok)
                    {
                        await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentHeader = "Nelze vytvořit oběd",
                            ContentMessage = lunchPrep.Error ?? "Neznámá chyba.",
                            ButtonDefinitions = ButtonEnum.Ok,
                            Icon = Icon.Warning,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        }).ShowWindowDialogAsync(main);

                        return;
                    }

                    ev = lunchPrep.Event!;
                }

                await RunBusyBackgroundAsync("Ukládám událost a aktualizuji den…", async () =>
                {
                    ev.EmployeeId = _employeeId;

                    if (ev.IsDeleted)
                    {
                        _eventService.DeleteEvent(ev.Id, _employeeId);
                    }
                    else if (ev.Id != 0)
                    {
                        ev.IsAutoGenerated = false;
                        _eventService.UpdateEvent(ev);
                    }
                    else
                    {
                        _eventService.CreateEvent(ev);
                    }

                    var generator = new AutomaticEventsGeneratorService(
                        _eventService,
                        prompt => AskCollisionOnUiAsync(prompt),
                        _employeeId);

                    await generator.RegenerateRangeEventsAsync(ev.StartTime.Date, ev.EndTime.Date);

                    MessageBus.Current.SendMessage(new UserSettingsChangedMessage(day));
                    MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());

                    _events = _eventService.LoadEvents(_employeeId);
                    RecalculateWorkingHours();
                    CurrentViewModel = CurrentViewModel;
                });
            });
            
            GenerateEPDCommand = ReactiveCommand.CreateFromTask(GenerateEPDAsync);

            ShowDayCommand = ReactiveCommand.Create(() =>
            {
                OpenDayView();

                IsDayViewVisible = true;
                IsWeekViewVisible = false;
                IsMonthViewVisible = false;

                this.RaisePropertyChanged(nameof(IsDayViewVisible));
                this.RaisePropertyChanged(nameof(IsWeekViewVisible));
                this.RaisePropertyChanged(nameof(IsMonthViewVisible));

                _events = _eventService.LoadEvents(_employeeId);
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

                _events = _eventService.LoadEvents(_employeeId);
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

                _events = _eventService.LoadEvents(_employeeId);
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
                        case ViewKind.Day:
                            OpenDayView();
                            IsDayViewVisible = true;
                            IsWeekViewVisible = false;
                            IsMonthViewVisible = false;
                            break;

                        case ViewKind.Week:
                            OpenWeekView();
                            IsDayViewVisible = false;
                            IsWeekViewVisible = true;
                            IsMonthViewVisible = false;
                            break;

                        default:
                            OpenMonthView();
                            IsDayViewVisible = false;
                            IsWeekViewVisible = false;
                            IsMonthViewVisible = true;
                            break;
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

            SaveUserSettingsCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (!SelectedDate.HasValue)
                    return;

                var date = SelectedDate.Value.Date;

                if (IsFullSpecialDay &&
                    TimeSpan.TryParse(ArrivalTime, out var a) &&
                    TimeSpan.TryParse(DepartureTime, out var d))
                {
                    SettingsService.SaveDaySettingsForDate(date, a, d, TimeSpan.Zero, TimeSpan.Zero, _employeeId, true);
                    MessageBus.Current.SendMessage(new UserSettingsChangedMessage(date));
                    return;
                }

                if (string.IsNullOrWhiteSpace(ArrivalTime) ||
                    string.IsNullOrWhiteSpace(DepartureTime) ||
                    string.IsNullOrWhiteSpace(LunchStartTime) ||
                    string.IsNullOrWhiteSpace(LunchEndTime))
                {
                    SettingsService.DeleteDaySettingsForDate(date, _employeeId);
                }
                else if (TimeSpan.TryParse(ArrivalTime, out var arr) &&
                         TimeSpan.TryParse(DepartureTime, out var dep) &&
                         TimeSpan.TryParse(LunchStartTime, out var l0) &&
                         TimeSpan.TryParse(LunchEndTime, out var l1))
                {
                    if (dep <= arr)
                    {
                        await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentHeader = "Chyba",
                            ContentMessage = "Odchod nesmí být před nebo roven příchodu.",
                            ButtonDefinitions = ButtonEnum.Ok,
                            Icon = Icon.Error
                        }).ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());
                        return;
                    }

                    if (l1 <= l0)
                    {
                        await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentHeader = "Chyba",
                            ContentMessage = "Konec oběda nesmí být před nebo roven začátku.",
                            ButtonDefinitions = ButtonEnum.Ok,
                            Icon = Icon.Error
                        }).ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());
                        return;
                    }

                    if (l0 < arr || l1 > dep)
                    {
                        await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentHeader = "Chyba",
                            ContentMessage = "Oběd musí být uvnitř pracovní doby.",
                            ButtonDefinitions = ButtonEnum.Ok,
                            Icon = Icon.Error
                        }).ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());
                        return;
                    }

                    SettingsService.SaveDaySettingsForDate(date, arr, dep, l0, l1, _employeeId);
                }

                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(date));

                await RunBusyBackgroundAsync("Ukládám nastavení dne a přegenerovávám události…", async () =>
                {
                    var generator = new AutomaticEventsGeneratorService(
                        _eventService,
                        prompt => AskCollisionOnUiAsync(prompt),
                        _employeeId);

                    await generator.RegenerateDailyEventsAsync(date, preserveUserSettings: true);

                    MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());

                    _events = _eventService.LoadEvents(_employeeId);
                    RecalculateWorkingHours();
                    CurrentViewModel = CurrentViewModel;
                });
            });

            RefreshImportBatchesAsync();
            RxApp.MainThreadScheduler.ScheduleAsync(async (_, __) =>
            {
                await InitializeAutoEventsAsync();
                return Disposable.Empty;
            });
        }

        private void EnsureInitialDefaults()
        {
            var year = (SelectedDate ?? DateTime.Today).Year;

            GlobalSettingsService.EnsureDefaultEmployee(_employeeId);

            GlobalSettingsService.EnsureDefaultSemesterSettingsAsync(year, SemesterType.Winter, _employeeId)
                .GetAwaiter()
                .GetResult();

            GlobalSettingsService.EnsureDefaultSemesterSettingsAsync(year, SemesterType.Summer, _employeeId)
                .GetAwaiter()
                .GetResult();
        }

        private async Task InitializeAutoEventsAsync()
        {
            await RunBusyBackgroundAsync("Inicializuji automatické události…", async () =>
            {
                EnsureInitialDefaults();

                var generator = new AutomaticEventsGeneratorService(
                    _eventService,
                    _ => Task.FromResult(true),
                    _employeeId);

                var year = DateTime.Today.Year;
                var from = new DateTime(year, 1, 1);
                var to = new DateTime(year, 12, 31);

                await generator.RegenerateRangeEventsAsync(from, to);

                _events = _eventService.LoadEvents(_employeeId);
                RefreshImportBatchesAsync();
                RecalculateWorkingHours();

                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
            });
        }

        public int LunchMinutes
        {
            get
            {
                if (!TimeSpan.TryParse(LunchEndTime, out var end) ||
                    !TimeSpan.TryParse(LunchStartTime, out var start))
                    return 0;

                return Math.Max(0, (int)(end - start).TotalMinutes);
            }
        }

        public int ConfiguredDayMinutes
        {
            get
            {
                if (!TimeSpan.TryParse(ArrivalTime, out var arrival) ||
                    !TimeSpan.TryParse(DepartureTime, out var departure))
                    return 0;

                var totalMinutes = (int)(departure - arrival).TotalMinutes;
                var netMinutes = totalMinutes - LunchMinutes;
                return Math.Max(0, netMinutes);
            }
        }

        private static string FormatMinutes(int totalMinutes)
        {
            string sign = totalMinutes < 0 ? "-" : "";
            totalMinutes = Math.Abs(totalMinutes);

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            return $"{sign}{hours:00}:{minutes:00}";
        }

        public string ConfiguredDayDisplay => FormatMinutes(ConfiguredDayMinutes);

        public string DayDisplay
        {
            get
            {
                if (!SelectedDate.HasValue)
                    return string.Empty;

                return $"{FormatMinutes(DayActualMinutes)} / {FormatMinutes(DayExpectedMinutes)}";
            }
        }

        public string WeekDisplay
        {
            get
            {
                if (!SelectedWeek.HasValue)
                    return string.Empty;

                var weekEvents = _eventService.GetEventsForWeek(_employeeId, SelectedWeek.Value);
                if (!weekEvents.Any())
                    return string.Empty;

                var byMonth = _hoursCalculator.WeeklyDisplayMetricsByMonth(
                    SelectedWeek.Value,
                    weekEvents,
                    _employeeId);

                if (byMonth.Count == 1)
                {
                    var only = byMonth.First().Value;
                    return $"{FormatMinutes(only.ActualMinutes)} / {FormatMinutes(only.ExpectedMinutes)}";
                }

                var parts = byMonth
                    .OrderBy(kv => kv.Key.Year)
                    .ThenBy(kv => kv.Key.Month)
                    .Select(kv =>
                    {
                        var key = kv.Key;
                        var v = kv.Value;
                        var monthName = new DateTime(key.Year, key.Month, 1)
                            .ToString("MMMM", System.Globalization.CultureInfo.CurrentCulture);

                        return $"{monthName}: {FormatMinutes(v.ActualMinutes)} / {FormatMinutes(v.ExpectedMinutes)}";
                    });

                return string.Join(" | ", parts);
            }
        }

        public string MonthDisplay
        {
            get
            {
                if (!SelectedMonth.HasValue)
                    return string.Empty;

                return $"{FormatMinutes(MonthActualMinutes)} / {FormatMinutes(MonthExpectedMinutes)}";
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
            if (!SelectedDate.HasValue || !SelectedWeek.HasValue || !SelectedMonth.HasValue)
                return;

            var selectedDate = SelectedDate.Value.Date;
            var selectedWeek = SelectedWeek.Value.Date;
            var selectedMonth = SelectedMonth.Value;

            var day = _hoursCalculator.DailyDisplayMetrics(selectedDate, _events, _employeeId);
            DayActualMinutes = day.ActualMinutes;
            DayExpectedMinutes = day.ExpectedMinutes;

            var week = _hoursCalculator.WeeklyDisplayMetrics(selectedWeek, _events, _employeeId);
            WeekActualMinutes = week.ActualMinutes;
            WeekExpectedMinutes = week.ExpectedMinutes;

            var month = _hoursCalculator.MonthlyDisplayMetrics(
                selectedMonth.Year,
                selectedMonth.Month,
                _events,
                _employeeId);

            MonthActualMinutes = month.ActualMinutes;
            MonthExpectedMinutes = month.ExpectedMinutes;

            this.RaisePropertyChanged(nameof(ConfiguredDayDisplay));
            this.RaisePropertyChanged(nameof(DayDisplay));
            this.RaisePropertyChanged(nameof(WeekDisplay));
            this.RaisePropertyChanged(nameof(MonthDisplay));
        }

        private void LoadUserSettingsForDate(DateTime date)
        {
            var dayEvents = _eventService.GetEventsForDay(_employeeId, date);
            var resolved = SettingsService.GetResolvedDaySettings(date, _employeeId);
            var dayOverride = SettingsService.GetDaySettingsForDate(date, _employeeId);

            var eight = TimeSpan.FromHours(8);
            var netNorm = (resolved.DepartureTime - resolved.ArrivalTime) - (resolved.LunchEnd - resolved.LunchStart);
            if (netNorm < TimeSpan.Zero)
                netNorm = TimeSpan.Zero;

            DateTime dayArr = date.Date + resolved.ArrivalTime;
            DateTime dayDep = date.Date + resolved.DepartureTime;

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

            IsFullSpecialDay = fullSpecialDay;

            if (fullSpecialDay && specials.Any())
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
                ArrivalTime = resolved.ArrivalTime.ToString(@"hh\:mm");
                DepartureTime = resolved.DepartureTime.ToString(@"hh\:mm");
                LunchStartTime = resolved.LunchStart.ToString(@"hh\:mm");
                LunchEndTime = resolved.LunchEnd.ToString(@"hh\:mm");
                return;
            }

            if (dayOverride != null)
            {
                ArrivalTime = dayOverride.ArrivalTime.ToString(@"hh\:mm");
                DepartureTime = dayOverride.DepartureTime.ToString(@"hh\:mm");
                LunchStartTime = dayOverride.LunchStart.ToString(@"hh\:mm");
                LunchEndTime = dayOverride.LunchEnd.ToString(@"hh\:mm");
            }
            else
            {
                ArrivalTime = resolved.ArrivalTime.ToString(@"hh\:mm");
                DepartureTime = resolved.DepartureTime.ToString(@"hh\:mm");
                LunchStartTime = resolved.LunchStart.ToString(@"hh\:mm");
                LunchEndTime = resolved.LunchEnd.ToString(@"hh\:mm");
            }
        }

        private async Task GenerateEPDAsync()
        {
            try
            {
                var mainWindow = Helpers.Helper.GetMainWindow();
                if (mainWindow == null)
                {
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
                    return;
                }

                await RunBusyAsync("Načítám rozvrh…", async () =>
                {
                    var epdGenerator = new EPDGenerator(
                        _eventService,
                        prompt => AskCollisionOnUiAsync(prompt),
                        _employeeId,
                        status => SetBusyTextSafe(status));

                    var report = await Task.Run(() =>
                        epdGenerator.GenerateEPDEventsWithReportAsync(teacherScheduleCsvPath));

                    SetBusyTextSafe("Obnovuji zobrazení…");

                    RefreshImportBatchesAsync();
                    _events = _eventService.LoadEvents(_employeeId);
                    RecalculateWorkingHours();

                    var summary =
                        $"Načteno řádků: {report.TotalRows}\n" +
                        $"Importováno: {report.ImportedRows}\n" +
                        $"Přeskočeno: {report.SkippedRows}\n" +
                        $"Vytvořeno událostí: {report.Events.Count}";

                    if (report.Errors.Any())
                    {
                        summary += "\n\nPrvní chyby:\n" +
                                   string.Join("\n", report.Errors.Take(10));
                    }

                    await MessageBoxManager.GetMessageBoxStandard(
                        new MessageBoxStandardParams
                        {
                            ContentHeader = "Import EPD",
                            ContentMessage = summary,
                            ButtonDefinitions = ButtonEnum.Ok,
                            Icon = report.Events.Any() ? Icon.Success : Icon.Warning,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        })
                        .ShowWindowDialogAsync(mainWindow);
                });
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard(
                    new MessageBoxStandardParams
                    {
                        ContentHeader = "Chyba importu",
                        ContentMessage = ex.ToString(),
                        ButtonDefinitions = ButtonEnum.Ok,
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    })
                    .ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());
            }
        }

        private Task<bool> AskCollisionOnUiAsync(string prompt)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var owner =
                        (Avalonia.Application.Current?.ApplicationLifetime
                            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?
                        .Windows?.FirstOrDefault(w => w.IsActive)
                        ?? Helpers.Helper.GetMainWindow();

                    var msgBox = MessageBoxManager.GetMessageBoxStandard(
                        new MessageBoxStandardParams
                        {
                            ButtonDefinitions = ButtonEnum.YesNo,
                            Icon = Icon.Question,
                            ContentHeader = "Kolize s obědem",
                            ContentMessage = prompt,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        });

                    var result = await msgBox.ShowWindowDialogAsync(owner);
                    tcs.TrySetResult(result == ButtonResult.Yes);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        public async Task DeleteImportByIdAsync(ImportBatchItemViewModel item)
        {
            var main = Helpers.Helper.GetMainWindow();

            var box = MessageBoxManager.GetMessageBoxStandard(
                new MessageBoxStandardParams
                {
                    ButtonDefinitions = ButtonEnum.YesNo,
                    Icon = Icon.Question,
                    ContentHeader = "Smazání načteného rozvrhu",
                    ContentMessage = $"Smazat načtený rozvrh «{item.Label}» ({item.EventsCount} událostí)?",
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            var res = await box.ShowWindowDialogAsync(main);
            if (res != ButtonResult.Yes)
                return;

            await RunBusyBackgroundAsync("Mažu načtený rozvrh…", async () =>
            {
                await _eventService.DeleteEventsByImportIdFastAsync(item.Id, _employeeId);

                SetBusyTextSafe("Obnovuji zobrazení…");

                RefreshImportBatchesAsync();
                _events = _eventService.LoadEvents(_employeeId);
                RecalculateWorkingHours();
                CurrentViewModel = CurrentViewModel;
            });
        }

        private async Task RefreshImportBatchesAsync()
        {
            var rows = await Task.Run(() =>
                _eventService.GetImportBatches(_employeeId)
                             .OrderByDescending(x => x.RangeStart)
                             .ToList());

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ImportBatches.Clear();

                foreach (var b in rows)
                    ImportBatches.Add(new ImportBatchItemViewModel(b, this));
            });
        }

        private int GetWorkingDaysInMonth(int year, int month)
        {
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int count = 0;

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                if (date.DayOfWeek != DayOfWeek.Saturday &&
                    date.DayOfWeek != DayOfWeek.Sunday &&
                    !HolidayHelper.IsCzechHoliday(date))
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

        private int _busyCounter = 0;
        private int _busySuspendCounter = 0;
        private async Task RunBusyAsync(string initialText, Func<Task> action)
        {
            _busyCounter++;
            IsBusy = true;
            BusyText = initialText;

            await Task.Yield();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => { },
                Avalonia.Threading.DispatcherPriority.Background);

            try
            {
                await action();
            }
            finally
            {
                _busyCounter--;

                if (_busyCounter <= 0)
                {
                    _busyCounter = 0;
                    BusyText = "Načítám…";
                    IsBusy = false;
                }
            }
        }

        private void SetBusyTextSafe(string text)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                BusyText = text;
            });
        }

        private void RefreshSelectedDaySettingsPanel()
        {
            if (!SelectedDate.HasValue)
                return;

            LoadUserSettingsForDate(SelectedDate.Value);

            this.RaisePropertyChanged(nameof(ArrivalTime));
            this.RaisePropertyChanged(nameof(DepartureTime));
            this.RaisePropertyChanged(nameof(LunchStartTime));
            this.RaisePropertyChanged(nameof(LunchEndTime));
            this.RaisePropertyChanged(nameof(LunchMinutes));
            this.RaisePropertyChanged(nameof(IsLunchEnabled));
        }

        private async Task RunBusyBackgroundAsync(string initialText, Func<Task> backgroundAction)
        {
            await RunBusyAsync(initialText, async () =>
            {
                await Task.Run(backgroundAction);
            });
        }
    }
}