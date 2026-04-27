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
using Avalonia.Platform.Storage;
using ReactiveUI;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;

namespace TeacherScheduleApp.ViewModels
{
    public class PdfPreviewViewModel : ReactiveObject
    {
        private readonly IPdfPreviewService _pdfService;
        private readonly EventService _eventService;
        private readonly int _employeeId;

        public ObservableCollection<string> AvailableMonths { get; }

        private string _selectedMonth = string.Empty;
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

        public async Task LoadInitialAsync()
        {
            await LoadPreviewAsync();
        }

        public Bitmap? CurrentPage => Pages.ElementAtOrDefault(PageIndex);

        public ReactiveCommand<Unit, Unit> SavePdf { get; }

        public PdfPreviewViewModel(
            IPdfPreviewService pdfService,
            EventService eventService,
            DateTime initialMonth,
            int employeeId = EventService.DefaultEmployeeId)
        {
            _pdfService = pdfService;
            _eventService = eventService;
            _employeeId = employeeId;

            var months = _eventService.LoadEvents(_employeeId)
                .Select(e => new DateTime(e.StartTime.Year, e.StartTime.Month, 1))
                .Distinct()
                .OrderByDescending(d => d)
                .Select(d => d.ToString("MM-yyyy"));

            AvailableMonths = new ObservableCollection<string>(months);

            this.WhenAnyValue(vm => vm.SelectedMonth)
               .Skip(1)
               .Where(s => !string.IsNullOrWhiteSpace(s))
               .SelectMany(_ => Observable.FromAsync(LoadPreviewAsync))
               .Subscribe();

            SelectedMonth = initialMonth.ToString("MM-yyyy");

            SavePdf = ReactiveCommand.CreateFromTask(async () =>
            {
                var (year, month) = Parse(SelectedMonth);

                var bytes = _pdfService.GenerateMonthReport(
                    year,
                    month,
                    _eventService.GetEventsForMonth(_employeeId, new DateTime(year, month, 1))
                );

                Window? parent = null;
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    parent = desktop.MainWindow;

                if (parent is null)
                    return;

                var file = await parent.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Uložit PDF",
                    SuggestedFileName = $"EPD_{month:D2}-{year:D4}.pdf",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("PDF")
                        {
                            Patterns = new[] { "*.pdf" },
                            MimeTypes = new[] { "application/pdf" }
                        }
                    }
                });

                if (file is not null)
                {
                    await using var stream = await file.OpenWriteAsync();
                    await stream.WriteAsync(bytes);
                }
            });
        }

        private async Task LoadPreviewAsync()
        {
            var selected = SelectedMonth;
            var (year, month) = Parse(selected);

            var images = await Task.Run(() =>
            {
                var monthEvents = _eventService.GetEventsForMonth(_employeeId, new DateTime(year, month, 1));
                var pdfBytes = _pdfService.GenerateMonthReport(year, month, monthEvents, _employeeId);
                return _pdfService.RenderPdfPages(pdfBytes).ToList();
            });

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Pages.Clear();

                foreach (var img in images)
                    Pages.Add(img);

                PageIndex = 0;
            });
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
                    int.TryParse(parts[1], out var y) &&
                    m >= 1 && m <= 12)
                {
                    return (y, m);
                }
            }

            var today = DateTime.Today;
            return (today.Year, today.Month);
        }
    }
}
