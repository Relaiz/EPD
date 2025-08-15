using Avalonia;
using Microsoft.EntityFrameworkCore;
using System;
using TeacherScheduleApp.Data;

namespace TeacherScheduleApp;

class Program
{

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            using var db = new AppDbContext();
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Migration failed: {ex}");
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
