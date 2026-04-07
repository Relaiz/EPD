using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;

namespace TeacherScheduleApp.ViewModels
{
    public class GlobalUserSettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly CompositeDisposable _disposables = new();
        private readonly EventService _eventService;
        private readonly Action _closeRequested;
        private readonly int _employeeId;

        private GlobalSettingsFormModel _originalSettings = new();

        public string HeaderDisplay => $"Rok {ActiveYear} · {CurrentSemesterDisplay}";
        public string DaysHeaderDisplay => $"Pracovní doba · {CurrentSemesterDisplay}";
        public string WorkTimeHeaderDisplay => $"Globální pracovní doba · {CurrentSemesterDisplay}";

        public bool ActiveSemesterIsWinter
        {
            get => ActiveSemester == SemesterType.Winter;
            set
            {
                if (value)
                    ActiveSemester = SemesterType.Winter;
            }
        }

        public bool ActiveSemesterIsSummer
        {
            get => ActiveSemester == SemesterType.Summer;
            set
            {
                if (value)
                    ActiveSemester = SemesterType.Summer;
            }
        }

        private int _activeYear;
        public int ActiveYear
        {
            get => _activeYear;
            set => this.RaiseAndSetIfChanged(ref _activeYear, value);
        }

        public ObservableCollection<int> AvailableYears { get; } = new();

        public Interaction<string, bool> ShowCollisionMessage { get; } = new();

        private SemesterType _activeSemester;
        public SemesterType ActiveSemester
        {
            get => _activeSemester;
            set
            {
                if (_activeSemester == value)
                    return;

                this.RaiseAndSetIfChanged(ref _activeSemester, value);
                this.RaisePropertyChanged(nameof(CurrentSemesterDisplay));
                this.RaisePropertyChanged(nameof(DaysHeaderDisplay));
                this.RaisePropertyChanged(nameof(WorkTimeHeaderDisplay));
                this.RaisePropertyChanged(nameof(HeaderDisplay));
                this.RaisePropertyChanged(nameof(ActiveSemesterIsWinter));
                this.RaisePropertyChanged(nameof(ActiveSemesterIsSummer));
            }
        }

        public string CurrentSemesterDisplay =>
            ActiveSemester == SemesterType.Winter
                ? "Zimní semestr"
                : "Letní semestr";

        private GlobalSettingsFormModel _currentSettings = new();
        public GlobalSettingsFormModel CurrentSettings
        {
            get => _currentSettings;
            set => this.RaiseAndSetIfChanged(ref _currentSettings, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> GoBackCommand { get; }

        public GlobalUserSettingsViewModel(
            int initialYear,
            SemesterType initialSemester,
            Action closeRequested,
            int employeeId = EventService.DefaultEmployeeId)
        {
            _closeRequested = closeRequested ?? (() => { });
            _employeeId = employeeId;
            _eventService = new EventService();

            ActiveYear = initialYear;
            ActiveSemester = initialSemester;

            var years = GlobalSettingsService.GetYearsWithData(_employeeId);
            if (!years.Contains(initialYear))
                years.Add(initialYear);

            foreach (var y in years.Distinct().OrderBy(y => y))
                AvailableYears.Add(y);

            GoBackCommand = ReactiveCommand.Create(() => _closeRequested());

            ShowCollisionMessage.RegisterHandler(async inter =>
            {
                var mb = new MessageBoxStandardParams
                {
                    ButtonDefinitions = ButtonEnum.YesNo,
                    Icon = Icon.Question,
                    ContentHeader = "Kolize s obědem",
                    ContentMessage = inter.Input,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var result = await MessageBoxManager
                    .GetMessageBoxStandard(mb)
                    .ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());

                inter.SetOutput(result == ButtonResult.Yes);
            });

            this.WhenAnyValue(x => x.ActiveYear, x => x.ActiveSemester)
                .Subscribe(x =>
                {
                    LoadFor(x.Item1, x.Item2);
                    this.RaisePropertyChanged(nameof(HeaderDisplay));
                    this.RaisePropertyChanged(nameof(DaysHeaderDisplay));
                    this.RaisePropertyChanged(nameof(WorkTimeHeaderDisplay));
                    this.RaisePropertyChanged(nameof(ActiveSemesterIsWinter));
                    this.RaisePropertyChanged(nameof(ActiveSemesterIsSummer));
                })
                .DisposeWith(_disposables);

            SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync)
                .DisposeWith(_disposables);

            MessageBus.Current
                .Listen<GlobalSettingsChangedMessage>()
                .Where(msg => msg.Semester == ActiveSemester)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => LoadFor(ActiveYear, ActiveSemester))
                .DisposeWith(_disposables);

            LoadFor(ActiveYear, ActiveSemester);
        }

        private void LoadFor(int year, SemesterType sem)
        {
            var settings = GlobalSettingsService.LoadSemesterSettings(year, sem, _employeeId)
                           ?? GlobalSettingsService.GetDefaultSettings(year, sem, _employeeId);

            var employee = GlobalSettingsService.EnsureDefaultEmployee(_employeeId);

            var form = MapFromSemesterSettings(settings);
            form.EmployeeName = employee.FullName;
            form.Department = employee.Department;

            CurrentSettings = form;
            _originalSettings = CurrentSettings.Clone();
        }

        private SemesterSettings BuildSemesterSettings()
        {
            return new SemesterSettings
            {
                EmployeeId = _employeeId,
                Year = ActiveYear,
                Semester = ActiveSemester,
                GlobalStartTime = Safe(CurrentSettings.GlobalStartTime),
                GlobalEndTime = Safe(CurrentSettings.GlobalEndTime),
                MinBreakDuration = Safe(CurrentSettings.MinBreakDuration),
                MaxBreakDuration = Safe(CurrentSettings.MaxBreakDuration),
                AutoEventNamePreLunch = Safe(CurrentSettings.AutoEventNamePreLunch),
                AutoEventNameLunch = Safe(CurrentSettings.AutoEventNameLunch),
                AutoEventNamePostLunch = Safe(CurrentSettings.AutoEventNamePostLunch),
                WeekdaySettings = new List<WeekdaySettings>
                {
                    new()
                    {
                        DayOfWeek = 1,
                        ArrivalTime = ParseTime(CurrentSettings.MondayArrival),
                        DepartureTime = ParseTime(CurrentSettings.MondayDeparture),
                        LunchStart = ParseTime(CurrentSettings.MondayLunchStart),
                        LunchEnd = ParseTime(CurrentSettings.MondayLunchEnd)
                    },
                    new()
                    {
                        DayOfWeek = 2,
                        ArrivalTime = ParseTime(CurrentSettings.TuesdayArrival),
                        DepartureTime = ParseTime(CurrentSettings.TuesdayDeparture),
                        LunchStart = ParseTime(CurrentSettings.TuesdayLunchStart),
                        LunchEnd = ParseTime(CurrentSettings.TuesdayLunchEnd)
                    },
                    new()
                    {
                        DayOfWeek = 3,
                        ArrivalTime = ParseTime(CurrentSettings.WednesdayArrival),
                        DepartureTime = ParseTime(CurrentSettings.WednesdayDeparture),
                        LunchStart = ParseTime(CurrentSettings.WednesdayLunchStart),
                        LunchEnd = ParseTime(CurrentSettings.WednesdayLunchEnd)
                    },
                    new()
                    {
                        DayOfWeek = 4,
                        ArrivalTime = ParseTime(CurrentSettings.ThursdayArrival),
                        DepartureTime = ParseTime(CurrentSettings.ThursdayDeparture),
                        LunchStart = ParseTime(CurrentSettings.ThursdayLunchStart),
                        LunchEnd = ParseTime(CurrentSettings.ThursdayLunchEnd)
                    },
                    new()
                    {
                        DayOfWeek = 5,
                        ArrivalTime = ParseTime(CurrentSettings.FridayArrival),
                        DepartureTime = ParseTime(CurrentSettings.FridayDeparture),
                        LunchStart = ParseTime(CurrentSettings.FridayLunchStart),
                        LunchEnd = ParseTime(CurrentSettings.FridayLunchEnd)
                    }
                }
            };
        }

        private GlobalSettingsFormModel MapFromSemesterSettings(SemesterSettings s)
        {
            var byDay = s.WeekdaySettings?
                .OrderBy(x => x.DayOfWeek)
                .ToDictionary(x => x.DayOfWeek)
                ?? new Dictionary<int, WeekdaySettings>();

            WeekdaySettings GetOrDefault(int day)
            {
                if (byDay.TryGetValue(day, out var value))
                    return value;

                var defaults = GlobalSettingsService.GetDefaultSettings(s.Year, s.Semester, s.EmployeeId);
                return defaults.WeekdaySettings.First(x => x.DayOfWeek == day);
            }

            var mon = GetOrDefault(1);
            var tue = GetOrDefault(2);
            var wed = GetOrDefault(3);
            var thu = GetOrDefault(4);
            var fri = GetOrDefault(5);

            return new GlobalSettingsFormModel
            {
                GlobalStartTime = s.GlobalStartTime,
                GlobalEndTime = s.GlobalEndTime,
                MinBreakDuration = s.MinBreakDuration,
                MaxBreakDuration = s.MaxBreakDuration,
                AutoEventNamePreLunch = s.AutoEventNamePreLunch,
                AutoEventNameLunch = s.AutoEventNameLunch,
                AutoEventNamePostLunch = s.AutoEventNamePostLunch,

                MondayArrival = mon.ArrivalTime.ToString(@"hh\:mm"),
                MondayDeparture = mon.DepartureTime.ToString(@"hh\:mm"),
                MondayLunchStart = mon.LunchStart.ToString(@"hh\:mm"),
                MondayLunchEnd = mon.LunchEnd.ToString(@"hh\:mm"),

                TuesdayArrival = tue.ArrivalTime.ToString(@"hh\:mm"),
                TuesdayDeparture = tue.DepartureTime.ToString(@"hh\:mm"),
                TuesdayLunchStart = tue.LunchStart.ToString(@"hh\:mm"),
                TuesdayLunchEnd = tue.LunchEnd.ToString(@"hh\:mm"),

                WednesdayArrival = wed.ArrivalTime.ToString(@"hh\:mm"),
                WednesdayDeparture = wed.DepartureTime.ToString(@"hh\:mm"),
                WednesdayLunchStart = wed.LunchStart.ToString(@"hh\:mm"),
                WednesdayLunchEnd = wed.LunchEnd.ToString(@"hh\:mm"),

                ThursdayArrival = thu.ArrivalTime.ToString(@"hh\:mm"),
                ThursdayDeparture = thu.DepartureTime.ToString(@"hh\:mm"),
                ThursdayLunchStart = thu.LunchStart.ToString(@"hh\:mm"),
                ThursdayLunchEnd = thu.LunchEnd.ToString(@"hh\:mm"),

                FridayArrival = fri.ArrivalTime.ToString(@"hh\:mm"),
                FridayDeparture = fri.DepartureTime.ToString(@"hh\:mm"),
                FridayLunchStart = fri.LunchStart.ToString(@"hh\:mm"),
                FridayLunchEnd = fri.LunchEnd.ToString(@"hh\:mm")
            };
        }

        private bool ValidateSettings()
        {
            bool TryParse(string? s, out TimeSpan ts)
                => TimeSpan.TryParse(s, out ts);

            if (!TryParse(CurrentSettings.GlobalStartTime, out var g0) ||
                !TryParse(CurrentSettings.GlobalEndTime, out var g1) ||
                g0 >= g1)
            {
                return false;
            }

            foreach (var day in new[]
            {
                ("Monday", CurrentSettings.MondayArrival, CurrentSettings.MondayLunchStart, CurrentSettings.MondayLunchEnd, CurrentSettings.MondayDeparture),
                ("Tuesday", CurrentSettings.TuesdayArrival, CurrentSettings.TuesdayLunchStart, CurrentSettings.TuesdayLunchEnd, CurrentSettings.TuesdayDeparture),
                ("Wednesday", CurrentSettings.WednesdayArrival, CurrentSettings.WednesdayLunchStart, CurrentSettings.WednesdayLunchEnd, CurrentSettings.WednesdayDeparture),
                ("Thursday", CurrentSettings.ThursdayArrival, CurrentSettings.ThursdayLunchStart, CurrentSettings.ThursdayLunchEnd, CurrentSettings.ThursdayDeparture),
                ("Friday", CurrentSettings.FridayArrival, CurrentSettings.FridayLunchStart, CurrentSettings.FridayLunchEnd, CurrentSettings.FridayDeparture),
            })
            {
                if (string.IsNullOrWhiteSpace(day.Item2) &&
                    string.IsNullOrWhiteSpace(day.Item3) &&
                    string.IsNullOrWhiteSpace(day.Item4) &&
                    string.IsNullOrWhiteSpace(day.Item5))
                {
                    continue;
                }

                if (!TryParse(day.Item2, out var a) ||
                    !TryParse(day.Item3, out var l0) ||
                    !TryParse(day.Item4, out var l1) ||
                    !TryParse(day.Item5, out var d))
                {
                    return false;
                }

                if (a < g0 || a >= l0 || l0 >= l1 || l1 >= d || d > g1)
                    return false;
            }

            if (!TryParse(CurrentSettings.MinBreakDuration, out var minB) ||
                !TryParse(CurrentSettings.MaxBreakDuration, out var maxB) ||
                minB > maxB)
            {
                return false;
            }

            return true;
        }

        private async Task SaveAsync()
        {
            if (!ValidateSettings())
            {
                await MessageBoxManager.GetMessageBoxStandard(
                    "Chyba",
                    "Nastavení nejsou validní. Zkontrolujte časové hodnoty.",
                    ButtonEnum.Ok,
                    Icon.Error)
                    .ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());

                return;
            }

            IsBusy = true;

            try
            {
                var semesterSettings = BuildSemesterSettings();

                await GlobalSettingsService.SaveEmployeeInfoAsync(
                    _employeeId,
                    CurrentSettings.EmployeeName,
                    CurrentSettings.Department);

                await GlobalSettingsService.SaveSemesterSettingsAsync(
                    ActiveYear,
                    ActiveSemester,
                    semesterSettings,
                    _employeeId);

                var (from, to) = GetSemesterRange(ActiveYear, ActiveSemester);
                await SettingsService.DeleteComputedDaySettingsInRangeAsync(from, to, _employeeId);

                var generator = new AutomaticEventsGeneratorService(
                    _eventService,
                    prompt => ShowCollisionMessage.Handle(prompt).FirstAsync().ToTask(),
                    _employeeId);

                await generator.RegenerateRangeEventsAsync(from, to);

                _originalSettings = CurrentSettings.Clone();

                MessageBus.Current.SendMessage(new GlobalSettingsChangedMessage(ActiveSemester));
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static (DateTime from, DateTime to) GetSemesterRange(int year, SemesterType sem)
        {
            if (sem == SemesterType.Winter)
            {
                var from = new DateTime(year, 9, 1);
                var to = new DateTime(year + 1, 2, 10);
                return (from, to);
            }

            var summerFrom = new DateTime(year, 2, 10);
            var summerTo = new DateTime(year, 8, 31);
            return (summerFrom, summerTo);
        }

        private static string Safe(string? value) => value?.Trim() ?? string.Empty;

        private static TimeSpan ParseTime(string value)
            => TimeSpan.Parse(value);

        public void Dispose()
        {
            _disposables.Dispose();
        }

        public class GlobalSettingsFormModel : ReactiveObject
        {
            private string _globalStartTime = "08:00";
            public string GlobalStartTime
            {
                get => _globalStartTime;
                set => this.RaiseAndSetIfChanged(ref _globalStartTime, value);
            }

            private string _globalEndTime = "16:30";
            public string GlobalEndTime
            {
                get => _globalEndTime;
                set => this.RaiseAndSetIfChanged(ref _globalEndTime, value);
            }

            private string _minBreakDuration = "00:15";
            public string MinBreakDuration
            {
                get => _minBreakDuration;
                set => this.RaiseAndSetIfChanged(ref _minBreakDuration, value);
            }

            private string _maxBreakDuration = "01:00";
            public string MaxBreakDuration
            {
                get => _maxBreakDuration;
                set => this.RaiseAndSetIfChanged(ref _maxBreakDuration, value);
            }

            private string _autoEventNamePreLunch = "Dopolední pracovní doba";
            public string AutoEventNamePreLunch
            {
                get => _autoEventNamePreLunch;
                set => this.RaiseAndSetIfChanged(ref _autoEventNamePreLunch, value);
            }

            private string _autoEventNameLunch = "Oběd";
            public string AutoEventNameLunch
            {
                get => _autoEventNameLunch;
                set => this.RaiseAndSetIfChanged(ref _autoEventNameLunch, value);
            }

            private string _autoEventNamePostLunch = "Odpolední pracovní doba";
            public string AutoEventNamePostLunch
            {
                get => _autoEventNamePostLunch;
                set => this.RaiseAndSetIfChanged(ref _autoEventNamePostLunch, value);
            }

            private string _mondayArrival = "08:00";
            public string MondayArrival
            {
                get => _mondayArrival;
                set => this.RaiseAndSetIfChanged(ref _mondayArrival, value);
            }

            private string _mondayDeparture = "16:30";
            public string MondayDeparture
            {
                get => _mondayDeparture;
                set => this.RaiseAndSetIfChanged(ref _mondayDeparture, value);
            }

            private string _mondayLunchStart = "12:00";
            public string MondayLunchStart
            {
                get => _mondayLunchStart;
                set => this.RaiseAndSetIfChanged(ref _mondayLunchStart, value);
            }

            private string _mondayLunchEnd = "12:30";
            public string MondayLunchEnd
            {
                get => _mondayLunchEnd;
                set => this.RaiseAndSetIfChanged(ref _mondayLunchEnd, value);
            }

            private string _tuesdayArrival = "08:00";
            public string TuesdayArrival
            {
                get => _tuesdayArrival;
                set => this.RaiseAndSetIfChanged(ref _tuesdayArrival, value);
            }

            private string _tuesdayDeparture = "16:30";
            public string TuesdayDeparture
            {
                get => _tuesdayDeparture;
                set => this.RaiseAndSetIfChanged(ref _tuesdayDeparture, value);
            }

            private string _tuesdayLunchStart = "12:00";
            public string TuesdayLunchStart
            {
                get => _tuesdayLunchStart;
                set => this.RaiseAndSetIfChanged(ref _tuesdayLunchStart, value);
            }

            private string _tuesdayLunchEnd = "12:30";
            public string TuesdayLunchEnd
            {
                get => _tuesdayLunchEnd;
                set => this.RaiseAndSetIfChanged(ref _tuesdayLunchEnd, value);
            }

            private string _wednesdayArrival = "08:00";
            public string WednesdayArrival
            {
                get => _wednesdayArrival;
                set => this.RaiseAndSetIfChanged(ref _wednesdayArrival, value);
            }

            private string _wednesdayDeparture = "16:30";
            public string WednesdayDeparture
            {
                get => _wednesdayDeparture;
                set => this.RaiseAndSetIfChanged(ref _wednesdayDeparture, value);
            }

            private string _wednesdayLunchStart = "12:00";
            public string WednesdayLunchStart
            {
                get => _wednesdayLunchStart;
                set => this.RaiseAndSetIfChanged(ref _wednesdayLunchStart, value);
            }

            private string _wednesdayLunchEnd = "12:30";
            public string WednesdayLunchEnd
            {
                get => _wednesdayLunchEnd;
                set => this.RaiseAndSetIfChanged(ref _wednesdayLunchEnd, value);
            }

            private string _thursdayArrival = "08:00";
            public string ThursdayArrival
            {
                get => _thursdayArrival;
                set => this.RaiseAndSetIfChanged(ref _thursdayArrival, value);
            }

            private string _thursdayDeparture = "16:30";
            public string ThursdayDeparture
            {
                get => _thursdayDeparture;
                set => this.RaiseAndSetIfChanged(ref _thursdayDeparture, value);
            }

            private string _thursdayLunchStart = "12:00";
            public string ThursdayLunchStart
            {
                get => _thursdayLunchStart;
                set => this.RaiseAndSetIfChanged(ref _thursdayLunchStart, value);
            }

            private string _thursdayLunchEnd = "12:30";
            public string ThursdayLunchEnd
            {
                get => _thursdayLunchEnd;
                set => this.RaiseAndSetIfChanged(ref _thursdayLunchEnd, value);
            }

            private string _fridayArrival = "08:00";
            public string FridayArrival
            {
                get => _fridayArrival;
                set => this.RaiseAndSetIfChanged(ref _fridayArrival, value);
            }

            private string _fridayDeparture = "16:30";
            public string FridayDeparture
            {
                get => _fridayDeparture;
                set => this.RaiseAndSetIfChanged(ref _fridayDeparture, value);
            }

            private string _fridayLunchStart = "12:00";
            public string FridayLunchStart
            {
                get => _fridayLunchStart;
                set => this.RaiseAndSetIfChanged(ref _fridayLunchStart, value);
            }

            private string _fridayLunchEnd = "12:30";
            public string FridayLunchEnd
            {
                get => _fridayLunchEnd;
                set => this.RaiseAndSetIfChanged(ref _fridayLunchEnd, value);
            }

            private string _employeeName = string.Empty;
            public string EmployeeName
            {
                get => _employeeName;
                set => this.RaiseAndSetIfChanged(ref _employeeName, value);
            }

            private string _department = string.Empty;
            public string Department
            {
                get => _department;
                set => this.RaiseAndSetIfChanged(ref _department, value);
            }

            public GlobalSettingsFormModel Clone()
            {
                return new GlobalSettingsFormModel
                {
                    GlobalStartTime = GlobalStartTime,
                    GlobalEndTime = GlobalEndTime,
                    MinBreakDuration = MinBreakDuration,
                    MaxBreakDuration = MaxBreakDuration,
                    AutoEventNamePreLunch = AutoEventNamePreLunch,
                    AutoEventNameLunch = AutoEventNameLunch,
                    AutoEventNamePostLunch = AutoEventNamePostLunch,

                    MondayArrival = MondayArrival,
                    MondayDeparture = MondayDeparture,
                    MondayLunchStart = MondayLunchStart,
                    MondayLunchEnd = MondayLunchEnd,

                    TuesdayArrival = TuesdayArrival,
                    TuesdayDeparture = TuesdayDeparture,
                    TuesdayLunchStart = TuesdayLunchStart,
                    TuesdayLunchEnd = TuesdayLunchEnd,

                    WednesdayArrival = WednesdayArrival,
                    WednesdayDeparture = WednesdayDeparture,
                    WednesdayLunchStart = WednesdayLunchStart,
                    WednesdayLunchEnd = WednesdayLunchEnd,

                    ThursdayArrival = ThursdayArrival,
                    ThursdayDeparture = ThursdayDeparture,
                    ThursdayLunchStart = ThursdayLunchStart,
                    ThursdayLunchEnd = ThursdayLunchEnd,

                    FridayArrival = FridayArrival,
                    FridayDeparture = FridayDeparture,
                    FridayLunchStart = FridayLunchStart,
                    FridayLunchEnd = FridayLunchEnd,
                    EmployeeName = EmployeeName,
                    Department = Department
                };
            }
        }
    }
}