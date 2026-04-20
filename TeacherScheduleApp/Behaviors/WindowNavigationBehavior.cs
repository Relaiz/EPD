using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using TeacherScheduleApp.ViewModels;

namespace TeacherScheduleApp.Behaviors
{
    public sealed class WindowNavigationBehavior : Behavior<Window>
    {
        protected override void OnAttached()
        {
            base.OnAttached();

            if (AssociatedObject == null)
                return;

            AssociatedObject.Opened += OnOpened;
            AssociatedObject.KeyDown += OnKeyDown;
        }

        protected override void OnDetaching()
        {
            if (AssociatedObject != null)
            {
                AssociatedObject.Opened -= OnOpened;
                AssociatedObject.KeyDown -= OnKeyDown;
            }

            base.OnDetaching();
        }

        private void OnOpened(object? sender, System.EventArgs e)
        {
            AssociatedObject?.Focus();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (AssociatedObject?.DataContext is not MainWindowViewModel vm)
                return;

            if (ShouldIgnoreKey(e.Source))
                return;

            if (e.Key is Key.Left or Key.Right or Key.Home)
            {
                vm.HandleNavigationKey(e.Key, e.KeyModifiers);
                e.Handled = true;
            }
        }

        private static bool ShouldIgnoreKey(object? source)
        {
            if (source is TextBox)
                return true;

            if (source is Visual visual)
            {
                return visual
                    .GetSelfAndVisualAncestors()
                    .OfType<TextBox>()
                    .Any();
            }

            return false;
        }
    }
}