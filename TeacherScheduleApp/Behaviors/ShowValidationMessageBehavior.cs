using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Disposables;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using TeacherScheduleApp.ViewModels;

namespace TeacherScheduleApp.Behaviors
{
    public class ShowValidationMessageBehavior : Behavior<Window>
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
                AssociatedObject.DataContextChanged += AssociatedObject_DataContextChanged;
            }
        }

        private void AssociatedObject_DataContextChanged(object sender, EventArgs e)
        {
            if (AssociatedObject.DataContext is CreateEventDialogViewModel vm)
            {
                RegisterInteraction(vm);
                AssociatedObject.DataContextChanged -= AssociatedObject_DataContextChanged;
            }
        }

        private void RegisterInteraction(CreateEventDialogViewModel vm)
        {
            vm.ShowValidationMessage.RegisterHandler(async interaction =>
            {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ButtonDefinitions = ButtonEnum.Ok,
                    ContentTitle = "Chyba Validace",
                    ContentMessage = interaction.Input,
                    Icon = Icon.Warning
                });
                await msgBox.ShowAsync();
                interaction.SetOutput(Unit.Default);
            });
        }
    }
}
