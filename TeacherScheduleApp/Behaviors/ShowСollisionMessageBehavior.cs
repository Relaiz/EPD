using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using ReactiveUI;
using System;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using TeacherScheduleApp.ViewModels;

namespace TeacherScheduleApp.Behaviors
{
    public class ShowCollisionMessageBehavior : Behavior<Window>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            TryRegister();
            AssociatedObject.DataContextChanged += (_, _) => TryRegister();
        }

        private void TryRegister()
        {
            if (AssociatedObject.DataContext is MainWindowViewModel vm)
            {
                vm.ShowCollisionMessage.RegisterHandler(async interaction =>
                {
                    var msgBox = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ButtonDefinitions = ButtonEnum.YesNo,
                        ContentTitle = "Kolize s obědem",
                        ContentMessage = interaction.Input,
                        Icon = Icon.Warning
                    });
                    var result = await msgBox.ShowWindowDialogAsync(AssociatedObject);
                    interaction.SetOutput(result == ButtonResult.Yes);
                });
            }
        }
    }
}
