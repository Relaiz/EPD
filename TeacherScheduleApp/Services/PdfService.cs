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

namespace TeacherScheduleApp.Services
{
    public interface IPdfPreviewService
    {
        byte[] GenerateMonthReport(int year, int month, IEnumerable<Event> events);
        IReadOnlyList<Bitmap> RenderPdfPages(byte[] pdfBytes, int dpi = 300);
    }

    public class PdfService : IPdfPreviewService
    {
        public byte[] GenerateMonthReport(int year, int month, IEnumerable<Event> events)
        {
            var sem = GlobalSettingsService.GetSemesterForDate(new DateTime(year, month, 1));
            var gl = GlobalSettingsService.LoadGlobalSettings(sem)
                      ?? GlobalSettingsService.GetDefaultSettings(sem);

            const string tfmt = @"hh\:mm";
            var stdStart = TimeSpan.ParseExact(gl.GlobalStartTime, tfmt, CultureInfo.InvariantCulture);
            var stdEnd = TimeSpan.ParseExact(gl.GlobalEndTime, tfmt, CultureInfo.InvariantCulture);
            var stdDaily = (stdEnd - stdStart).TotalHours;

            int daysInMonth = DateTime.DaysInMonth(year, month);
            var eventsByDay = events
                .GroupBy(e => e.StartTime.Day)
                .ToDictionary(g => g.Key, g => g.ToList());

            var totalLunchMonthly = events
           .Where(e => e.EventType == EventType.Lunch)
           .Select(e => (e.EndTime - e.StartTime).TotalHours)
            .Sum();
            int workDays = Enumerable.Range(1, daysInMonth)
                .Select(d => new DateTime(year, month, d))
                .Count(dt => dt.DayOfWeek is not DayOfWeek.Saturday
                          && dt.DayOfWeek is not DayOfWeek.Sunday
                          && !HolidayHelper.IsCzechHoliday(dt));
            var monthQuota = (workDays * stdDaily)- totalLunchMonthly;

            TimeSpan totalWorked = TimeSpan.Zero,
                     totalOver = TimeSpan.Zero,
                     totalUnder = TimeSpan.Zero;

            using var ms = new MemoryStream();
            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(10);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Row(r =>
                    {
                        r.RelativeColumn().Column(c =>
                        {
                            c.Item()
                             .Text("Evidence pracovní doby, včetně přestávek v práci a práce přesčas")
                             .FontSize(12).SemiBold().AlignCenter();

                            c.Item().Row(rr =>
                            {
                                rr.ConstantColumn(80).Text($"jméno:\n{gl.EmployeeName}");
                                rr.ConstantColumn(80).Text($"útvar:\n{gl.Department}");
                                rr.ConstantColumn(200).Column(cc =>
                                {
                                    cc.Item().Text($"pracovní doba: {gl.GlobalStartTime}–{gl.GlobalEndTime} hod.");
                                    cc.Item().Text($"docházka za měsíc: {new DateTime(year, month, 1):MMMM yyyy}");
                                });
                            });
                        });

                        r.ConstantColumn(100).Border(1).Padding(4).Column(q =>
                        {
                            q.Item().Text("Fond prac. doby:").Bold();
                            q.Item().Text($"{monthQuota:F0} hodin");
                        });
                    });
                    const float pageMargin = 10f;
                    var pageWidth = PageSizes.A4.Landscape().Width;
                    var contentWidth = pageWidth - 2 * pageMargin; 
                    page.Content().PaddingTop(10).Column(col =>
                    {
                        col.Item()
                           .Width(contentWidth)
                           .Table(table =>
                           {
                               table.ColumnsDefinition(cd =>
                               {
                                   cd.RelativeColumn(1);   // Den
                                   cd.RelativeColumn(3);   // Začátek
                                   cd.RelativeColumn(2);   // 1. př. Od
                                   cd.RelativeColumn(2);   // 1. př. Do
                                   cd.RelativeColumn(2);   // 2. př. Od
                                   cd.RelativeColumn(2);   // 2. př. Do
                                   cd.RelativeColumn(3);   // Konec
                                   cd.RelativeColumn(2);   // Odprac.
                                   cd.RelativeColumn(2);   // Přes.
                                   cd.RelativeColumn(2);   // Neodpr.
                                   cd.RelativeColumn(3);   // Poznámka
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
                                   bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                                   bool isHoliday = HolidayHelper.IsCzechHoliday(date);

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
                                   var us = SettingsService.GetUserSettingsForDate(date);
                                   var def = GetWeekdayDefaults(gl, date.DayOfWeek);
                                   var dayStart = us?.ArrivalTime ?? def.arrival;
                                   var dayEnd = us?.DepartureTime ?? def.departure;
                                   var dayEvents = eventsByDay.GetValueOrDefault(d) ?? new List<Event>();

                                   var special = dayEvents
                                      .Where(e => e.EventType != EventType.Work && e.EventType != EventType.Lunch)
                                      .FirstOrDefault();
                                   if (special != null && dayEvents.Count == 1)
                                   {
                                       TimeSpan startSpec, endSpec, workedSpec, overSpec, underSpec;
                                       string noteSpec;

                                       if (special.EventType == EventType.BusinessTrip)
                                       {
                                           startSpec = special.StartTime.TimeOfDay;
                                           endSpec = special.EndTime.TimeOfDay;
                                           var dur = endSpec - startSpec;
                                           if (dur.TotalHours > stdDaily)
                                               dur = TimeSpan.FromHours(stdDaily);
                                           workedSpec = dur;
                                           overSpec = TimeSpan.FromHours(Math.Max(0, workedSpec.TotalHours - stdDaily));
                                           underSpec = TimeSpan.FromHours(Math.Max(0, stdDaily - workedSpec.TotalHours));
                                       }
                                       else
                                       {
                                           startSpec = TimeSpan.Zero;
                                           endSpec = TimeSpan.Zero;
                                           workedSpec = TimeSpan.Zero;
                                           overSpec = TimeSpan.Zero;
                                           underSpec = TimeSpan.FromHours(stdDaily);
                                       }

                                       noteSpec = special.EventType switch
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
                                       totalWorked += workedSpec;
                                       totalOver += overSpec;
                                       totalUnder += underSpec;

                                       table.Cell().Border(1).Text($"{d}.");
                                       table.Cell().Border(1).Text(
                                           startSpec == TimeSpan.Zero ? "" : startSpec.ToString(@"hh\:mm\:ss"));
                                       table.Cell().Border(1).Text("");
                                       table.Cell().Border(1).Text("");
                                       table.Cell().Border(1).Text("");
                                       table.Cell().Border(1).Text("");
                                       table.Cell().Border(1).Text(
                                       endSpec == TimeSpan.Zero ? "" : endSpec.ToString(@"hh\:mm\:ss"));
                                       table.Cell().Border(1).Text($"{workedSpec:hh\\:mm\\:ss}");
                                       table.Cell().Border(1).Text($"{overSpec:hh\\:mm\\:ss}");
                                       table.Cell().Border(1).Text($"{underSpec:hh\\:mm\\:ss}");
                                       table.Cell().Border(1).Text(noteSpec);
                                       continue;
                                   }

                                   var workPeriods = dayEvents
                                       .Where(e => e.EventType == EventType.Work)
                                       .Select(e => e.EndTime - e.StartTime)
                                       .Aggregate(TimeSpan.Zero, (a, b) => a + b);

                                   var lunches = dayEvents
                                       .Where(e => e.EventType == EventType.Lunch)
                                       .OrderBy(e => e.StartTime)
                                       .ToList();

                                   var totalLunch = lunches
                                       .Select(e => e.EndTime - e.StartTime)
                                       .Aggregate(TimeSpan.Zero, (a, b) => a + b);

                                   var worked = workPeriods > TimeSpan.Zero
                                       ? workPeriods
                                       : (dayEnd - dayStart) - totalLunch;

                                   var note = dayEvents
                                       .FirstOrDefault(e => e.EventType is not EventType.Work and not EventType.Lunch)
                                       ?.EventType switch
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

                                   var over = TimeSpan.FromHours(Math.Max(0, worked.TotalHours - stdDaily));
                                   var underHours = stdDaily - worked.TotalHours - totalLunch.TotalHours;
                                   var under = TimeSpan.FromHours(Math.Max(0, underHours));

                                   totalWorked += worked;
                                   totalOver += over;
                                   totalUnder += under;
                                   table.Cell().Border(1).Text($"{d}.");
                                   table.Cell().Border(1).Text(dayStart.ToString(@"hh\:mm"));
                                   table.Cell().Border(1).Text(lunches.ElementAtOrDefault(0)?.StartTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                   table.Cell().Border(1).Text(lunches.ElementAtOrDefault(0)?.EndTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                   table.Cell().Border(1).Text(lunches.ElementAtOrDefault(1)?.StartTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                   table.Cell().Border(1).Text(lunches.ElementAtOrDefault(1)?.EndTime.TimeOfDay.ToString(@"hh\:mm") ?? "");
                                   table.Cell().Border(1).Text(dayEnd.ToString(@"hh\:mm"));
                                   table.Cell().Border(1).Text($"{worked:hh\\:mm\\:ss}");
                                   table.Cell().Border(1).Text($"{over:hh\\:mm\\:ss}");
                                   table.Cell().Border(1).Text($"{under:hh\\:mm\\:ss}");
                                   table.Cell().Border(1).Text(note);
                               }

                               table.Footer(f =>
                               {
                                   f.Cell().ColumnSpan(7).Text("Celkem").AlignRight();

                                   int wh = totalWorked.Days * 24 + totalWorked.Hours;
                                   int wm = totalWorked.Minutes;
                                   int ws = totalWorked.Seconds;
                                   int oh = totalOver.Days * 24 + totalOver.Hours;
                                   int om = totalOver.Minutes;
                                   int os = totalOver.Seconds;
                                   int uh = totalUnder.Days * 24 + totalUnder.Hours;
                                   int um = totalUnder.Minutes;
                                   int us = totalUnder.Seconds;

                                   f.Cell().Border(1).Text($"{wh:00}:{wm:00}:{ws:00}");
                                   f.Cell().Border(1).Text($"{oh:00}:{om:00}:{os:00}");
                                   f.Cell().Border(1).Text($"{uh:00}:{um:00}:{us:00}");
                                   f.Cell().Border(1).Text("");
                               });
                           });
                        var kontr = totalWorked - totalOver + totalUnder;
                        int kh = kontr.Days * 24 + kontr.Hours;
                        int km = kontr.Minutes;
                        int ks = kontr.Seconds;

                        col.Item().PaddingTop(8).Text(txt =>
                        {
                            txt.Span("kontr. č. Σ odprac. - přesčas + neodprac. hodin (odpovídá FPD): ")
                               .SemiBold();
                            txt.Span($"{kh:00}:{km:00}:{ks:00}");
                        });

                        col.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeColumn().Text("podpis zaměstnance:");
                            r.RelativeColumn().Text("podpis nadřízeného pracovníка:");
                        });

                        col.Item().PaddingTop(4)
                           .Text("*) do poznámky: D – dovolená, N – nemoc, OČR – ošetřování, " +
                                 "L – lékař, PC – pracovní cesta, S – svátek a ostatní dny pracovního klidu")
                           .FontSize(8)
                           .AlignCenter();
                    });
                });
            })
            .GeneratePdf(ms);

            return ms.ToArray();
        }
        public IReadOnlyList<Bitmap> RenderPdfPages(byte[] pdfBytes, int dpi = 300)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return RenderOnLinuxWithGsBinary(pdfBytes, dpi);
            else
                return RenderOnWindowsWithGhostscriptNet(pdfBytes, dpi);
        }
        public IReadOnlyList<Bitmap> RenderOnWindowsWithGhostscriptNet(byte[] pdfBytes, int dpi = 300)
        {
            GhostscriptVersionInfo version;
           
                version = GhostscriptVersionInfo.GetLastInstalledVersion()
                          ?? throw new InvalidOperationException("Ghostscript není nainstalován.");
            
            

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
                using var proc = Process.Start(psi)!;
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    var err = proc.StandardError.ReadToEnd();
                    throw new InvalidOperationException($"gs вернул код {proc.ExitCode}: {err}");
                }
                var images = Directory
                    .GetFiles(tmp, "page-*.png")
                    .OrderBy(f => f)
                    .Select(f =>
                    {                      
                        return new Bitmap(f);
                    })
                    .ToList();

                return images;
            }
            finally
            {
               Directory.Delete(tmp, recursive: true);
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
    }
}
