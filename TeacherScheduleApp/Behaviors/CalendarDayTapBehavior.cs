using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;
using Avalonia;
using System.Windows.Input;
using Avalonia.VisualTree;

namespace TeacherScheduleApp.Behaviors
{
    public class CalendarDayTapBehavior : Behavior<Calendar>
    {
        public static readonly StyledProperty<ICommand?> CommandProperty =
            AvaloniaProperty.Register<CalendarDayTapBehavior, ICommand?>(nameof(Command));

        public ICommand? Command
        {
            get => GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        private IInputElement? _hookTarget;

        protected override void OnAttached()
        {
            base.OnAttached();
            _hookTarget = (AssociatedObject?.GetVisualRoot() as IInputElement) ?? AssociatedObject;
            _hookTarget?.AddHandler(InputElement.PointerReleasedEvent,
                OnPointerReleased,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        protected override void OnDetaching()
        {
            _hookTarget?.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            _hookTarget = null;
            base.OnDetaching();
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Source is not Control src) return;

            var dayBtn = src as CalendarDayButton
                      ?? src.FindAncestorOfType<CalendarDayButton>();
            if (dayBtn is null) return;

            var cmd = Command;
            if (cmd?.CanExecute(null) == true)
                cmd.Execute(null);
        }
    }
}
