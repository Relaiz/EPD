using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using ReactiveUI;
using System;
using System.Reactive;
using TeacherScheduleApp.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace TeacherScheduleApp.Behaviors
{
    public class ConfirmDeleteBehavior : Behavior<Window>
    {
        protected override void OnAttached()
        {
            base.OnAttached();

            if (AssociatedObject.DataContext is CreateEventDialogViewModel vm)
            {
                RegisterInteraction(vm);
            }
            else
            {
                AssociatedObject.DataContextChanged += OnDataContextChanged;
            }
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (AssociatedObject.DataContext is CreateEventDialogViewModel vm)
            {
                RegisterInteraction(vm);
                AssociatedObject.DataContextChanged -= OnDataContextChanged;
            }
        }

        private void RegisterInteraction(CreateEventDialogViewModel vm)
        {
            vm.RequestDeleteConfirmation.RegisterHandler(async interaction =>
            {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ButtonDefinitions = ButtonEnum.YesNo,
                    ContentTitle = "Potvrdit Smazání",
                    ContentMessage = interaction.Input,
                    Icon = Icon.Warning
                });
                var result = await msgBox.ShowAsync();
                bool confirmed = result == ButtonResult.Yes;
                interaction.SetOutput(confirmed);
            });
        }
    }
}
