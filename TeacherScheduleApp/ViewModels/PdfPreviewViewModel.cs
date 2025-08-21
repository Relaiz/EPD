using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using ReactiveUI;
using TeacherScheduleApp.Services;

namespace TeacherScheduleApp.ViewModels
{
    public class PdfPreviewViewModel : ReactiveObject
    {
        private readonly IPdfPreviewService _pdfService;
        private readonly EventService _eventService;

        public ObservableCollection<string> AvailableMonths { get; }

        private string _selectedMonth;
        public string SelectedMonth
        {
            get => _selectedMonth;
            set => this.RaiseAndSetIfChanged(ref _selectedMonth, value);
        }
        public ObservableCollection<Bitmap> Pages { get; } = new();
        private int _pageIndex;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _pageIndex, value);
                this.RaisePropertyChanged(nameof(CurrentPage));
            }
        }
        public Bitmap CurrentPage => Pages.ElementAtOrDefault(PageIndex);

        public ReactiveCommand<Unit, Unit> SavePdf { get; }

        public PdfPreviewViewModel(IPdfPreviewService pdfService,
                                   EventService eventService,
                                   DateTime initialMonth)
        {
            _pdfService = pdfService;
            _eventService = eventService;
            var months = _eventService.LoadEvents()
                             .Select(e => new DateTime(e.StartTime.Year, e.StartTime.Month, 1))
                             .Distinct()
                             .OrderByDescending(d => d)
                             .Select(d => d.ToString("MM-yyyy"));

            AvailableMonths = new ObservableCollection<string>(months);

            this.WhenAnyValue(vm => vm.SelectedMonth)
            .Where(s => !string.IsNullOrEmpty(s))
            .Subscribe(async _ => await LoadPreviewAsync());
            SelectedMonth = initialMonth.ToString("MM-yyyy");

            SavePdf = ReactiveCommand.CreateFromTask(async () =>
            {
                var (year, month) = Parse(SelectedMonth);
                var bytes = _pdfService.GenerateMonthReport(
                    year, month,
                    _eventService.GetEventsForMonth(new DateTime(year, month, 1))
                );

                var dlg = new SaveFileDialog
                {
                    Title = "Uložit PDF",
                    Filters = { new FileDialogFilter { Name = $"EPD_{month:D2}-{year:D4}", Extensions = { "pdf" } } }
                };

                Window? parent = null;
                if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    parent = desktop.MainWindow;

                var path = await dlg.ShowAsync(parent);
                if (!string.IsNullOrEmpty(path))
                    await File.WriteAllBytesAsync(path, bytes);
            });
        }

        private async Task LoadPreviewAsync()
        {
            Pages.Clear();
            PageIndex = 0;

            var (year, month) = Parse(SelectedMonth);
            await _eventService.BalanceEventsForMonthAsync(year, month, AskUserAsync);
            var events = _eventService.GetEventsForMonth(new DateTime(year, month, 1));        
            var pdfBytes = _pdfService.GenerateMonthReport(year, month, events);
            var images = _pdfService.RenderPdfPages(pdfBytes);
            foreach (var img in images)
                Pages.Add(img);

            PageIndex = 0;
        }
        private static async Task<bool> AskUserAsync(string message)
        {
            var owner =
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                    .Windows?.FirstOrDefault(w => w.IsActive)
                ?? Helpers.Helper.GetMainWindow();

            var box = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard(
                new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                    Icon = MsBox.Avalonia.Enums.Icon.Question,
                    ContentHeader = "Potvrzení přesunu hodin",
                    ContentMessage = message,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            var res = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => box.ShowWindowDialogAsync(owner)
            ); 

            return res == MsBox.Avalonia.Enums.ButtonResult.Yes;
        }

        private static (int year, int month) Parse(string s)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                var parts = s.Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var m) &&
                    int.TryParse(parts[1], out var y))
                {
                    if (m >= 1 && m <= 12)
                        return (y, m);
                }
            }

            var today = DateTime.Today;
            return (today.Year, today.Month);
        }
    }
}
