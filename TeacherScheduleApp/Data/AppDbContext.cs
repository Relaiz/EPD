using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Event> Events { get; set; }
        public DbSet<UserSettings> UserSettings { get; set; }
        public DbSet<GlobalSettings> GlobalSettings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string baseDir;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            else
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            var appDir = Path.Combine(baseDir, "TeacherScheduleApp");
            Directory.CreateDirectory(appDir);

            var dbPath = Path.Combine(appDir, "teacherapp.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

       
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var dateTimeConverter = new ValueConverter<DateTime, string>(
                       v => v.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                       v => DateTime.ParseExact(v, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                   );
            modelBuilder.Entity<Event>()
                    .Property(e => e.StartTime)
                    .HasConversion(dateTimeConverter);

            modelBuilder.Entity<Event>()
                .Property(e => e.EndTime)
                .HasConversion(dateTimeConverter);
            base.OnModelCreating(modelBuilder);         
        }
    }
}
