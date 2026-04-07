using System;
using System.IO;

namespace TeacherScheduleApp.Helpers
{
    public static class WeekBalanceStore
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "TeacherScheduleApp", "balance-weeks");

        static WeekBalanceStore()
        {
            Directory.CreateDirectory(Dir);
        }

        private static string FilePath(int employeeId, int isoYear, int isoWeek)
            => Path.Combine(Dir, $"{employeeId}_{isoYear:D4}-W{isoWeek:D2}.fp");

        public static bool IsBalanced(int employeeId, int isoYear, int isoWeek, string fingerprint)
        {
            var p = FilePath(employeeId, isoYear, isoWeek);
            if (!File.Exists(p))
                return false;

            var saved = File.ReadAllText(p);
            return string.Equals(saved, fingerprint, StringComparison.Ordinal);
        }

        public static void Save(int employeeId, int isoYear, int isoWeek, string fingerprint)
        {
            var p = FilePath(employeeId, isoYear, isoWeek);
            File.WriteAllText(p, fingerprint);
        }

        public static void Invalidate(int employeeId, int isoYear, int isoWeek)
        {
            var p = FilePath(employeeId, isoYear, isoWeek);
            if (File.Exists(p))
                File.Delete(p);
        }
    }
}