using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using TeacherScheduleApp.Controls;
using TeacherScheduleApp.ViewModels;

namespace TeacherScheduleApp.Behaviors
{
    public class AttachCalendarPanelBehavior : Behavior<CalendarPanel>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject.DataContext is WeekViewModel wm)
            {
                wm.AttachCalendarPanel(AssociatedObject);
            }
            else if(AssociatedObject.DataContext is DayViewModel dm)
            {
                dm.AttachCalendarPanel(AssociatedObject);
            }
            
            else
            {
                AssociatedObject.DataContextChanged += AssociatedObject_DataContextChanged;
            }
        }

        private void AssociatedObject_DataContextChanged(object sender, System.EventArgs e)
        {
            if (AssociatedObject.DataContext is WeekViewModel vm)
            {
                vm.AttachCalendarPanel(AssociatedObject);
                AssociatedObject.DataContextChanged -= AssociatedObject_DataContextChanged;
            } 
            else if (AssociatedObject.DataContext is DayViewModel dm)
            {
                dm.AttachCalendarPanel(AssociatedObject);
                AssociatedObject.DataContextChanged -= AssociatedObject_DataContextChanged;
            }
            
        }
        protected override void OnDetaching()
        {
            base.OnDetaching();
            AssociatedObject.DataContextChanged -= AssociatedObject_DataContextChanged;
        }
    }
}
