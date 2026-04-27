using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Services
{
    public class EPDGenerator
    {
        private readonly EventService _eventService;
        private readonly Func<string, Task<bool>> _askCollision;
        private readonly int _employeeId;
        private readonly Action<string>? _reportStatus;

        public record EpdImportReport(
            List<Event> Events,
            int TotalRows,
            int ImportedRows,
            int SkippedRows,
            List<string> Errors,
            Encoding UsedEncoding,
            DateTime? RangeStart,
            DateTime? RangeEnd,
            string BatchId,
            string BatchLabel
        );

        static class CsvCols
        {
            public const int TitlePart1 = 3;
            public const int TitlePart2 = 20;
            public const int DescPart1 = 15;
            public const int DescPart2 = 16;
            public const int StartTime = 30;
            public const int EndTime = 31;
            public const int WeekFrom = 32;
            public const int WeekTo = 33;
            public const int Parity = 35;
            public const int BaseDate = 42;
            public const int DateFrom = 43;
            public const int DateTo = 44;
            public const int MinCols = 45;
        }

        public EPDGenerator(EventService eventService, Func<string, Task<bool>> askCollision, int employeeId = EventService.DefaultEmployeeId, Action<string>? reportStatus = null)
        {
            _eventService = eventService;
            _askCollision = askCollision;
            _employeeId = employeeId;
            _reportStatus = reportStatus;
        }

        public async Task<List<Event>> GenerateEPDEventsAsync(string teacherScheduleCsvPath)
        {
            var report = await GenerateEPDEventsWithReportAsync(teacherScheduleCsvPath);
            return report.Events;
        }

        public async Task<EpdImportReport> GenerateEPDEventsWithReportAsync(string teacherScheduleCsvPath)
        {
            _reportStatus?.Invoke("Čtu CSV soubor…");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var (lines, usedEnc) = ReadAllLinesSmart(teacherScheduleCsvPath);
            if (lines.Length == 0)
                throw new InvalidDataException($"Soubor je prázdný: {Path.GetFileName(teacherScheduleCsvPath)}");

            var header = SplitCsvLine(lines[0]);
            if (header.Length < CsvCols.MinCols)
                throw new InvalidDataException(
                    $"Nedostatečný počet sloupců v hlavičce: nalezeno {header.Length}, vyžadováno ≥ {CsvCols.MinCols}. Soubor: {Path.GetFileName(teacherScheduleCsvPath)}");

            var errors = new List<string>();
            var eventsOut = new List<Event>();

            int totalRows = Math.Max(0, lines.Length - 1);
            int imported = 0;
            int skipped = 0;

            var batchId = Guid.NewGuid().ToString("N");
            var batchName = Path.GetFileName(teacherScheduleCsvPath);

            DateTime newImportStart = DateTime.MaxValue;
            DateTime newImportEnd = DateTime.MinValue;

            _reportStatus?.Invoke("Zpracovávám řádky CSV…");

            for (int i = 1; i < lines.Length; i++)
            {
                var raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    skipped++;
                    continue;
                }

                string[] p;
                try
                {
                    p = SplitCsvLine(raw);
                }
                catch
                {
                    skipped++;
                    errors.Add($"Řádek {i + 1}: poškozené CSV (nepárové uvozovky).");
                    continue;
                }

                if (p.Length < CsvCols.MinCols)
                {
                    skipped++;
                    errors.Add($"Řádek {i + 1}: nedostatečný počet sloupců (získáno {p.Length}, vyžadováno ≥ {CsvCols.MinCols}).");
                    continue;
                }

                if (!TryParseDate(p[CsvCols.DateFrom], out var dateFrom) || !TryParseDate(p[CsvCols.DateTo], out var dateTo))
                {
                    skipped++;
                    errors.Add($"Řádek {i + 1}: neplatná data období (DateFrom/DateTo).");
                    continue;
                }

                DateTime baseDate;
                if (!TryParseDate(p[CsvCols.BaseDate], out baseDate))
                {
                    if (!TryParseDate(p[CsvCols.DateFrom], out baseDate))
                    {
                        skipped++;
                        errors.Add($"Řádek {i + 1}: neplatné základní datum (BaseDate/DateFrom).");
                        continue;
                    }
                }

                if (!TryParseWeek(p[CsvCols.WeekFrom], out var weekFrom) || !TryParseWeek(p[CsvCols.WeekTo], out var weekTo))
                {
                    skipped++;
                    errors.Add($"Řádek {i + 1}: neplatná čísla týdnů (WeekFrom/WeekTo).");
                    continue;
                }

                if (!TryParseTime(p[CsvCols.StartTime], out var t0) || !TryParseTime(p[CsvCols.EndTime], out var t1))
                {
                    skipped++;
                    errors.Add($"Řádek {i + 1}: neplatný čas (Start/End).");
                    continue;
                }

                if (t1 <= t0)
                {
                    skipped++;
                    errors.Add($"Řádek {i + 1}: konec je dříve než začátek nebo stejný ({t0:hh\\:mm}–{t1:hh\\:mm}).");
                    continue;
                }

                var parity = SafeFirstUpper(p[CsvCols.Parity], 'K');
                var title = $"{Safe(p[CsvCols.TitlePart1])} {Safe(p[CsvCols.TitlePart2])}".Trim();
                var description = $"{Safe(p[CsvCols.DescPart1])} {Safe(p[CsvCols.DescPart2])}".Trim();
                var targetDow = baseDate.DayOfWeek;

                if (dateTo < dateFrom)
                {
                    skipped++;
                    errors.Add($"Řádek {i + 1}: DateTo je dříve než DateFrom ({dateFrom:dd.MM.yyyy} > {dateTo:dd.MM.yyyy}).");
                    continue;
                }

                newImportStart = dateFrom.Date < newImportStart ? dateFrom.Date : newImportStart;
                newImportEnd = dateTo.Date > newImportEnd ? dateTo.Date : newImportEnd;

                foreach (var dt in EnumerateScheduleDates(
                             dateFrom,
                             dateTo,
                             targetDow,
                             weekFrom,
                             weekTo,
                             parity))
                {
                    eventsOut.Add(new Event
                    {
                        EmployeeId = _employeeId,
                        Title = title,
                        Description = description,
                        StartTime = dt + t0,
                        EndTime = dt + t1,
                        EventType = EventType.Teaching,
                        AllDay = false,
                        IsAutoGenerated = false,
                        IsDeleted = false
                    });
                }

                imported++;
            }

            DateTime? rangeStart = null;
            DateTime? rangeEnd = null;
            ChangedRange changedRange = ChangedRange.Empty;

            if (newImportStart != DateTime.MaxValue && newImportEnd != DateTime.MinValue)
            {
                var oldImported = _eventService
                    .GetEventsForRange(_employeeId, newImportStart, newImportEnd.AddDays(1))
                    .Where(e => !e.IsDeleted && e.ImportBatchId != null)
                    .ToList();

                DateTime? oldImportStart = oldImported.Any() ? oldImported.Min(e => e.StartTime.Date) : null;
                DateTime? oldImportEnd = oldImported.Any() ? oldImported.Max(e => e.StartTime.Date) : null;

                rangeStart = new[]
                {
                    oldImportStart,
                    (DateTime?)newImportStart
                }
                .Where(x => x.HasValue)
                .Min()!.Value.Date;

                rangeEnd = new[]
                {
                    oldImportEnd,
                    (DateTime?)newImportEnd
                }
                .Where(x => x.HasValue)
                .Max()!.Value.Date;

                changedRange = new ChangedRange(rangeStart.Value, rangeEnd.Value);
            }

            if (newImportStart != DateTime.MaxValue && newImportEnd != DateTime.MinValue)
            {
                _reportStatus?.Invoke("Mažu původní import…");

                var deletedRange = await _eventService.BulkSoftDeleteImportedInRangeRawAsync(
                    newImportStart,
                    newImportEnd,
                    _employeeId);

                changedRange = changedRange.Merge(deletedRange);
            }

            if (eventsOut.Count > 0)
            {
                _reportStatus?.Invoke("Ukládám události…");

                var importBatch = new ImportBatch
                {
                    Id = batchId,
                    Label = batchName,
                    ImportedAt = DateTime.Now
                };

                await _eventService.CreateImportBatchAsync(importBatch);

                foreach (var ev in eventsOut)
                    ev.ImportBatchId = batchId;

                _eventService.CreateEventsBulk(eventsOut);

                changedRange = changedRange.Merge(
                    ChangedRange.FromDates(eventsOut.Select(e => e.StartTime.Date)));
            }

            if (changedRange.HasValue)
            {
                _reportStatus?.Invoke("Přegenerovávám automatické události…");

                var processor = new ScheduleChangeProcessor(
                    _eventService,
                    _askCollision,
                    _employeeId);

                await processor.ApplyAsync(
                    changedRange,
                    preserveUserSettings: false,
                    expandToFullYearIfYearHasNoAuto: true);
            }

            _reportStatus?.Invoke("Dokončeno.");

            MessageBus.Current.SendMessage(new EpdGeneratedMessage());

            return new EpdImportReport(
                Events: eventsOut,
                TotalRows: totalRows,
                ImportedRows: imported,
                SkippedRows: skipped,
                Errors: errors,
                UsedEncoding: usedEnc,
                RangeStart: rangeStart,
                RangeEnd: rangeEnd,
                BatchId: batchId,
                BatchLabel: batchName
            );
        }

        private static DateTime FirstOnOrAfter(DateTime start, DayOfWeek targetDow)
        {
            int delta = ((int)targetDow - (int)start.DayOfWeek + 7) % 7;
            return start.AddDays(delta);
        }

        private static IEnumerable<DateTime> EnumerateScheduleDates(
            DateTime dateFrom,
            DateTime dateTo,
            DayOfWeek targetDow,
            int weekFrom,
            int weekTo,
            char parity)
        {
            for (var dt = FirstOnOrAfter(dateFrom.Date, targetDow); dt <= dateTo.Date; dt = dt.AddDays(7))
            {
                int isoWeek = System.Globalization.ISOWeek.GetWeekOfYear(dt);

                if (isoWeek < weekFrom || isoWeek > weekTo)
                    continue;

                switch (parity)
                {
                    case 'L':
                        if (isoWeek % 2 == 0) continue;
                        break;
                    case 'S':
                        if (isoWeek % 2 != 0) continue;
                        break;
                    case 'J':
                        if (((isoWeek - weekFrom) % 2) == 0) continue;
                        break;
                }

                yield return dt;
            }
        }

        private static (string[] lines, Encoding used) ReadAllLinesSmart(string path)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var bytes = File.ReadAllBytes(path);

            var candidates = new List<Encoding>
            {
                new UTF8Encoding(false, true),
                new UTF8Encoding(false, false),
                Encoding.UTF8,
                Encoding.Unicode,
                Encoding.BigEndianUnicode,
                Encoding.GetEncoding(1250),
                Encoding.GetEncoding(1251),
                Encoding.GetEncoding(28592),
                Encoding.Latin1
            };

            string bestText = string.Empty;
            Encoding bestEnc = Encoding.UTF8;
            int bestBad = int.MaxValue;

            foreach (var enc in candidates)
            {
                try
                {
                    var text = enc.GetString(bytes);
                    var bad = text.Count(ch => ch == '\uFFFD');

                    if (bad < bestBad)
                    {
                        bestBad = bad;
                        bestEnc = enc;
                        bestText = text;
                        if (bad == 0) break;
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrEmpty(bestText))
                throw new InvalidDataException($"Nelze rozpoznat kódování souboru: {Path.GetFileName(path)}");

            if (bestText.Length > 0 && bestText[0] == '\uFEFF')
                bestText = bestText[1..];

            var lines = bestText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            return (lines, bestEnc);
        }

        private static string[] SplitCsvLine(string line)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ';' && !inQuotes)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            list.Add(sb.ToString());

            for (int i = 0; i < list.Count; i++)
                list[i] = list[i].Trim();

            return list.ToArray();
        }

        private static bool TryParseDate(string s, out DateTime dt)
        {
            var cultures = new[]
            {
                new CultureInfo("cs-CZ"),
                new CultureInfo("ru-RU"),
                CultureInfo.InvariantCulture,
                CultureInfo.CurrentCulture
            };

            var fmts = new[]
            {
                "d.M.yyyy",
                "dd.MM.yyyy",
                "yyyy-MM-dd",
                "d.M.yyyy H:mm",
                "dd.MM.yyyy H:mm",
                "yyyy-MM-dd H:mm",
                "dd.MM.yyyy HH:mm",
                "H:mm d.M.yyyy",
                "H:mm dd.MM.yyyy"
            };

            foreach (var c in cultures)
            {
                if (DateTime.TryParse(s, c, DateTimeStyles.None, out dt)) return true;
                if (DateTime.TryParseExact(s, fmts, c, DateTimeStyles.None, out dt)) return true;
            }

            dt = default;
            return false;
        }

        private static bool TryParseTime(string s, out TimeSpan ts)
        {
            var fmts = new[] { "h\\:mm", "hh\\:mm", "h\\:mm\\:ss", "hh\\:mm\\:ss" };
            if (TimeSpan.TryParse(s, out ts)) return true;
            if (TimeSpan.TryParseExact(s, fmts, CultureInfo.InvariantCulture, out ts)) return true;
            return false;
        }

        private static bool TryParseWeek(string s, out int w)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out w))
                return w >= 1 && w <= 53;

            w = 0;
            return false;
        }

        private static string Safe(string? s) => s?.Trim() ?? string.Empty;

        private static char SafeFirstUpper(string? s, char fallback)
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallback;

            return char.ToUpperInvariant(s.Trim()[0]);
        }

        private void AdjustDaySettingsForDay(DateTime day)
        {
            var evs = _eventService.GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted)
                .ToList();

            var workEvs = evs
                .Where(e => e.EventType is EventType.Work or EventType.BusinessTrip or EventType.Teaching)
                .OrderBy(e => e.StartTime)
                .ToList();

            if (!workEvs.Any())
                return;

            var merged = MergeIntervals(workEvs);

            var arrival = merged.First().start.TimeOfDay;
            var departure = merged.Last().end.TimeOfDay;

            var lunchEvs = evs
                .Where(e => e.EventType == EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            TimeSpan lunchStart;
            TimeSpan lunchEnd;

            if (lunchEvs.Any())
            {
                lunchStart = lunchEvs.First().StartTime.TimeOfDay;
                lunchEnd = lunchEvs.Last().EndTime.TimeOfDay;
            }
            else
            {
                var resolved = SettingsService.GetResolvedDaySettings(day, _employeeId);
                lunchStart = resolved.LunchStart;
                lunchEnd = resolved.LunchEnd;
            }

            SettingsService.SaveDaySettingsForDate(day, arrival, departure, lunchStart, lunchEnd, _employeeId);
        }

        private List<(DateTime start, DateTime end)> MergeIntervals(IEnumerable<Event> events)
        {
            var intervals = events
                .Select(e => (e.StartTime, e.EndTime))
                .OrderBy(iv => iv.StartTime)
                .ToList();

            var merged = new List<(DateTime s, DateTime e)>();

            foreach (var (s, e) in intervals)
            {
                if (merged.Count == 0 || merged.Last().e < s)
                    merged.Add((s, e));
                else
                {
                    var last = merged[^1];
                    merged[^1] = (last.s, last.e > e ? last.e : e);
                }
            }

            return merged.Select(t => (t.s, t.e)).ToList();
        }
    }
}
