using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TeacherScheduleApp.Views;
using TeacherScheduleApp.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using Avalonia.ReactiveUI;
using TeacherScheduleApp.Data;
using QuestPDF.Infrastructure;
using QuestPDF;
using Splat;
using TeacherScheduleApp.Services;
using System.Threading.Tasks;
using System;
using TeacherScheduleApp.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Globalization;


namespace TeacherScheduleApp;


public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Settings.License = LicenseType.Community;
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("cs-CZ");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("cs-CZ");
        InitializeDatabase();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var locator = Splat.Locator.CurrentMutable;
        locator.RegisterLazySingleton<IPdfPreviewService>(() => new PdfService());
        locator.RegisterLazySingleton(() => new EventService());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };        
        }

        base.OnFrameworkInitializationCompleted();
    }
    private void InitializeDatabase()
    {
        using var db = new AppDbContext();
        db.Database.EnsureCreated();
    }
}