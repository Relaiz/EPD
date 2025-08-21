using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeacherScheduleApp.Services
{
    public interface IErrorDialogService
    {
        Task ShowErrorAsync(string title, string message);
    }
    public sealed class ErrorDialogService : IErrorDialogService
    {
        public async Task ShowErrorAsync(string title, string message)
        {
            var mb = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = title,
                ContentMessage = message,
                ButtonDefinitions = ButtonEnum.Ok,
                Icon = Icon.Error,
                ShowInCenter = true,
                CanResize = false
            });
            await mb.ShowAsync();
        }
    }
}
