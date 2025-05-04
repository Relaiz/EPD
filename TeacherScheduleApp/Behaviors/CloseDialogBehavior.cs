using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using ReactiveUI;
using System;
using System.Reactive;
using TeacherScheduleApp.ViewModels;

namespace TeacherScheduleApp.Behaviors
{
    public class CloseDialogBehavior : Behavior<Window>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject.DataContext is CreateEventDialogViewModel vm )
            {
                RegisterClose(vm);
            }
            else
            {
                AssociatedObject.DataContextChanged += AssociatedObject_DataContextChanged;
            }
        }

        private void AssociatedObject_DataContextChanged(object sender, EventArgs e)
        {
            if (AssociatedObject.DataContext is CreateEventDialogViewModel vm)
            {
                RegisterClose(vm);
                AssociatedObject.DataContextChanged -= AssociatedObject_DataContextChanged;
            }
        }

        private void RegisterClose(CreateEventDialogViewModel vm)
        {
            vm.RequestClose.RegisterHandler(interaction =>
            {
                AssociatedObject.Close(interaction.Input);
                interaction.SetOutput(Unit.Default);
            });
        }
    }
}
