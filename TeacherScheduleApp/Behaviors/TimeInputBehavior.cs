using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;
using System;
using Avalonia;
using System.Reactive.Linq;
using System.Text.RegularExpressions;

namespace TeacherScheduleApp.Behaviors
{
    public class TimeInputBehavior : Behavior<TextBox>
    {
        private static readonly Regex AllowedChars = new Regex("^[0-9:]*$");
        private static readonly Regex StrictTime = new Regex(@"^([01]\d|2[0-3]):[0-5]\d$");
        private IDisposable? _textChangedSub;
        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(TextBox.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
                AssociatedObject.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
                _textChangedSub = AssociatedObject.GetObservable<string?>(TextBox.TextProperty).Subscribe(OnTextChanged);
            }
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(TextBox.TextInputEvent, OnTextInput);
                AssociatedObject.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
            }
            _textChangedSub?.Dispose();
            _textChangedSub = null;

            base.OnDetaching();
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!AllowedChars.IsMatch(e.Text))
            {
                e.Handled = true;
                return;
            }
            if (AssociatedObject is { } tb && (tb.Text?.Length ?? 0) >= 5)
                e.Handled = true;
        }

        private void OnTextChanged(string? text)
        {
            if (AssociatedObject == null) return;
            if (string.IsNullOrEmpty(text)) return;

            if (text!.Length == 4 && Regex.IsMatch(text, @"^\d{4}$"))
                AssociatedObject.Text = text.Insert(2, ":");

            if (AssociatedObject.Text!.Length > 5)
                AssociatedObject.Text = AssociatedObject.Text.Substring(0, 5);
        }

        private void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (AssociatedObject == null) return;
            var t = AssociatedObject.Text?.Trim() ?? "";

            if (t.Length == 0) return;

            if (!StrictTime.IsMatch(t))
            {
                AssociatedObject.Text = "";
            }
        }
    }
}
