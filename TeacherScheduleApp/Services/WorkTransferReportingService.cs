
    using Microsoft.EntityFrameworkCore;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TeacherScheduleApp.Data;
    using TeacherScheduleApp.Models;
    using static TeacherScheduleApp.Services.EventService;

    namespace TeacherScheduleApp.Services
    {
        public static class WorkTransferReportingService
        {
            public sealed class TransferEntry
            {
                public int Id { get; set; }
                public int EmployeeId { get; set; }
                public DateTime FromDay { get; set; }
                public DateTime ToDay { get; set; }
                public int Minutes { get; set; }
                public TransferEdge Edge { get; set; }
            }

            public static void AddTransfer(
                DateTime fromDay,
                DateTime toDay,
                int minutes,
                TransferEdge edge,
                int employeeId = EventService.DefaultEmployeeId)
            {
                minutes = minutes - minutes % 5;
                if (minutes < 5)
                    return;

                fromDay = fromDay.Date;
                toDay = toDay.Date;

                using var db = new AppDbContext();
                var now = DateTime.UtcNow;

                var row = db.BalanceTransfers.SingleOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.FromDay == fromDay &&
                    x.ToDay == toDay &&
                    x.Edge == (int)edge);

                if (row == null)
                {
                    db.BalanceTransfers.Add(new BalanceTransfer
                    {
                        EmployeeId = employeeId,
                        FromDay = fromDay,
                        ToDay = toDay,
                        Edge = (int)edge,
                        Minutes = minutes,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }
                else
                {
                    row.Minutes += minutes;
                    row.UpdatedAtUtc = now;
                }

                db.SaveChanges();
            }

            public static void RemoveEntry(TransferEntry entry)
            {
                using var db = new AppDbContext();

                var row = db.BalanceTransfers.SingleOrDefault(x => x.Id == entry.Id);
                if (row == null)
                    return;

                db.BalanceTransfers.Remove(row);
                db.SaveChanges();
            }

            public static List<TransferEntry> GetTransfersFrom(
                DateTime fromDay,
                int employeeId = EventService.DefaultEmployeeId)
            {
                fromDay = fromDay.Date;

                using var db = new AppDbContext();
                return db.BalanceTransfers
                    .AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId && x.FromDay == fromDay)
                    .OrderBy(x => x.ToDay)
                    .ThenBy(x => x.Edge)
                    .Select(x => new TransferEntry
                    {
                        Id = x.Id,
                        EmployeeId = x.EmployeeId,
                        FromDay = x.FromDay,
                        ToDay = x.ToDay,
                        Minutes = x.Minutes,
                        Edge = (TransferEdge)x.Edge
                    })
                    .ToList();
            }

            public static List<TransferEntry> GetTransfersTo(
                DateTime toDay,
                int employeeId = EventService.DefaultEmployeeId)
            {
                toDay = toDay.Date;

                using var db = new AppDbContext();
                return db.BalanceTransfers
                    .AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId && x.ToDay == toDay)
                    .OrderBy(x => x.FromDay)
                    .ThenBy(x => x.Edge)
                    .Select(x => new TransferEntry
                    {
                        Id = x.Id,
                        EmployeeId = x.EmployeeId,
                        FromDay = x.FromDay,
                        ToDay = x.ToDay,
                        Minutes = x.Minutes,
                        Edge = (TransferEdge)x.Edge
                    })
                    .ToList();
            }

            public static double GetMovedOut(
                DateTime day,
                int employeeId = EventService.DefaultEmployeeId)
            {
                day = day.Date;

                using var db = new AppDbContext();
                var minutes = db.BalanceTransfers
                    .Where(x => x.EmployeeId == employeeId && x.FromDay == day)
                    .Select(x => (int?)x.Minutes)
                    .Sum() ?? 0;

                return minutes / 60.0;
            }

            public static double GetMovedIn(
                DateTime day,
                int employeeId = EventService.DefaultEmployeeId)
            {
                day = day.Date;

                using var db = new AppDbContext();
                var minutes = db.BalanceTransfers
                    .Where(x => x.EmployeeId == employeeId && x.ToDay == day)
                    .Select(x => (int?)x.Minutes)
                    .Sum() ?? 0;

                return minutes / 60.0;
            }

            public static void ResetWeek(
                IEnumerable<DateTime> scopeDays,
                int employeeId = EventService.DefaultEmployeeId)
            {
                var days = scopeDays
                    .Select(x => x.Date)
                    .Distinct()
                    .ToList();

                if (days.Count == 0)
                    return;

                using var db = new AppDbContext();

                var rows = db.BalanceTransfers
                    .Where(x => x.EmployeeId == employeeId &&
                                (days.Contains(x.FromDay) || days.Contains(x.ToDay)))
                    .ToList();

                if (rows.Count == 0)
                    return;

                db.BalanceTransfers.RemoveRange(rows);
                db.SaveChanges();
            }
        }
    }

    public enum TransferEdge
    {
        Start,
        End
    }

    public sealed record WorkTransferEntry(
        DateTime FromDay,
        DateTime ToDay,
        int Minutes,
        TransferEdge Edge
    );
