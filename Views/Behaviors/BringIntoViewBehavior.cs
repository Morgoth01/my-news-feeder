using System;
using System.Windows;
using System.Windows.Threading;

namespace MyNewsFeeder.Views.Behaviors
{
    /// <summary>
    /// Attached behavior to automatically scroll an element into view when enabled.
    /// </summary>
    public static class BringIntoViewBehavior
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(BringIntoViewBehavior),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && e.NewValue is bool enabled && enabled)
            {
                // Delay to ensure layout is ready before bringing into view.
                element.Dispatcher.BeginInvoke(new Action(() => element.BringIntoView()), DispatcherPriority.Background);
            }
        }
    }
}