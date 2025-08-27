using System;
using System.IO;

namespace TeacherScheduleApp.Helpers
{
    public static class MonthBalanceStore
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "TeacherScheduleApp", "balance");

        static MonthBalanceStore() { Directory.CreateDirectory(Dir); }

        private static string FilePath(int y, int m) => Path.Combine(Dir, $"{y:D4}-{m:D2}.fp");

        public static bool IsBalanced(int y, int m, string fingerprint)
        {
            var p = FilePath(y, m);
            if (!File.Exists(p)) return false;
            var saved = File.ReadAllText(p);
            return string.Equals(saved, fingerprint, StringComparison.Ordinal);
        }

        public static void Save(int y, int m, string fingerprint)
        {
            var p = FilePath(y, m);
            File.WriteAllText(p, fingerprint);
        }

        public static void Invalidate(int y, int m)
        {
            var p = FilePath(y, m);
            if (File.Exists(p)) File.Delete(p);
        }
    }
}
