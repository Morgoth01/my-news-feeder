using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyNewsFeeder.Views
{
    internal static class WindowThemeHelper
    {
        private const int DwmwaUseImmersiveDarkModeOld = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCloak = 13;
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x00080000;
        private const uint LwaAlpha = 0x00000002;
        private static readonly DependencyProperty IsPreparedProperty = DependencyProperty.RegisterAttached(
            "IsPrepared",
            typeof(bool),
            typeof(WindowThemeHelper),
            new PropertyMetadata(false));
        private static readonly DependencyProperty IsFirstFramePendingProperty = DependencyProperty.RegisterAttached(
            "IsFirstFramePending",
            typeof(bool),
            typeof(WindowThemeHelper),
            new PropertyMetadata(false));

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmFlush();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr hwnd,
            uint colorKey,
            byte alpha,
            uint flags);

        public static void Apply(Window window, bool useDarkMode)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                ApplyCompositionBackground(window, hwnd, useDarkMode);

                var value = useDarkMode ? 1 : 0;
                if (DwmSetWindowAttribute(
                        hwnd,
                        DwmwaUseImmersiveDarkMode,
                        ref value,
                        Marshal.SizeOf<int>()) != 0)
                {
                    DwmSetWindowAttribute(
                        hwnd,
                        DwmwaUseImmersiveDarkModeOld,
                        ref value,
                        Marshal.SizeOf<int>());
                }
            }
            catch
            {
                // Older Windows versions may not support themed title bars.
            }
        }

        public static void Apply(Window window)
        {
            Apply(window, IsDarkModeEnabled());
        }

        public static void Prepare(Window window)
        {
            PrepareCore(window, null);
        }

        public static void Prepare(Window window, bool useDarkMode)
        {
            PrepareCore(window, useDarkMode);
        }

        public static void ShowPrepared(Window window)
        {
            PrimeForShow(window);
            window.Show();
        }

        public static bool? ShowDialogPrepared(Window window)
        {
            PrimeForShow(window);
            return window.ShowDialog();
        }

        public static bool IsStagingFirstFrame(Window window)
        {
            return window != null && (bool)window.GetValue(IsFirstFramePendingProperty);
        }

        public static void ApplyToOpenWindows(bool useDarkMode)
        {
            if (Application.Current == null)
            {
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                Apply(window, useDarkMode);
            }
        }

        private static void PrepareCore(Window window, bool? useDarkModeOverride)
        {
            if (window == null || (bool)window.GetValue(IsPreparedProperty))
            {
                return;
            }

            window.SetValue(IsPreparedProperty, true);
            window.SetValue(IsFirstFramePendingProperty, true);
            var targetOpacity = window.Opacity > 0 ? window.Opacity : 1d;
            window.Opacity = 0;

            window.SourceInitialized += (_, __) =>
                Apply(window, useDarkModeOverride ?? IsDarkModeEnabled());

            EventHandler revealHandler = null;
            revealHandler = (_, __) =>
            {
                window.ContentRendered -= revealHandler;

                // ContentRendered can still precede MaterialDesign's final resource and
                // composition passes. Keep the HWND transparent for a few frames so its
                // first visible surface is already fully themed.
                var revealTimer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };

                revealTimer.Tick += (_, __) =>
                {
                    revealTimer.Stop();
                    if (window.IsVisible)
                    {
                        window.UpdateLayout();
                        var targetMonitorTimer = new DispatcherTimer(
                            DispatcherPriority.ContextIdle,
                            window.Dispatcher)
                        {
                            Interval = TimeSpan.FromMilliseconds(100)
                        };

                        targetMonitorTimer.Tick += (_, __) =>
                        {
                            targetMonitorTimer.Stop();
                            if (!window.IsVisible)
                            {
                                return;
                            }

                            window.UpdateLayout();
                            SetNativeWindowAlpha(window, targetOpacity);
                            window.Opacity = targetOpacity;

                            try
                            {
                                DwmFlush();
                            }
                            catch
                            {
                            }

                            SetWindowCloaked(window, false);

                            try
                            {
                                DwmFlush();
                            }
                            catch
                            {
                            }

                            if (targetOpacity >= 1d)
                            {
                                ClearNativeLayeredStyle(window);
                            }

                            window.SetValue(IsFirstFramePendingProperty, false);
                        };

                        targetMonitorTimer.Start();
                    }
                };

                revealTimer.Start();
            };

            window.ContentRendered += revealHandler;
        }

        private static void PrimeForShow(Window window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (!(bool)window.GetValue(IsPreparedProperty))
            {
                Prepare(window);
            }

            // Owner, placement and DataContext have already been assigned at this point.
            // Create and theme the native HWND while it is still invisible so Windows
            // cannot compose a light default frame during Show()/ShowDialog().
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            SetNativeWindowAlpha(hwnd, 0);
            SetWindowCloaked(hwnd, true);

            try
            {
                // DWM processes caption/theme changes asynchronously. Flush them while
                // the HWND is still hidden to avoid a light native frame on first show.
                DwmFlush();
            }
            catch
            {
                // DWM may be unavailable in restricted or legacy desktop sessions.
            }
        }

        private static void SetWindowCloaked(Window window, bool isCloaked)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowCloaked(hwnd, isCloaked);
            }
        }

        private static void SetWindowCloaked(IntPtr hwnd, bool isCloaked)
        {
            try
            {
                var value = isCloaked ? 1 : 0;
                DwmSetWindowAttribute(
                    hwnd,
                    DwmwaCloak,
                    ref value,
                    Marshal.SizeOf<int>());
            }
            catch
            {
                // Native alpha and WPF opacity remain available as fallbacks.
            }
        }

        private static void SetNativeWindowAlpha(Window window, double opacity)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetNativeWindowAlpha(hwnd, opacity);
            }
        }

        private static void SetNativeWindowAlpha(IntPtr hwnd, double opacity)
        {
            try
            {
                var extendedStyle = GetWindowLong(hwnd, GwlExStyle);
                if ((extendedStyle & WsExLayered) == 0)
                {
                    SetWindowLong(hwnd, GwlExStyle, extendedStyle | WsExLayered);
                }

                var normalizedOpacity = Math.Max(0d, Math.Min(1d, opacity));
                var alpha = (byte)Math.Round(normalizedOpacity * byte.MaxValue);
                SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha);
            }
            catch
            {
                // WPF opacity remains the fallback if native layered-window APIs fail.
            }
        }

        private static void ClearNativeLayeredStyle(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var extendedStyle = GetWindowLong(hwnd, GwlExStyle);
                if ((extendedStyle & WsExLayered) != 0)
                {
                    SetWindowLong(hwnd, GwlExStyle, extendedStyle & ~WsExLayered);
                }
            }
            catch
            {
            }
        }

        private static bool IsDarkModeEnabled()
        {
            return Application.Current?.MainWindow?.DataContext
                is ViewModels.MainViewModel mainViewModel && mainViewModel.DarkMode;
        }

        private static void ApplyCompositionBackground(Window window, IntPtr hwnd, bool useDarkMode)
        {
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is not { } compositionTarget)
            {
                return;
            }

            var fallback = useDarkMode
                ? Color.FromRgb(0x17, 0x18, 0x1C)
                : Color.FromRgb(0xF5, 0xF6, 0xF8);

            compositionTarget.BackgroundColor = window.Background is SolidColorBrush brush && brush.Color.A == byte.MaxValue
                ? brush.Color
                : fallback;
        }
    }
}