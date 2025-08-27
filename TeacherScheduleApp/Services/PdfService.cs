using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using Ghostscript.NET;
using Ghostscript.NET.Rasterizer;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TeacherScheduleApp.Models;
using QuestPDF.Helpers;
using TeacherScheduleApp.Services;
using TeacherScheduleApp.Helpers;
using static TeacherScheduleApp.Models.GlobalSettings;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.VisualBasic;
using System.Runtime.CompilerServices;
using System.Text;

namespace TeacherScheduleApp.Services
{
    public interface IPdfPreviewService
    {
        byte[] GenerateMonthReport(int year, int month, IEnumerable<Event> events);
        IReadOnlyList<Bitmap> RenderPdfPages(byte[] pdfBytes, int dpi = 300);
    }

    public class PdfService : IPdfPreviewService
    {
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
        public byte[] GenerateMonthReport(int year, int month, IEnumerable<Event> events)
        {
            var sem = GlobalSettingsService.GetSemesterForDate(new DateTime(year, month, 1));
            var gl = GlobalSettingsService.LoadGlobalSettings(year, sem)
                     ?? GlobalSettingsService.GetDefaultSettings(year, sem);
            var calc = new WorkingHoursCalculatorService();

            int daysInMonth = DateTime.DaysInMonth(year, month);
            var eventsByDay = events
                .GroupBy(e => e.StartTime.Day)
                .ToDictionary(g => g.Key, g => g.ToList());

            int workDays = Enumerable.Range(1, daysInMonth)
                .Select(d => new DateTime(year, month, d))
                .Count(dt => dt.DayOfWeek is not DayOfWeek.Saturday
                          && dt.DayOfWeek is not DayOfWeek.Sunday
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

                    // HEADER
                    page.Header().Column(h =>
                    {
                        h.Item().Container().PaddingBottom(6)
                            .Text("Evidence pracovní doby, včetně přestávek v práci a práce přesčas")
                            .FontSize(12).SemiBold().AlignCenter();

                        h.Item().Row(r =>
                        {
                            r.RelativeColumn(1).Column(c =>
                            {
                                c.Item().Container().PaddingBottom(2).Text("Fakulta elektrotechniky a informatiky")
                                    .FontSize(9).AlignLeft();
                                c.Item().Container().PaddingBottom(2).Text($"jméno: {gl.EmployeeName}")
                                    .FontSize(9).AlignLeft();
                                c.Item().Text($"útvar: {gl.Department}").FontSize(9).AlignLeft();
                            });

                            r.RelativeColumn(1).Column(c =>
                            {
                                c.Item().Container().PaddingBottom(1)
                                    .Text($"pracovní doba: {gl.GlobalStartTime}–{gl.GlobalEndTime} hod.")
                                    .FontSize(9).AlignLeft();
                                c.Item().Text($"docházka za měsíc: {new DateTime(year, month, 1):MMMM yyyy}")
                                    .FontSize(9).AlignLeft();
                            });

                            r.ConstantColumn(120).Container().Border(1).Padding(4).Column(q =>
                            {
                                q.Item().Text("Fond prac. doby:").Bold().FontSize(9).AlignLeft();
                                q.Item().Text($"{monthQuota:F0} hodin").FontSize(9).AlignLeft();
                            });
                        });
                    });

                    const float pageMargin = 10f;
                    var pageWidth = PageSizes.A4.Landscape().Width;
                    var contentWidth = pageWidth - 2 * pageMargin;
                    double sumWorked = 0.0;
                    double sumOvers = 0.0;
                    double sumNeod = 0.0;
                    // BODY
                    page.Content().PaddingTop(10).Column(col =>
                    {
                        col.Item().Width(contentWidth).Table(table =>
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
                                var dm = calc.DailyMetrics(date, events);

                                const double dayQuota = 8.0;
                                double worked = dm.workInclBT;
                                double specialOnly = Math.Max(0.0, dm.specialNonPc);
                                double movedOut = WorkTransferReportingService.GetMovedOut(date);
                                double movedIn = WorkTransferReportingService.GetMovedInDetails(date).Sum(x => x.hours);
                                double effective = worked - movedOut + movedIn;
                                double overShown = (movedIn > 1e-6) ? 0.0 : Math.Max(0.0, effective - dayQuota);
                                double neodShown = (movedOut > 1e-6) ? 0.0 : Math.Max(0.0, dayQuota - effective);
                                double neodShown1 = specialOnly;
                                sumWorked += worked;
                                sumOvers += overShown;
                                sumNeod += neodShown1;
                                var def = GetWeekdayDefaults(gl, date.DayOfWeek);
                                var us = SettingsService.GetUserSettingsForDate(date);
                                var dayStart = us?.ArrivalTime ?? def.arrival;
                                var dayEnd = us?.DepartureTime ?? def.departure;

                                var dayEvents = eventsByDay.GetValueOrDefault(d) ?? new List<Event>();

                                var lunches = dayEvents
                                    .Where(e => e.EventType == EventType.Lunch)
                                    .OrderBy(e => e.StartTime)
                                    .ToList();
                                var winS = date + dayStart;
                                var winE = date + dayEnd;
                                bool hasUserLunch = us is not null && us.LunchEnd > us.LunchStart;
                                var lunchStart = hasUserLunch ? us.LunchStart : def.lunchStart;
                                var lunchEnd = hasUserLunch ? us.LunchEnd : def.lunchEnd;

                                
                                string note = string.Join("+",
                                    dayEvents
                                        .Where(e => e.EventType != EventType.Work && e.EventType != EventType.Lunch)
                                        .Select(e => CodeFor(e.EventType))
                                        .Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct());
                                var workedTs = TimeSpan.FromHours(worked);
                               // var overTs = TimeSpan.FromHours(over);
                             //   var neodTs = TimeSpan.FromHours(neod);
                                table.Cell().Border(1).Text($"{d}.");
                                table.Cell().Border(1).Text(dayStart.ToString(@"hh\:mm"));
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(0)?.StartTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(0)?.EndTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(1)?.StartTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(lunches.ElementAtOrDefault(1)?.EndTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                table.Cell().Border(1).Text(dayEnd.ToString(@"hh\:mm"));

                                table.Cell().Border(1).Text($"{TimeSpan.FromHours(worked):hh\\:mm\\:ss}");
                                table.Cell().Border(1).Text($"{TimeSpan.FromHours(overShown):hh\\:mm\\:ss}");
                                table.Cell().Border(1).Text($"{TimeSpan.FromHours(neodShown):hh\\:mm\\:ss}");
                                table.Cell().Border(1).Text(note);
                            }

                            table.Footer(f =>
                            {
                                f.Cell().ColumnSpan(7).Text("Celkem").AlignRight();

                                var monthTotals = calc.MonthlyMetrics(year, month, events);
                                var totalWorkedTs = TimeSpan.FromHours(sumWorked);
                                var totalOverTs = TimeSpan.FromHours(sumOvers);
                                var totalNeodTs = TimeSpan.FromHours(sumNeod);

                                f.Cell().Border(1).Text($"{(totalWorkedTs.Days * 24 + totalWorkedTs.Hours):00}:{totalWorkedTs.Minutes:00}:{totalWorkedTs.Seconds:00}");
                                f.Cell().Border(1).Text($"{(totalOverTs.Days * 24 + totalOverTs.Hours):00}:{totalOverTs.Minutes:00}:{totalOverTs.Seconds:00}");
                                f.Cell().Border(1).Text($"{(totalNeodTs.Days * 24 + totalNeodTs.Hours):00}:{totalNeodTs.Minutes:00}:{totalNeodTs.Seconds:00}");
                                f.Cell().Border(1).Text("");
                            });
                        });

                        var monthTotals2 = calc.MonthlyMetrics(year, month, events);
                        var kontr = TimeSpan.FromHours(sumWorked - sumOvers + sumNeod); int kh = kontr.Days * 24 + kontr.Hours;
                        int km = kontr.Minutes;
                        int ks = kontr.Seconds;

                        col.Item().PaddingTop(8).Text(txt =>
                        {
                            txt.Span("kontr. č. Σ odprac. - přesčas + neodprac. hodin (odpovídá FPD): ").SemiBold();
                            txt.Span($"{kh:00}:{km:00}:{ks:00}");
                        });

                        col.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeColumn().Text("podpis zaměstnance:");
                            r.RelativeColumn().Text("podpis nadřízeného pracovníka:");
                        });

                        col.Item().PaddingTop(4)
                           .Text("*) do poznámky: D – dovolená, N – nemoc, OČR – ošetřování, L – lékař, PC – pracovní cesta, S – svátek a ostatní dny pracovního klidu")
                           .FontSize(8).AlignCenter();
                    });
                });
            })
            .GeneratePdf(ms);

            return ms.ToArray();
        }


        public IReadOnlyList<Bitmap> RenderPdfPages(byte[] pdfBytes, int dpi = 300)
        {
            try
            {
                if (pdfBytes is null || pdfBytes.Length == 0)
                    throw new ArgumentException("PDF je prázdné nebo chybí.", nameof(pdfBytes));

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return RenderOnLinuxWithGsBinary(pdfBytes, dpi);
                else
                    return RenderOnWindowsWithGhostscriptNet(pdfBytes, dpi);
            }

            catch (Exception ex)
            {

                Debug.WriteLine($"PDF render failed: {ex}");
                throw new PdfRenderException("Nepodařilo se otevřít náhled PDF.", ex);
            }
        }
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
                var args = $"-q -dNOPAUSE -dBATCH -sDEVICE=pngalpha -r{dpi} " +
                           $"-sOutputFile=\"{outputPattern}\" \"{pdfFile}\"";

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
        public   static (TimeSpan arrival, TimeSpan departure, TimeSpan lunchStart, TimeSpan lunchEnd)
        GetWeekdayDefaults(GlobalSettings g, DayOfWeek dow)
        {
            const string fmt = @"hh\:mm";
            return dow switch
            {
                DayOfWeek.Monday => (TimeSpan.ParseExact(g.MondayArrival, fmt, null),
                                        TimeSpan.ParseExact(g.MondayDeparture, fmt, null),
                                        TimeSpan.ParseExact(g.MondayLunchStart, fmt, null),
                                        TimeSpan.ParseExact(g.MondayLunchEnd, fmt, null)),
                DayOfWeek.Tuesday => (TimeSpan.ParseExact(g.TuesdayArrival, fmt, null),
                                        TimeSpan.ParseExact(g.TuesdayDeparture, fmt, null),
                                        TimeSpan.ParseExact(g.TuesdayLunchStart, fmt, null),
                                        TimeSpan.ParseExact(g.TuesdayLunchEnd, fmt, null)),
                DayOfWeek.Wednesday => (TimeSpan.ParseExact(g.WednesdayArrival, fmt, null),
                                        TimeSpan.ParseExact(g.WednesdayDeparture, fmt, null),
                                        TimeSpan.ParseExact(g.WednesdayLunchStart, fmt, null),
                                        TimeSpan.ParseExact(g.WednesdayLunchEnd, fmt, null)),
                DayOfWeek.Thursday => (TimeSpan.ParseExact(g.ThursdayArrival, fmt, null),
                                        TimeSpan.ParseExact(g.ThursdayDeparture, fmt, null),
                                        TimeSpan.ParseExact(g.ThursdayLunchStart, fmt, null),
                                        TimeSpan.ParseExact(g.ThursdayLunchEnd, fmt, null)),
                DayOfWeek.Friday => (TimeSpan.ParseExact(g.FridayArrival, fmt, null),
                                        TimeSpan.ParseExact(g.FridayDeparture, fmt, null),
                                        TimeSpan.ParseExact(g.FridayLunchStart, fmt, null),
                                        TimeSpan.ParseExact(g.FridayLunchEnd, fmt, null)),
                _ => (TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)
            };
        }
        private List<(DateTime start, DateTime end)> MergeIntervals(List<(DateTime start, DateTime end)> intervals)
        {
            var sorted = intervals.OrderBy(x => x.start).ToList();
            var merged = new List<(DateTime start, DateTime end)>();
            foreach (var seg in sorted)
            {
                if (merged.Count == 0 || merged[^1].end < seg.start)
                    merged.Add(seg);
                else
                    merged[^1] = (
                        merged[^1].start,
                        merged[^1].end > seg.end ? merged[^1].end : seg.end
                    );
            }
            return merged;
        }
    }
}
