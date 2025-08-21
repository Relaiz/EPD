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
            var days = Math.Max(1, DaysCount);
            var hours = Math.Max(1, HoursCount);

            const double HourHeight = 50;
            const double MinDayWidth = 200;

            double minWidth = days * MinDayWidth;
            double width = double.IsInfinity(availableSize.Width)
                ? minWidth
                : Math.Max(availableSize.Width, minWidth);

            double height = hours * HourHeight;
            if (!double.IsInfinity(availableSize.Height))
                height = Math.Min(availableSize.Height, height);

            var desired = new Size(Math.Max(1, width), Math.Max(1, height));
            foreach (var child in Children)
                child.Measure(new Size(double.PositiveInfinity, desired.Height));

            return desired;
        }


        protected override Size ArrangeOverride(Size finalSize)
        {
            var days = Math.Max(1, DaysCount);
            var hours = Math.Max(1, HoursCount);

            double width = Math.Max(1, finalSize.Width);
            double height = Math.Max(1, finalSize.Height);

            double dayWidth = width / days;
            double rowHeight = height / hours;
            static double ExclusiveHeight(double topPx, double bottomPx) => Math.Max(bottomPx - topPx - 1, 1);
            foreach (var bg in Children.OfType<CalendarBackgroundBlock>())
            {
                int day = bg.DayIndex;
                if (day < 0 || day >= days) continue;

                double sh = Math.Clamp(bg.StartHour, 0, hours);
                double eh = Math.Clamp(bg.EndHour, sh, hours);

                double left = day * dayWidth;
                double top = sh * rowHeight;
                double bot = eh * rowHeight;

                left = Math.Floor(left);
                top = Math.Floor(top);

                double h = ExclusiveHeight(top, bot);
                double w = Math.Max(Math.Floor(dayWidth), 1);

                bg.Arrange(new Rect(left, top, w, h));
            }

            foreach (var ev in Children.OfType<CalendarEventControl>())
            {
                int day = ev.DayIndex;
                if (day < 0 || day >= days) continue;

                double sh = Math.Clamp(ev.StartHour, 0, hours);
                double eh = Math.Clamp(ev.EndHour, sh + 0.01, hours);

                double leftBase = day * dayWidth;

                int cols = Math.Max(1, ev.OverlapCount);
                int colIdx = Math.Clamp(ev.OverlapIndex, 0, cols - 1);
                double cW = dayWidth / cols;

                double top = sh * rowHeight;
                double bot = eh * rowHeight;

                double left = Math.Floor(leftBase + colIdx * cW);
                top = Math.Floor(top);

                double w = Math.Max(Math.Floor(cW) - 4, 1);
                double h = Math.Max(ExclusiveHeight(top, bot), 2);

                ev.Arrange(new Rect(left, top, w, h));
            }

            return finalSize;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var days = Math.Max(1, DaysCount);
            var hours = Math.Max(1, HoursCount);

            if (Bounds.Width <= 0 || Bounds.Height <= 0)
                return;

            var point = e.GetPosition(this);
            double colWidth = Bounds.Width / days;
            double rowHeight = Bounds.Height / hours;

            if (colWidth <= 0 || rowHeight <= 0)
                return;

            int dayIndex = (int)Math.Floor(point.X / colWidth);
            double hour = point.Y / rowHeight;

            if (dayIndex >= 0 && dayIndex < days && hour >= 0 && hour < hours)
                DayHourClicked?.Invoke(dayIndex, hour);
        }
    }
}
