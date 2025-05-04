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

            var byDay = Children
                .OfType<CalendarEventControl>()
                .GroupBy(ev => ev.DayIndex);

            foreach (var dayGroup in byDay)
            {
                var events = dayGroup.OrderBy(ev => ev.StartHour).ToList();

                var colEnd = new List<double>();
                var assign = new Dictionary<CalendarEventControl, int>();
                foreach (var ev in events)
                {
                    int idx = colEnd.FindIndex(end => end <= ev.StartHour);
                    if (idx == -1)
                    {
                        idx = colEnd.Count;
                        colEnd.Add(ev.EndHour);
                    }
                    else
                    {
                        colEnd[idx] = ev.EndHour;
                    }
                    assign[ev] = idx;
                }

                int totalCols = colEnd.Count;
                double colMaxWidth = dayWidth / totalCols;

                var columnWidths = new double[totalCols];
                foreach (var ev in events)
                {
                    int col = assign[ev];
                    double natural = ev.DesiredSize.Width + 8;               
                    columnWidths[col] = Math.Max(columnWidths[col], Math.Min(natural, colMaxWidth));
                }
                var columnOffsets = new double[totalCols];
                columnOffsets[0] = dayGroup.Key * dayWidth;
                for (int i = 1; i < totalCols; i++)
                    columnOffsets[i] = columnOffsets[i - 1] + columnWidths[i - 1];

                foreach (var ev in events)
                {
                    int col = assign[ev];
                    double left = columnOffsets[col];
                    double top = ev.StartHour * rowHeight;
                    double width = columnWidths[col];
                    double height = (ev.EndHour - ev.StartHour) * rowHeight;
                    ev.Arrange(new Rect(left, top, width, height));
                }
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
