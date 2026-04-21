using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Employee> Employees { get; set; }
        public DbSet<SemesterSettings> SemesterSettings { get; set; }
        public DbSet<WeekdaySettings> WeekdaySettings { get; set; }
        public DbSet<DaySettings> DaySettings { get; set; }
        public DbSet<ImportBatch> ImportBatches { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<BalanceSelfTrim> BalanceSelfTrims { get; set; }
        public DbSet<BalanceTransfer> BalanceTransfers { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured)
                return;

            var appDir = AppContext.BaseDirectory;
            var dbPath = Path.Combine(appDir, "teacherapp.db");

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var dateTimeConverter = new ValueConverter<DateTime, string>(
                v => v.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                v => DateTime.ParseExact(v, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            );

            var nullableDateTimeConverter = new ValueConverter<DateTime?, string?>(
                v => v.HasValue
                    ? v.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : null,
                v => string.IsNullOrWhiteSpace(v)
                    ? null
                    : DateTime.ParseExact(v, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            );

            var timeSpanConverter = new ValueConverter<TimeSpan, string>(
                v => v.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                v => TimeSpan.ParseExact(v, @"hh\:mm", CultureInfo.InvariantCulture)
            );

            modelBuilder.Entity<Employee>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.FullName).IsRequired();
                b.Property(x => x.Department).IsRequired();
            });

            modelBuilder.Entity<SemesterSettings>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.GlobalStartTime).IsRequired();
                b.Property(x => x.GlobalEndTime).IsRequired();
                b.Property(x => x.MinBreakDuration).IsRequired();
                b.Property(x => x.MaxBreakDuration).IsRequired();
                b.Property(x => x.AutoEventNamePreLunch).IsRequired();
                b.Property(x => x.AutoEventNameLunch).IsRequired();
                b.Property(x => x.AutoEventNamePostLunch).IsRequired();

                b.HasIndex(x => new { x.EmployeeId, x.Year, x.Semester }).IsUnique();

                b.HasOne(x => x.Employee)
                    .WithMany(x => x.SemesterSettings)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<WeekdaySettings>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.ArrivalTime)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.Property(x => x.DepartureTime)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.Property(x => x.LunchStart)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.Property(x => x.LunchEnd)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.HasIndex(x => new { x.SemesterSettingsId, x.DayOfWeek }).IsUnique();

                b.HasOne(x => x.SemesterSettings)
                    .WithMany(x => x.WeekdaySettings)
                    .HasForeignKey(x => x.SemesterSettingsId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DaySettings>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.Date)
                    .HasConversion(dateTimeConverter)
                    .IsRequired();

                b.Property(x => x.ArrivalTime)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.Property(x => x.DepartureTime)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.Property(x => x.LunchStart)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.Property(x => x.LunchEnd)
                    .HasConversion(timeSpanConverter)
                    .IsRequired();

                b.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
                b.HasIndex(x => new { x.EmployeeId, x.IsManualOverride, x.Date });

                b.HasOne(x => x.Employee)
                    .WithMany(x => x.DaySettings)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ImportBatch>(b =>
            {
                b.HasKey(x => x.Id);

                b.Property(x => x.Id).IsRequired();
                b.Property(x => x.ImportedAt)
                    .HasConversion(dateTimeConverter)
                    .IsRequired();
            });

            modelBuilder.Entity<Event>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.Title).IsRequired();

                b.Property(x => x.StartTime)
                    .HasConversion(dateTimeConverter)
                    .IsRequired();

                b.Property(x => x.EndTime)
                    .HasConversion(dateTimeConverter)
                    .IsRequired();

                b.Property(x => x.AutoGeneratedForDate)
                    .HasConversion(nullableDateTimeConverter);

                b.HasIndex(x => new { x.EmployeeId, x.IsDeleted, x.StartTime });
                b.HasIndex(x => new { x.EmployeeId, x.IsDeleted, x.EndTime });
                b.HasIndex(x => new { x.EmployeeId, x.IsAutoGenerated, x.AutoGeneratedForDate });
                b.HasIndex(x => new { x.EmployeeId, x.IsDeleted, x.ImportBatchId });
                b.HasIndex(x => new { x.EmployeeId, x.ParentEventId });
                b.HasIndex(x => x.ImportBatchId);

                b.HasOne(x => x.Employee)
                    .WithMany(x => x.Events)
                    .HasForeignKey(x => x.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.ParentEvent)
                    .WithMany(x => x.Children)
                    .HasForeignKey(x => x.ParentEventId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.ImportBatch)
                    .WithMany(x => x.Events)
                    .HasForeignKey(x => x.ImportBatchId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<BalanceSelfTrim>(b =>
            {
                b.HasKey(x => x.Id);

                b.HasIndex(x => new { x.EmployeeId, x.Day, x.Edge }).IsUnique();

                b.Property(x => x.Minutes).IsRequired();
                b.Property(x => x.Day).IsRequired();
                b.Property(x => x.Edge).IsRequired();
                b.Property(x => x.CreatedAtUtc).IsRequired();
                b.Property(x => x.UpdatedAtUtc).IsRequired();
            });

            modelBuilder.Entity<BalanceTransfer>(b =>
            {
                b.HasKey(x => x.Id);

                b.HasIndex(x => new { x.EmployeeId, x.FromDay, x.ToDay, x.Edge }).IsUnique();
                b.HasIndex(x => new { x.EmployeeId, x.FromDay });
                b.HasIndex(x => new { x.EmployeeId, x.ToDay });

                b.Property(x => x.Minutes).IsRequired();
                b.Property(x => x.FromDay).IsRequired();
                b.Property(x => x.ToDay).IsRequired();
                b.Property(x => x.Edge).IsRequired();
                b.Property(x => x.CreatedAtUtc).IsRequired();
                b.Property(x => x.UpdatedAtUtc).IsRequired();
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}