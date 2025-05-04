using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using System.Linq;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.ViewModels;

namespace TeacherScheduleApp.Behaviors
{
    public class DayCellClickBehavior : Behavior<Control>
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
            var ctrl = e.Source as Control;
            while (ctrl != null)
            {
                if (ctrl.DataContext is Event ev)
                {
                    return;
                }         
                ctrl = ctrl.Parent as Control;
            }

         
            if (AssociatedObject.DataContext is MonthViewModel.MonthDayInfo dayInfo)
            {
            
                var itemsControl = FindItemsControl(AssociatedObject);
                if (itemsControl?.DataContext is MonthViewModel vm)
                {
                    vm.OnEmptySpaceClicked(dayInfo);
                }
            }
        }

      
        private ItemsControl FindItemsControl(Control start)
        {
            var parent = start.Parent;
            while (parent != null)
            {
                if (parent is ItemsControl ic)
                    return ic;
                parent = (parent as Control)?.Parent;
            }
            return null;
        }
    }
}
