using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using System.Linq;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.ViewModels;

namespace TeacherScheduleApp.Behaviors
{
    public class EventClickBehavior : Behavior<Control>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PointerPressed += OnPointerPressed;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.PointerPressed -= OnPointerPressed;
            base.OnDetaching();
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
           
            if (AssociatedObject.DataContext is Event ev)
            {
                var vm = FindMonthViewModel(AssociatedObject);
                if (vm != null)
                {
                    vm.OnEventClicked(ev);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Vyhledá ovládací prvek, který má ve svém rodiči datový kontext MonthViewModel.
        /// </summary>
        private MonthViewModel FindMonthViewModel(Control start)
        {
            Control current = start;
            while (current != null)
            {
                if (current.DataContext is MonthViewModel vm)
                    return vm;
                current = current.Parent as Control;
            }
            return null;
        }
    }
}
