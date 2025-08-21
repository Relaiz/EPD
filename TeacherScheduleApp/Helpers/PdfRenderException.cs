using System;

namespace TeacherScheduleApp.Helpers
{
    public sealed class PdfRenderException : Exception
    {
        public PdfRenderException(string message, Exception? inner = null)
            : base(message, inner) { }
    }
}
