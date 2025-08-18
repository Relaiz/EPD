using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;
using static TeacherScheduleApp.Models.GlobalSettings;

namespace TeacherScheduleApp.ViewModels
{
    public class GlobalUserSettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly CompositeDisposable _disposables = new();
        private readonly EventService _eventService;
        private GlobalSettings _originalSettings;
        private readonly Action _closeRequested;

        public string HeaderDisplay => $"Rok {ActiveYear} · {CurrentSemesterDisplay}";
        public string DaysHeaderDisplay => $"Pracovní doba · {CurrentSemesterDisplay}";

        public string WorkTimeHeaderDisplay => $"Globální pracovní doba · {CurrentSemesterDisplay}";

        public bool ActiveSemesterIsWinter
        {
            get => ActiveSemester == SemesterType.Winter;
            set { if (value) ActiveSemester = SemesterType.Winter; }
        }
        public bool ActiveSemesterIsSummer
        {
            get => ActiveSemester == SemesterType.Summer;
            set { if (value) ActiveSemester = SemesterType.Summer; }
        }

        public int ActiveYear { get => _activeYear; set { this.RaiseAndSetIfChanged(ref _activeYear, value); LoadFor(ActiveYear, ActiveSemester); } }
        private int _activeYear;

        public ObservableCollection<int> AvailableYears { get; } = new();
        public Interaction<string, bool> ShowCollisionMessage { get; } = new Interaction<string, bool>();
        private SemesterType _activeSemester;
        public SemesterType ActiveSemester
        {
            get => _activeSemester;
            set
            {
                if (_activeSemester == value) return;
                this.RaiseAndSetIfChanged(ref _activeSemester, value);
                this.RaisePropertyChanged(nameof(CurrentSemesterDisplay));
                this.RaisePropertyChanged(nameof(DaysHeaderDisplay));
                this.RaisePropertyChanged(nameof(WorkTimeHeaderDisplay));
                this.RaisePropertyChanged(nameof(HeaderDisplay));
                this.RaisePropertyChanged(nameof(ActiveSemesterIsWinter));
                this.RaisePropertyChanged(nameof(ActiveSemesterIsSummer));
            }
        }

        public string CurrentSemesterDisplay
            => ActiveSemester == SemesterType.Winter
               ? "Zimní semestr"
               : "Letní semestr";

        private GlobalSettings _currentSettings;
        public GlobalSettings CurrentSettings
        {
            get => _currentSettings;
            set => this.RaiseAndSetIfChanged(ref _currentSettings, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        public GlobalUserSettingsViewModel(int initialYear, SemesterType initialSemester, Action closeRequested)
        {
            _closeRequested = closeRequested ?? (() => { });
            _eventService = new EventService();
            ActiveYear = initialYear;
            ActiveSemester = initialSemester;
            var years = GlobalSettingsService.GetYearsWithData();
            if (!years.Contains(initialYear)) years.Add(initialYear);
            foreach (var y in years.Distinct().OrderBy(y => y))
                AvailableYears.Add(y);

            var loaded = GlobalSettingsService.LoadGlobalSettings(ActiveYear, ActiveSemester);
            CurrentSettings = loaded ?? GlobalSettingsService.GetDefaultSettings(ActiveYear, ActiveSemester);
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
            .Subscribe(_ =>
            {
                LoadFor(ActiveYear, ActiveSemester);
                this.RaisePropertyChanged(nameof(HeaderDisplay));
                this.RaisePropertyChanged(nameof(DaysHeaderDisplay));
                this.RaisePropertyChanged(nameof(WorkTimeHeaderDisplay));
                this.RaisePropertyChanged(nameof(ActiveSemesterIsWinter));
                this.RaisePropertyChanged(nameof(ActiveSemesterIsSummer));
            })
            .DisposeWith(_disposables);
            SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync).DisposeWith(_disposables);
            MessageBus.Current
                .Listen<GlobalSettingsChangedMessage>()
                .Where(msg => msg.Semester == ActiveSemester)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => LoadFor(ActiveYear,ActiveSemester))
                .DisposeWith(_disposables);
            this.WhenAnyValue(x => x.ActiveYear, x => x.ActiveSemester)
            .Subscribe(tuple => LoadFor(tuple.Item1, tuple.Item2));
        }

        private void LoadFor(int year, SemesterType sem)
        {
            CurrentSettings = GlobalSettingsService.LoadGlobalSettings(year, sem)
                              ?? GlobalSettingsService.GetDefaultSettings(year, sem);
        }
        public ReactiveCommand<Unit, Unit> GoBackCommand { get; }

        private bool ValidateSettings()
        {
            bool TryParse(string s, out TimeSpan ts)
            {
                return TimeSpan.TryParse(s, out ts);
            }

            if (!TryParse(CurrentSettings.GlobalStartTime, out var g0) ||
                !TryParse(CurrentSettings.GlobalEndTime, out var g1) ||
                 g0 >= g1)
                return false;

            foreach (var day in new[]
            {
              ("Monday",   CurrentSettings.MondayArrival,   CurrentSettings.MondayLunchStart,   CurrentSettings.MondayLunchEnd,   CurrentSettings.MondayDeparture),
              ("Tuesday",  CurrentSettings.TuesdayArrival,  CurrentSettings.TuesdayLunchStart,  CurrentSettings.TuesdayLunchEnd,  CurrentSettings.TuesdayDeparture),
              ("Wednesday",CurrentSettings.WednesdayArrival,CurrentSettings.WednesdayLunchStart,CurrentSettings.WednesdayLunchEnd,CurrentSettings.WednesdayDeparture),
              ("Thursday", CurrentSettings.ThursdayArrival, CurrentSettings.ThursdayLunchStart, CurrentSettings.ThursdayLunchEnd, CurrentSettings.ThursdayDeparture),
              ("Friday",   CurrentSettings.FridayArrival,   CurrentSettings.FridayLunchStart,   CurrentSettings.FridayLunchEnd,   CurrentSettings.FridayDeparture),
            })
            {
               
               if (string.IsNullOrWhiteSpace(day.Item2) &&
                string.IsNullOrWhiteSpace(day.Item3) &&
                string.IsNullOrWhiteSpace(day.Item4) &&
                string.IsNullOrWhiteSpace(day.Item5))
                    continue;             
                if (!TryParse(day.Item2, out var a) ||
                    !TryParse(day.Item3, out var l0) ||
                    !TryParse(day.Item4, out var l1) ||
                    !TryParse(day.Item5, out var d))
                    return false;

            if (a < g0 || a >= l0 || l0 >= l1 || l1 >= d || d > g1)
                    return false;
            }

           
            if (!TryParse(CurrentSettings.MinBreakDuration, out var minB) ||
                !TryParse(CurrentSettings.MaxBreakDuration, out var maxB) ||
                minB > maxB)
                return false;

            return true;
        }

        public bool IsBusy { get; private set; }

        public void Dispose()
        {
            _disposables.Dispose();
        }
        private async Task SaveAsync()
        {
            if (!ValidateSettings())
            {
                await MessageBoxManager.GetMessageBoxStandard(
                    "Chyba",
                    "Nastavení nejsou validní. Zkontrolujte časové hodnoty.",
                    ButtonEnum.Ok,
                    Icon.Error).ShowWindowDialogAsync(Helpers.Helper.GetMainWindow());
                return;
            }

            IsBusy = true;
            try
            {
                await GlobalSettingsService.SaveGlobalSettingsAsync(ActiveYear, ActiveSemester, CurrentSettings);

                var (from, to) = GetSemesterRange(ActiveYear, ActiveSemester);
                var generator = new AutomaticEventsGeneratorService(_eventService,
                    prompt => ShowCollisionMessage.Handle(prompt).FirstAsync().ToTask());

                await generator.RegenerateRangeEventsAsync(from, to);

                MessageBus.Current.SendMessage(new GlobalSettingsChangedMessage(ActiveSemester));
            }
            finally { IsBusy = false; }
        }

        private static (DateTime from, DateTime to) GetSemesterRange(int year, SemesterType sem)
        {
            if (sem == SemesterType.Winter)
            {
                var from = new DateTime(year, 9, 1);
                var to = new DateTime(year + 1, 2, 10);
                return (from, to);
            }
            else
            {
                var from = new DateTime(year, 2, 10);
                var to = new DateTime(year, 8, 31);
                return (from, to);
            }
        }
    }
}
