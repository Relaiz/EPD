using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;
using System.Text.RegularExpressions;

namespace TeacherScheduleApp.Behaviors
{
    public class TimeInputBehavior : Behavior<TextBox>
    {
        private static readonly Regex ValidTextRegex = new Regex("^[0-9:]*$");

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(TextBox.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            }
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(TextBox.TextInputEvent, OnTextInput);
            }
            base.OnDetaching();
        }

        private void OnTextInput(object sender, TextInputEventArgs e)
        {
            if (!ValidTextRegex.IsMatch(e.Text))
            {
                e.Handled = true;
            }
        }
    }
}
