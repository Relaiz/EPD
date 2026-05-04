using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;
using Ghostscript.NET;
using Ghostscript.NET.Rasterizer;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Services
{
    public interface IPdfPreviewService
    {
        byte[] GenerateMonthReport(int year, int month, IEnumerable<Event> events, int employeeId = EventService.DefaultEmployeeId);
        IReadOnlyList<Bitmap> RenderPdfPages(byte[] pdfBytes, int dpi = 300);
    }

    public class PdfService : IPdfPreviewService
    {
        private const int QUANTUM_MIN = 5;

        private static int RoundDownToQuantum(int minutes)
            => minutes - minutes % QUANTUM_MIN;

        private static int ToWholeMinutes(double hours)
            => (int)Math.Round(hours * 60.0);

        private static string FormatTotalMinutes(int totalMinutes)
        {
            var sign = totalMinutes < 0 ? "-" : string.Empty;
            var absMinutes = Math.Abs(totalMinutes);
            return $"{sign}{absMinutes / 60:00}:{absMinutes % 60:00}:00";
        }

        private static List<(DateTime s, DateTime e)> TakeUpToMinutes(IEnumerable<(DateTime s, DateTime e)> intervals, int maxMinutes)
        {
            var left = Math.Max(0, maxMinutes);
            var result = new List<(DateTime s, DateTime e)>();

            foreach (var iv in MergeIntervals(intervals))
            {
                if (left <= 0)
                    break;

                var minutes = (int)(iv.e - iv.s).TotalMinutes;
                var take = Math.Min(minutes, left);

                if (take > 0)
                {
                    result.Add((iv.s, iv.s.AddMinutes(take)));
                    left -= take;
                }
            }

            return MergeIntervals(result);
        }

        private static readonly HashSet<EventType> SpecialTypes = new()
        {
            EventType.DayOff,
            EventType.Illness,
            EventType.Vacation,
            EventType.Ocr,
            EventType.Doctor,
            EventType.BusinessTrip,
            EventType.Holiday
        };

        private static readonly HashSet<EventType> PaidSpecialTypes = new()
        {
            EventType.DayOff,
            EventType.Illness,
            EventType.Vacation,
            EventType.Ocr,
            EventType.Doctor,
            EventType.Holiday
        };

        private static string CodeFor(EventType t) => t switch
        {
            EventType.Vacation => "D",
            EventType.Illness => "N",
            EventType.Ocr => "OČR",
            EventType.Doctor => "L",
            EventType.BusinessTrip => "PC",
            EventType.Holiday => "S",
            EventType.DayOff => "S",
            _ => ""
        };

        private static bool IsPaidSpecialForPdf(Event e)
        {
            if (e.IsDeleted || e.EventType == EventType.Lunch)
                return false;

            return PaidSpecialTypes.Contains(e.EventType);
        }

        private static bool IsCreditedForPdf(Event e)
        {
            if (e.IsDeleted || e.EventType == EventType.Lunch)
                return false;

            return IsWorkLike(e) || IsPaidSpecialForPdf(e);
        }

        private static int GetMergedMinutesForPdf(
            DateTime day,
            IEnumerable<Event> dayEvents,
            Func<Event, bool> predicate)
        {
            var intervals = dayEvents
                .Where(predicate)
                .Select(e => ClipToDay(day, e.StartTime, e.EndTime))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Where(x => x.e > x.s)
                .OrderBy(x => x.s)
                .ToList();

            if (intervals.Count == 0)
                return 0;

            var merged = new List<(DateTime s, DateTime e)>();

            foreach (var iv in intervals)
            {
                if (merged.Count == 0 || merged[^1].e < iv.s)
                {
                    merged.Add(iv);
                }
                else if (iv.e > merged[^1].e)
                {
                    merged[^1] = (merged[^1].s, iv.e);
                }
            }

            int minutes = merged.Sum(x => (int)(x.e - x.s).TotalMinutes);
            return RoundDownToQuantum(minutes);
        }

        public byte[] GenerateMonthReport(int year, int month, IEnumerable<Event> events, int employeeId = EventService.DefaultEmployeeId)
        {
            var employee = GlobalSettingsService.EnsureDefaultEmployee(employeeId);
            var semester = GlobalSettingsService.GetSemesterForDate(new DateTime(year, month, 1));
            var semesterSettings = GlobalSettingsService.LoadSemesterSettings(year, semester, employeeId)
                                   ?? GlobalSettingsService.GetDefaultSettings(year, semester, employeeId);

            var calc = new WorkingHoursCalculatorService();
            var eventService = new EventService();

            int daysInMonth = DateTime.DaysInMonth(year, month);

            var monthStart = new DateTime(year, month, 1);
            var monthEndExclusive = monthStart.AddMonths(1);

            var monthEvents = events
                .Where(e => !e.IsDeleted)
                .Where(e => e.StartTime < monthEndExclusive && e.EndTime >= monthStart)
                .ToList();

            var monthPdfComp = eventService.BuildMonthPdfCompensation(year, month, employeeId);


            int workDays = Enumerable.Range(1, daysInMonth)
                .Select(d => new DateTime(year, month, d))
                .Count(dt => dt.DayOfWeek is not DayOfWeek.Saturday
                          and not DayOfWeek.Sunday
                          && !HolidayHelper.IsCzechHoliday(dt));

            double monthQuota = workDays * 8.0;

            using var ms = new MemoryStream();

            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(10);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(h =>
                    {
                        h.Item().PaddingBottom(6)
                            .Text("Evidence pracovní doby, včetně přestávek v práci a práce přesčas")
                            .FontSize(12)
                            .SemiBold()
                            .AlignCenter();

                        h.Item().Row(r =>
                        {
                            r.RelativeItem(1).Column(c =>
                            {
                                c.Item().PaddingBottom(2).Text("Fakulta elektrotechniky a informatiky").FontSize(9);
                                c.Item().PaddingBottom(2).Text($"jméno: {employee.FullName}").FontSize(9);
                                c.Item().Text($"útvar: {employee.Department}").FontSize(9);
                            });

                            r.RelativeItem(1).Column(c =>
                            {
                                c.Item().PaddingBottom(1)
                                    .Text($"pracovní doba: {semesterSettings.GlobalStartTime}–{semesterSettings.GlobalEndTime} hod.")
                                    .FontSize(9);
                                c.Item()
                                    .Text($"docházka za měsíc: {new DateTime(year, month, 1):MMMM yyyy}")
                                    .FontSize(9);
                            });

                            r.ConstantItem(120).Border(1).Padding(4).Column(q =>
                            {
                                q.Item().Text("Fond prac. doby:").Bold().FontSize(9);
                                q.Item().Text($"{monthQuota:F0} hodin").FontSize(9);
                            });
                        });
                    });

                    int sumOverMin = 0;
                    int sumUnderMin = 0;
                    int sumCtrlWorkedMin = 0;

                    page.Content().PaddingTop(10).Column(col =>
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(1);  // Den
                                cd.RelativeColumn(3);  // Začátek
                                cd.RelativeColumn(2);  // 1. př. Od
                                cd.RelativeColumn(2);  // 1. př. Do
                                cd.RelativeColumn(2);  // 2. př. Od
                                cd.RelativeColumn(2);  // 2. př. Do
                                cd.RelativeColumn(3);  // Konec
                                cd.RelativeColumn(2);  // Odprac.
                                cd.RelativeColumn(2);  // Přes.
                                cd.RelativeColumn(2);  // Neodpr.
                                cd.RelativeColumn(3);  // Poznámka
                            });

                            table.Header(h =>
                            {
                                h.Cell().RowSpan(2).Border(1).Text("Den").SemiBold().AlignCenter();
                                h.Cell().RowSpan(2).Border(1).Text("Začátek\npracovní doby").SemiBold().AlignCenter();
                                h.Cell().ColumnSpan(2).Border(1).Text("1. přestávka").SemiBold().AlignCenter();
                                h.Cell().ColumnSpan(2).Border(1).Text("2. přestávka").SemiBold().AlignCenter();
                                h.Cell().RowSpan(2).Border(1).Text("Konec\npracovní\ndoby").SemiBold().AlignCenter();
                                h.Cell().RowSpan(2).Border(1).Text("Odpracováno").SemiBold().AlignCenter();
                                h.Cell().RowSpan(2).Border(1).Text("Přesčas").SemiBold().AlignCenter();
                                h.Cell().RowSpan(2).Border(1).Text("Neodprac.").SemiBold().AlignCenter();
                                h.Cell().RowSpan(2).Border(1).Text("Poznámka\n*)").SemiBold().AlignCenter();

                                h.Cell().Border(1).Background("#EEEEEE").Text("Od").AlignCenter();
                                h.Cell().Border(1).Background("#EEEEEE").Text("Do").AlignCenter();
                                h.Cell().Border(1).Background("#EEEEEE").Text("Od").AlignCenter();
                                h.Cell().Border(1).Background("#EEEEEE").Text("Do").AlignCenter();
                            });

                            for (int d = 1; d <= daysInMonth; d++)
                            {

                                var date = new DateTime(year, month, d);
                                bool isHoliday = HolidayHelper.IsCzechHoliday(date);
                                bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

                                if (isHoliday)
                                {
                                    table.Cell().Border(1).Background("#F0F0F0").Text($"{d}.");
                                    for (int i = 0; i < 9; i++)
                                        table.Cell().Border(1).Background("#F0F0F0").Text("");
                                    table.Cell().Border(1).Background("#F0F0F0").Text("S");
                                    continue;
                                }

                                if (isWeekend)
                                {
                                    table.Cell().Border(1).Background("#F0F0F0").Text($"{d}.");
                                    for (int i = 0; i < 10; i++)
                                        table.Cell().Border(1).Background("#F0F0F0").Text("");
                                    continue;
                                }

                                var dayEvents = monthEvents
                                    .Where(e => !e.IsDeleted)
                                    .Where(e => e.StartTime.Date <= date.Date && e.EndTime.Date >= date.Date)
                                    .ToList();

                                var comp = monthPdfComp.TryGetValue(date.Date, out var c)
                                    ? c
                                    : new EventService.PdfDayCompensation();

                                int actualWorkedMin = GetActualWorkedMinutesForPdf(date, dayEvents);
                                int specialPaidMin = GetPaidSpecialMinutesForPdf(date, dayEvents, employeeId);

                                var pdfBalance = GetDailyPdfBalanceMinutes(date, dayEvents, employeeId);

                                int extraMin = pdfBalance.ExtraMinutes;
                                int realUnderMin = pdfBalance.RealUnderMinutes;

                                extraMin = Math.Max(0, extraMin - comp.ExtraOffsetMinutes);
                                realUnderMin = Math.Max(0, realUnderMin - comp.UnderOffsetMinutes);

                                int displayUnderMin = specialPaidMin + realUnderMin;

                                sumOverMin += extraMin;
                                sumUnderMin += displayUnderMin;
                                sumCtrlWorkedMin += actualWorkedMin;

                                var (actualStart, actualEnd) = GetActualDayWindow(date, dayEvents, employeeId);

                                var lunches = dayEvents
                                    .Where(e => e.EventType == EventType.Lunch && !e.IsDeleted)
                                    .Select(e => new
                                    {
                                        Original = e,
                                        Clip = ClipToDay(date, e.StartTime, e.EndTime)
                                    })
                                    .Where(x => x.Clip.HasValue)
                                    .Select(x => new Event
                                    {
                                        StartTime = x.Clip!.Value.s,
                                        EndTime = x.Clip!.Value.e,
                                        EventType = EventType.Lunch
                                    })
                                    .OrderBy(e => e.StartTime)
                                    .ToList();

                                string note = string.Join("+", dayEvents
                                    .Where(e => e.EventType != EventType.Work &&
                                                e.EventType != EventType.Teaching &&
                                                e.EventType != EventType.Lunch &&
                                                !e.IsDeleted)
                                    .Select(e => CodeFor(e.EventType))
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Distinct());

                                table.Cell().Border(1).Text($"{d}.");
                                table.Cell().Border(1).Text(actualStart?.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(0)?.StartTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(0)?.EndTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(1)?.StartTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(1)?.EndTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(actualEnd?.ToString(@"hh\:mm") ?? "");

                                table.Cell().Border(1).Text($"{TimeSpan.FromMinutes(actualWorkedMin):hh\\:mm\\:ss}");
                                table.Cell().Border(1).Text($"{TimeSpan.FromMinutes(extraMin):hh\\:mm\\:ss}");
                                table.Cell().Border(1).Text($"{TimeSpan.FromMinutes(displayUnderMin):hh\\:mm\\:ss}");
                                table.Cell().Border(1).Text(note);
                            }

                            table.Footer(f =>
                            {
                                f.Cell().ColumnSpan(7).Text("Celkem").AlignRight();

                                f.Cell().Border(1).Text(FormatTotalMinutes(sumCtrlWorkedMin));
                                f.Cell().Border(1).Text(FormatTotalMinutes(sumOverMin));
                                f.Cell().Border(1).Text(FormatTotalMinutes(sumUnderMin));
                                f.Cell().Border(1).Text("");
                            });
                        });

                        var controlMinutes = sumCtrlWorkedMin - sumOverMin + sumUnderMin;

                        col.Item().PaddingTop(8).Text(txt =>
                        {
                            txt.Span("kontr. č. Σ odprac. - přesčas + neodprac. hodin (odpovídá FPD): ").SemiBold();
                            txt.Span(FormatTotalMinutes(controlMinutes));
                        });

                        col.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().Text("podpis zaměstnance:");
                            r.RelativeItem().Text("podpis nadřízeného pracovníka:");
                        });

                        col.Item().PaddingTop(4)
                            .Text("*) do poznámky: D – dovolená, N – nemoc, OČR – ošetřování, L – lékař, PC – pracovní cesta, S – svátek a ostatní dny pracovního klidu")
                            .FontSize(8)
                            .AlignCenter();
                    });
                });
            }).GeneratePdf(ms);

            return ms.ToArray();
        }

        private static bool IsWorkLike(Event e)
            => e.EventType == EventType.Work ||
               e.EventType == EventType.BusinessTrip ||
               e.EventType == EventType.Teaching;

        private static bool IsLockedDay(IEnumerable<Event> dayEvents)
        {
            var evs = dayEvents
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            if (evs.Count == 0)
                return false;

            bool firstManual = !evs.First().IsAutoGenerated && IsWorkLike(evs.First());
            bool lastManual = !evs.Last().IsAutoGenerated && IsWorkLike(evs.Last());

            return firstManual && lastManual;
        }

        private static DateTime MondayOf(DateTime d)
        {
            int delta = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return d.Date.AddDays(-delta);
        }

        public IReadOnlyList<Bitmap> RenderPdfPages(byte[] pdfBytes, int dpi = 300)
        {
            try
            {
                if (pdfBytes is null || pdfBytes.Length == 0)
                    throw new ArgumentException("PDF je prázdné nebo chybí.", nameof(pdfBytes));

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return RenderOnLinuxWithGsBinary(pdfBytes, dpi);
                if (OperatingSystem.IsWindows())
                    return RenderOnWindowsWithGhostscriptNet(pdfBytes, dpi);

                throw new PlatformNotSupportedException("Náhled PDF je podporovaný jen na Windows nebo Linuxu.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF render failed: {ex}");
                throw new PdfRenderException("Nepodařilo se otevřít náhled PDF.", ex);
            }
        }

        [SupportedOSPlatform("windows")]
        public IReadOnlyList<Bitmap> RenderOnWindowsWithGhostscriptNet(byte[] pdfBytes, int dpi = 300)
        {
            try
            {
                GhostscriptVersionInfo version =
                    GhostscriptVersionInfo.GetLastInstalledVersion()
                    ?? throw new PdfRenderException("Ghostscript není nainstalován.");

                using var rasterizer = new GhostscriptRasterizer();
                using var ms = new MemoryStream(pdfBytes);

                rasterizer.Open(ms, version, false);

                var pages = new List<Bitmap>();
                for (int i = 1; i <= rasterizer.PageCount; i++)
                {
                    using var img = rasterizer.GetPage(dpi, i);
                    using var png = new MemoryStream();
                    img.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    png.Position = 0;
                    pages.Add(new Bitmap(png));
                }

                return pages;
            }
            catch (PdfRenderException) { throw; }
            catch (Exception ex)
            {
                throw new PdfRenderException("Chyba při renderování PDF na Windows.", ex);
            }
        }

        private IReadOnlyList<Bitmap> RenderOnLinuxWithGsBinary(byte[] pdfBytes, int dpi)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pdfpreview_" + Guid.NewGuid());
            Directory.CreateDirectory(tmp);

            try
            {
                var pdfFile = Path.Combine(tmp, "doc.pdf");
                File.WriteAllBytes(pdfFile, pdfBytes);

                var outputPattern = Path.Combine(tmp, "page-%03d.png");
                var args = $"-q -dNOPAUSE -dBATCH -sDEVICE=pngalpha -r{dpi} -sOutputFile=\"{outputPattern}\" \"{pdfFile}\"";

                var psi = new ProcessStartInfo("gs", args)
                {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi) ?? throw new PdfRenderException("Nelze spustit Ghostscript (gs).");
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    var err = proc.StandardError.ReadToEnd();
                    throw new PdfRenderException($"Ghostscript selhal (kód {proc.ExitCode}). {err}");
                }

                var images = Directory
                    .GetFiles(tmp, "page-*.png")
                    .OrderBy(f => f)
                    .Select(f => new Bitmap(f))
                    .ToList();

                if (images.Count == 0)
                    throw new PdfRenderException("Nebyla vygenerována žádná stránka náhledu.");

                return images;
            }
            catch (PdfRenderException) { throw; }
            catch (Exception ex)
            {
                throw new PdfRenderException("Chyba při renderování PDF na Linuxu.", ex);
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { }
            }
        }
        private static (DateTime s, DateTime e)? ClipToDay(DateTime day, DateTime s, DateTime e)
        {
            var ds = day.Date;
            var de = ds.AddDays(1);

            var cs = s < ds ? ds : s;
            var ce = e > de ? de : e;

            return ce > cs ? (cs, ce) : null;
        }
        private class PdfRenderException : Exception
        {
            public PdfRenderException(string message) : base(message) { }
            public PdfRenderException(string message, Exception inner) : base(message, inner) { }
        }

        private static (TimeSpan? start, TimeSpan? end) GetActualDayWindow(DateTime day, IEnumerable<Event> dayEvents)
            => GetActualDayWindow(day, dayEvents, EventService.DefaultEmployeeId);

        private static (TimeSpan? start, TimeSpan? end) GetActualDayWindow(DateTime day, IEnumerable<Event> dayEvents, int employeeId)
        {
            var credited = GetEffectiveWorkIntervalsForPdf(day, dayEvents)
                .Where(x => x.e > x.s)
                .OrderBy(x => x.s)
                .ToList();

            if (credited.Count == 0)
                return (null, null);

            return (
                credited.First().s.TimeOfDay,
                credited.Last().e.TimeOfDay
            );
        }

        private static int GetActualWorkedMinutesForPdf(DateTime day, IEnumerable<Event> dayEvents)
        {
            return SumMinutes(GetEffectiveWorkIntervalsForPdf(day, dayEvents));
        }

        private static int GetPaidSpecialMinutesForPdf(DateTime day, IEnumerable<Event> dayEvents)
            => GetPaidSpecialMinutesForPdf(day, dayEvents, EventService.DefaultEmployeeId);

        private static int GetPaidSpecialMinutesForPdf(DateTime day, IEnumerable<Event> dayEvents, int employeeId)
        {
            return SumMinutes(GetPaidSpecialCreditIntervalsForPdf(day, dayEvents, employeeId));
        }

        private static int SumMinutes(IEnumerable<(DateTime s, DateTime e)> intervals)
        {
            int minutes = MergeIntervals(intervals)
                .Sum(x => (int)(x.e - x.s).TotalMinutes);

            return RoundDownToQuantum(minutes);
        }

        private static List<(DateTime s, DateTime e)> GetEffectiveWorkIntervalsForPdf(
            DateTime day,
            IEnumerable<Event> dayEvents)
        {
            var events = dayEvents
                .Where(e => !e.IsDeleted)
                .ToList();

            var paidAbsenceRaw = events
                .Where(IsPaidSpecialForPdf)
                .Select(e => ClipToDay(day, e.StartTime, e.EndTime))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            var paidAbsenceCredit = TakeUpToMinutes(paidAbsenceRaw, 8 * 60);
            int paidAbsenceCreditMin = SumMinutes(paidAbsenceCredit);

            if (paidAbsenceCreditMin >= 8 * 60)
                return new List<(DateTime s, DateTime e)>();

            var work = events
                .Where(IsWorkLike)
                .Select(e => ClipToDay(day, e.StartTime, e.EndTime))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            return SubtractIntervals(work, paidAbsenceRaw);
        }

        private static List<(DateTime s, DateTime e)> GetPaidSpecialCreditIntervalsForPdf(
            DateTime day,
            IEnumerable<Event> dayEvents,
            int employeeId)
        {
            var events = dayEvents
                .Where(e => !e.IsDeleted && IsPaidSpecialForPdf(e))
                .ToList();

            var paidAbsenceRaw = events
                .Select(e => ClipToDay(day, e.StartTime, e.EndTime))
                .Where(x => x.HasValue)
                .Select(x => x!.Value);

            return TakeUpToMinutes(paidAbsenceRaw, 8 * 60);
        }

        private static List<(DateTime s, DateTime e)> MergeIntervals(IEnumerable<(DateTime s, DateTime e)> intervals)
        {
            var sorted = intervals
                .Where(x => x.e > x.s)
                .OrderBy(x => x.s)
                .ToList();

            var merged = new List<(DateTime s, DateTime e)>();

            foreach (var iv in sorted)
            {
                if (merged.Count == 0 || merged[^1].e < iv.s)
                    merged.Add(iv);
                else if (iv.e > merged[^1].e)
                    merged[^1] = (merged[^1].s, iv.e);
            }

            return merged;
        }

        private static List<(DateTime s, DateTime e)> SubtractIntervals(
            IEnumerable<(DateTime s, DateTime e)> source,
            IEnumerable<(DateTime s, DateTime e)> blockers)
        {
            var blocked = MergeIntervals(blockers);
            var result = new List<(DateTime s, DateTime e)>();

            foreach (var seg in source.Where(x => x.e > x.s).OrderBy(x => x.s))
            {
                var cursor = seg.s;

                foreach (var b in blocked)
                {
                    if (b.e <= cursor)
                        continue;

                    if (b.s >= seg.e)
                        break;

                    if (b.s > cursor)
                        result.Add((cursor, b.s < seg.e ? b.s : seg.e));

                    if (b.e > cursor)
                        cursor = b.e > seg.e ? seg.e : b.e;

                    if (cursor >= seg.e)
                        break;
                }

                if (cursor < seg.e)
                    result.Add((cursor, seg.e));
            }

            return MergeIntervals(result);
        }

        private static DailyPdfBalanceMinutes GetDailyPdfBalanceMinutes(
            DateTime day,
            IEnumerable<Event> dayEvents,
            int employeeId)
        {
            const int expectedMin = 8 * 60;

            int paidSpecialMin = GetPaidSpecialMinutesForPdf(day, dayEvents, employeeId);

            if (paidSpecialMin >= expectedMin)
                return new DailyPdfBalanceMinutes(0, 0);

            int workedMin = GetActualWorkedMinutesForPdf(day, dayEvents);

            int creditedMin = RoundDownToQuantum(paidSpecialMin + workedMin);

            int extraMin = Math.Max(0, creditedMin - expectedMin);
            int realUnderMin = Math.Max(0, expectedMin - creditedMin);

            return new DailyPdfBalanceMinutes(
                RoundDownToQuantum(extraMin),
                RoundDownToQuantum(realUnderMin));
        }

        private sealed record DailyPdfBalanceMinutes(int ExtraMinutes, int RealUnderMinutes);
    }
}
