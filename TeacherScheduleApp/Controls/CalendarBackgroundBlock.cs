using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia;

namespace TeacherScheduleApp.Controls
{
    public class CalendarBackgroundBlock : Border
    {
        public int DayIndex { get; set; }
        public double StartHour { get; set; }
        public double EndHour { get; set; }

        public CalendarBackgroundBlock()
        {
            Background = Brushes.LightGray;
            Opacity = 0.25;
            IsHitTestVisible = false;
            ZIndex = 0;
            Padding = new Thickness(0);
            CornerRadius = new CornerRadius(0);
        }
    }
}
