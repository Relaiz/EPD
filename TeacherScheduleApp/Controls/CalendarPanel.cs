using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TeacherScheduleApp.Controls
{
    /// <summary>
    /// Panel, který umisťuje CalendarEventControl podle dne/hodiny
    /// a zpracovává kliknutí na prázdné místo.
    /// </summary>
    public class CalendarPanel : Panel
    {
        public int DaysCount { get; set; } = 1;
        public int HoursCount { get; set; } = 24;
        public event Action<int, double>? DayHourClicked;
        private const double HourHeight = 50;
        private const double MinDayWidth = 200;

        protected override Size MeasureOverride(Size availableSize)
        {
            double minWidth = DaysCount * MinDayWidth;
            double width = double.IsInfinity(availableSize.Width)
                ? minWidth
                : Math.Max(availableSize.Width, minWidth);
            double height = HoursCount * HourHeight;
            if (!double.IsInfinity(availableSize.Height))
                height = Math.Min(availableSize.Height, height);

            var desired = new Size(width, height);
            foreach (var child in Children)
                child.Measure(new Size(double.PositiveInfinity, desired.Height));

            return desired;
        }


        protected override Size ArrangeOverride(Size finalSize)
        {
            double dayWidth = finalSize.Width / DaysCount;
            double rowHeight = finalSize.Height / HoursCount;

            foreach (var ev in Children.OfType<CalendarEventControl>())
            {
                int day = ev.DayIndex;
                if (day < 0 || day >= DaysCount) continue;

                double leftBase = day * dayWidth;

                int cols = Math.Max(1, ev.OverlapCount);
                int colIdx = Math.Min(Math.Max(0, ev.OverlapIndex), cols - 1);

                double colWidth = dayWidth / cols;

                double left = leftBase + colIdx * colWidth;
                double top = ev.StartHour * rowHeight;
                double width = colWidth - 4;
                double height = (ev.EndHour - ev.StartHour) * rowHeight;

                ev.Arrange(new Rect(left, top, width, height));
            }

            return finalSize;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetPosition(this);
            double colWidth = Bounds.Width / DaysCount;
            double rowHeight = Bounds.Height / HoursCount;

            int dayIndex = (int)(point.X / colWidth);
            double hour = point.Y / rowHeight;

            if (dayIndex >= 0 && dayIndex < DaysCount && hour >= 0 && hour < HoursCount)
            {
                DayHourClicked?.Invoke(dayIndex, hour);
            }
        }
    }
}
