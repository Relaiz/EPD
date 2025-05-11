using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;
using System.Reactive.Linq;
using TeacherScheduleApp.ViewModels;
using TeacherScheduleApp.Models;
using System.Reactive;
using System;

namespace TeacherScheduleApp.Views
{
    public partial class CreateEventDialog : Window
    {
        public CreateEventDialog()
        {
            InitializeComponent();
        }
    }
}
