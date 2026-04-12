using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
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
        private const double HourHeight = 80;
        private const double MinDayWidth = 200;
        private const double EventHorizontalGap = 4;

        protected override Size MeasureOverride(Size availableSize)
        {
            var days = Math.Max(1, DaysCount);
            var hours = Math.Max(1, HoursCount);

            double minWidth = days * MinDayWidth;
            double width = double.IsInfinity(availableSize.Width)
                ? minWidth
                : Math.Max(availableSize.Width, minWidth);

            double height = hours * HourHeight;

            var desired = new Size(Math.Max(1, width), Math.Max(1, height));
            double dayWidth = desired.Width / days;

            foreach (var child in Children)
            {
                child.Measure(new Size(dayWidth, desired.Height));
            }

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

            static double ExclusiveHeight(double topPx, double bottomPx)
                => Math.Max(bottomPx - topPx - 1, 1);

            foreach (var bg in Children.OfType<CalendarBackgroundBlock>())
            {
                int day = bg.DayIndex;
                if (day < 0 || day >= days)
                    continue;

                double sh = Math.Clamp(bg.StartHour, 0, hours);
                double eh = Math.Clamp(bg.EndHour, sh, hours);

                double left = Math.Floor(day * dayWidth);
                double top = Math.Floor(sh * rowHeight);
                double bottom = Math.Ceiling(eh * rowHeight);

                double w = Math.Max(Math.Ceiling(dayWidth), 1);
                double h = ExclusiveHeight(top, bottom);

                bg.Arrange(new Rect(left, top, w, h));
            }

            foreach (var ev in Children.OfType<CalendarEventControl>())
            {
                int day = ev.DayIndex;
                if (day < 0 || day >= days)
                    continue;

                double sh = Math.Clamp(ev.StartHour, 0, hours);
                double eh = Math.Clamp(ev.EndHour, sh + 0.01, hours);

                int cols = Math.Max(1, ev.OverlapCount);
                int colIdx = Math.Clamp(ev.OverlapIndex, 0, cols - 1);

                double baseLeft = day * dayWidth;
                double columnWidth = dayWidth / cols;

                double left = Math.Floor(baseLeft + colIdx * columnWidth);
                double top = Math.Floor(sh * rowHeight);
                double bottom = Math.Ceiling(eh * rowHeight);

                double w = Math.Max(Math.Floor(columnWidth - EventHorizontalGap), 1);
                double h = Math.Max(ExclusiveHeight(top, bottom), 2);

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
            {
                DayHourClicked?.Invoke(dayIndex, hour);
            }
        }
    }
}