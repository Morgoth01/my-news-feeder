using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;
using MyNewsFeeder.Views;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WpfApplication = System.Windows.Application;
using WpfWindowState = System.Windows.WindowState;

namespace MyNewsFeeder.Services
{
    public sealed class DesktopNotificationService : IDisposable
    {
        private const int DefaultRetentionHours = 24;
        private const int MaxRetentionHours = 24 * 30;
        private const int MaxStoredNotifications = 200;
        private const string PrimaryToastAppUserModelId = "MyNewsFeeder";
        private const string ToastDisplayName = "My News Feeder";
        private const string ToastShortcutName = "My News Feeder.lnk";
        private const string ToastShortcutDescription = "My News Feeder";
        private const string ToastActivatorClsid = "{D18A73A8-1D9B-4B7B-B9B2-61C4D16E6D4E}";
        private const string ImportantToastGroup = "important";
        private const string ImportantToastTagPrefix = "imp-";
        private static readonly string[] LegacyToastAppUserModelIds =
        {
            "MyNewsFeeder.App",
            "mynewsfeeder.app"
        };
        private static readonly string[] LegacyToastShortcutNames =
        {
            "MyNewsFeeder.lnk",
            "MyNewsFeeder.App.lnk",
            "mynewsfeeder.app.lnk"
        };
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_NONOTIFY = 0x0080;
        private const uint WM_NULL = 0x0000;
        private const uint CommandShowLatestFeeds = 1001;
        private const uint CommandCloseApp = 1002;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid Fmtid;
            public uint Pid;

            public PropertyKey(Guid fmtid, uint pid)
            {
                Fmtid = fmtid;
                Pid = pid;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant : IDisposable
        {
            private ushort _vt;
            private ushort _wReserved1;
            private ushort _wReserved2;
            private ushort _wReserved3;
            private IntPtr _value;
            private IntPtr _value2;

            public static PropVariant FromString(string value)
            {
                return new PropVariant
                {
                    _vt = 31, // VT_LPWSTR
                    _value = Marshal.StringToCoTaskMemUni(value)
                };
            }

            public static PropVariant FromGuid(Guid value)
            {
                var pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>());
                Marshal.StructureToPtr(value, pointer, false);
                return new PropVariant
                {
                    _vt = 72, // VT_CLSID
                    _value = pointer
                };
            }

            public void Dispose()
            {
                _ = PropVariantClear(ref this);
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkComObject
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            uint GetCount(out uint cProps);
            uint GetAt(uint iProp, out PropertyKey pkey);
            uint GetValue(ref PropertyKey key, out PropVariant pv);
            uint SetValue(ref PropertyKey key, ref PropVariant pv);
            uint Commit();
        }

        private static readonly PropertyKey PKEY_AppUserModel_ID =
            new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        private static readonly PropertyKey PKEY_AppUserModel_ToastActivatorCLSID =
            new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 26);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIdNewItem, string lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out NativePoint lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        private NotifyIcon _notifyIcon;
        private readonly SettingsService _settingsService;
        private readonly AppSettings _settings;
        private readonly MenuHostWindow _menuHostWindow;
        private readonly List<ImportantNotificationItem> _recentImportantItems = new List<ImportantNotificationItem>();
        private readonly object _syncRoot = new object();
        private ImportantNotificationsWindow _notificationsWindow;
        private ImportantNotificationItem _pendingBalloonItem;
        private bool _pendingBalloonOpensLatestWindow;
        private string _toastLogoPath;
        private string _toastIdentityIconPath;
        private bool _disposed;
        private int _lastReportedRecentImportantCount = -1;

        public event Action<int> RecentImportantItemCountChanged;

        public DesktopNotificationService(SettingsService settingsService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _settings = LoadSettingsSafe();
            var shouldPersistSettings = false;
            if (_settings.RecentImportantNotifications == null)
            {
                _settings.RecentImportantNotifications = new List<ImportantNotificationItem>();
                shouldPersistSettings = true;
            }

            if (_settings.ImportantNotificationsRetentionHours < 0 ||
                _settings.ImportantNotificationsRetentionHours > MaxRetentionHours)
            {
                _settings.ImportantNotificationsRetentionHours = DefaultRetentionHours;
                shouldPersistSettings = true;
            }

            lock (_syncRoot)
            {
                foreach (var item in _settings.RecentImportantNotifications.Where(IsValidNotificationItem))
                {
                    _recentImportantItems.Add(new ImportantNotificationItem
                    {
                        FeedName = item.FeedName?.Trim() ?? "Feed",
                        Title = item.Title?.Trim() ?? string.Empty,
                        Link = item.Link?.Trim() ?? string.Empty,
                        PublicationDate = item.PublicationDate,
                        ReceivedAt = item.ReceivedAt
                    });
                }
            }

            var pruned = PruneAndPersistRecentItemsIfNeeded();
            if (shouldPersistSettings && !pruned)
            {
                PersistRecentItems();
            }
            NotifyRecentImportantCountChanged(force: true);

            EnsureToastAppRegistration();
            EnsureToastShortcut();
            ValidateToastIdentityRegistration();

            _menuHostWindow = new MenuHostWindow();

            CreateNotifyIcon();

            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        public void ShowImportantArticles(IReadOnlyList<FeedItem> items)
        {
            if (_disposed || items == null || items.Count == 0)
            {
                return;
            }

            RememberLatestItems(items);
            UpdateLatestItemsWindowIfOpen();

            if (items.Count == 1)
            {
                var item = items[0];
                RememberBalloonTarget(item, opensLatestWindow: false);
                var title = string.IsNullOrWhiteSpace(item?.FeedName) ? "Important article" : $"Important: {item.FeedName}";
                var message = string.IsNullOrWhiteSpace(item?.Title) ? "A new important article is available." : item.Title;
                ShowMessage(title, message, link: item?.Link, openLatestWindow: false);
                return;
            }

            RememberBalloonTarget(items[0], opensLatestWindow: true);

            var firstTitles = items
                .Select(item => item?.Title?.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(2)
                .ToList();

            var summary = firstTitles.Count > 0
                ? string.Join(" | ", firstTitles)
                : "Open My News Feeder to review them.";

            ShowMessage("Important articles", $"{items.Count} new matches. {summary}", openLatestWindow: true);
        }

        private void UpdateLatestItemsWindowIfOpen()
        {
            if (_disposed)
            {
                return;
            }

            var snapshot = GetLatestItemsSnapshot();
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || _notificationsWindow == null || !_notificationsWindow.IsLoaded)
                {
                    return;
                }

                _notificationsWindow.SetItems(snapshot);
            }));
        }

        public void RetainOnlyVisibleLinks(IReadOnlyList<FeedItem> visibleItems)
        {
            if (_disposed)
            {
                return;
            }

            var visibleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (visibleItems != null)
            {
                foreach (var item in visibleItems)
                {
                    var link = item?.Link?.Trim();
                    if (!string.IsNullOrWhiteSpace(link))
                    {
                        visibleLinks.Add(link);
                    }
                }
            }

            var removedAny = false;
            lock (_syncRoot)
            {
                removedAny = _recentImportantItems.RemoveAll(existing =>
                {
                    var link = existing?.Link?.Trim();
                    if (string.IsNullOrWhiteSpace(link))
                    {
                        return true;
                    }

                    return !visibleLinks.Contains(link);
                }) > 0;
            }

            if (!removedAny)
            {
                return;
            }

            PersistRecentItems();
            NotifyRecentImportantCountChanged();

            var snapshot = GetLatestItemsSnapshot();
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || _notificationsWindow == null || !_notificationsWindow.IsLoaded)
                {
                    return;
                }

                _notificationsWindow.SetItems(snapshot);
            }));
        }

        public void ShowMessage(
            string title,
            string message,
            int timeoutMs = 5000,
            string link = null,
            bool openLatestWindow = false)
        {
            if (_disposed || _notifyIcon == null)
            {
                return;
            }

            var safeTitle = Trim(title, 60, "My News Feeder");
            var safeMessage = Trim(message, 240, "New notification");

            if (TryShowToastNotification(safeTitle, safeMessage, link, openLatestWindow))
            {
                return;
            }

            WriteNotificationDiagnostic("Toast notification failed. Falling back to NotifyIcon balloon.");
            _notifyIcon.BalloonTipTitle = safeTitle;
            _notifyIcon.BalloonTipText = safeMessage;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(Math.Max(1000, timeoutMs));
        }

        public void ShowLatestImportantFeedsWindow()
        {
            ShowLatestFeedsWindow();
        }

        private static string Trim(string value, int maxLength, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var normalized = value.Trim();
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized.Substring(0, maxLength - 3) + "...";
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowLatestFeedsWindow();
        }

        private void NotifyIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            ImportantNotificationItem pendingItem;
            bool opensLatestWindow;
            lock (_syncRoot)
            {
                pendingItem = _pendingBalloonItem == null
                    ? null
                    : new ImportantNotificationItem
                    {
                        FeedName = _pendingBalloonItem.FeedName,
                        Title = _pendingBalloonItem.Title,
                        Link = _pendingBalloonItem.Link,
                        PublicationDate = _pendingBalloonItem.PublicationDate,
                        ReceivedAt = _pendingBalloonItem.ReceivedAt
                    };
                opensLatestWindow = _pendingBalloonOpensLatestWindow;
                _pendingBalloonItem = null;
                _pendingBalloonOpensLatestWindow = false;
            }

            if (opensLatestWindow)
            {
                ShowLatestFeedsWindow();
                return;
            }

            if (pendingItem == null || string.IsNullOrWhiteSpace(pendingItem.Link))
            {
                ShowLatestFeedsWindow();
                return;
            }

            OpenArticleFromNotification(pendingItem);
        }

        private void NotifyIcon_MouseUp(object sender, MouseEventArgs e)
        {
            if (_disposed || e.Button != MouseButtons.Right)
            {
                return;
            }

            ShowNativeContextMenu();
        }

        private void ShowNativeContextMenu()
        {
            if (_disposed || _menuHostWindow == null || _menuHostWindow.Handle == IntPtr.Zero)
            {
                return;
            }

            if (!GetCursorPos(out var cursorPoint))
            {
                return;
            }

            var menuHandle = CreatePopupMenu();
            if (menuHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                _ = AppendMenu(menuHandle, MF_STRING, (UIntPtr)CommandShowLatestFeeds, "Show latest important feeds");
                _ = AppendMenu(menuHandle, MF_SEPARATOR, UIntPtr.Zero, string.Empty);
                _ = AppendMenu(menuHandle, MF_STRING, (UIntPtr)CommandCloseApp, "Close app");

                var ownerHandle = _menuHostWindow.Handle;
                _ = SetForegroundWindow(ownerHandle);

                var command = TrackPopupMenuEx(
                    menuHandle,
                    TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                    cursorPoint.X,
                    cursorPoint.Y,
                    ownerHandle,
                    IntPtr.Zero);

                _ = PostMessage(ownerHandle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

                if (command == CommandShowLatestFeeds)
                {
                    ShowLatestFeedsWindow();
                }
                else if (command == CommandCloseApp)
                {
                    RequestAppShutdown();
                }
            }
            finally
            {
                _ = DestroyMenu(menuHandle);
            }
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (e.Reason == SessionSwitchReason.SessionUnlock ||
                e.Reason == SessionSwitchReason.ConsoleConnect ||
                e.Reason == SessionSwitchReason.RemoteConnect ||
                e.Reason == SessionSwitchReason.SessionLogon)
            {
                RecreateNotifyIcon();
            }
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            RecreateNotifyIcon();
        }

        private void RequestAppShutdown()
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    WpfApplication.Current?.Shutdown();
                }
                catch
                {
                    // Ignore shutdown errors to avoid blocking the tray thread.
                }
            }));
        }

        private void ShowLatestFeedsWindow()
        {
            if (_disposed)
            {
                return;
            }

            PruneAndPersistRecentItemsIfNeeded();
            var snapshot = GetLatestItemsSnapshot();
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (_notificationsWindow == null || !_notificationsWindow.IsLoaded)
                {
                    _notificationsWindow = new ImportantNotificationsWindow(
                        snapshot,
                        clearAllAction: ClearLatestItems,
                        removeItemAction: RemoveLatestItem,
                        currentRetentionHours: GetRetentionHours(),
                        updateRetentionHoursAction: SetRetentionHours,
                        maxStoredItems: MaxStoredNotifications);
                    if (WpfApplication.Current?.MainWindow != null)
                    {
                        _notificationsWindow.Owner = WpfApplication.Current.MainWindow;
                    }
                    _notificationsWindow.Closed += NotificationsWindow_Closed;
                    _notificationsWindow.Show();
                    return;
                }

                _notificationsWindow.SetItems(snapshot);
                _notificationsWindow.SetRetentionSelection(GetRetentionHours());
                if (_notificationsWindow.WindowState == WpfWindowState.Minimized)
                {
                    _notificationsWindow.WindowState = WpfWindowState.Normal;
                }
                _notificationsWindow.Activate();
            }));
        }

        private void NotificationsWindow_Closed(object sender, EventArgs e)
        {
            if (_notificationsWindow != null)
            {
                _notificationsWindow.Closed -= NotificationsWindow_Closed;
                _notificationsWindow = null;
            }
        }

        private void ClearLatestItems()
        {
            List<ImportantNotificationItem> removedItems;
            lock (_syncRoot)
            {
                removedItems = _recentImportantItems
                    .Select(item => new ImportantNotificationItem
                    {
                        FeedName = item.FeedName,
                        Title = item.Title,
                        Link = item.Link,
                        PublicationDate = item.PublicationDate,
                        ReceivedAt = item.ReceivedAt
                    })
                    .ToList();
                _recentImportantItems.Clear();
            }

            PersistRecentItems();
            NotifyRecentImportantCountChanged();

            if (removedItems.Count == 0)
            {
                ClearImportantToastHistory();
                return;
            }

            foreach (var removedItem in removedItems)
            {
                RemoveImportantToastHistoryEntry(removedItem);
            }

            // Also clear summary toasts ("multiple new important articles") that are not tied to one item.
            ClearImportantToastHistory();
        }

        private void RemoveLatestItem(ImportantNotificationItem item)
        {
            if (item == null)
            {
                return;
            }

            List<ImportantNotificationItem> removedItems = new List<ImportantNotificationItem>();
            lock (_syncRoot)
            {
                var link = item.Link?.Trim();
                Predicate<ImportantNotificationItem> matchPredicate;

                if (!string.IsNullOrWhiteSpace(link))
                {
                    matchPredicate = existing =>
                        string.Equals(existing?.Link?.Trim(), link, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    matchPredicate = existing =>
                        string.Equals(existing?.FeedName?.Trim(), item.FeedName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing?.Title?.Trim(), item.Title?.Trim(), StringComparison.OrdinalIgnoreCase);
                }

                removedItems = _recentImportantItems
                    .Where(existing => matchPredicate(existing))
                    .Select(existing => new ImportantNotificationItem
                    {
                        FeedName = existing.FeedName,
                        Title = existing.Title,
                        Link = existing.Link,
                        PublicationDate = existing.PublicationDate,
                        ReceivedAt = existing.ReceivedAt
                    })
                    .ToList();

                if (removedItems.Count > 0)
                {
                    _ = _recentImportantItems.RemoveAll(existing => matchPredicate(existing));
                }
            }

            PersistRecentItems();
            NotifyRecentImportantCountChanged();

            if (removedItems.Count == 0)
            {
                RemoveImportantToastHistoryEntry(item);
                return;
            }

            foreach (var removedItem in removedItems)
            {
                RemoveImportantToastHistoryEntry(removedItem);
            }
        }

        private void RememberLatestItems(IReadOnlyList<FeedItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                foreach (var source in items)
                {
                    if (source == null)
                    {
                        continue;
                    }

                    var title = source.Title?.Trim();
                    var link = source.Link?.Trim();
                    if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(link))
                    {
                        continue;
                    }

                    var existingIndex = !string.IsNullOrWhiteSpace(link)
                        ? _recentImportantItems.FindIndex(item => string.Equals(item.Link, link, StringComparison.OrdinalIgnoreCase))
                        : _recentImportantItems.FindIndex(item =>
                            string.Equals(item.FeedName, source.FeedName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase));

                    if (existingIndex >= 0)
                    {
                        _recentImportantItems.RemoveAt(existingIndex);
                    }

                    _recentImportantItems.Insert(0, new ImportantNotificationItem
                    {
                        FeedName = source.FeedName?.Trim() ?? "Feed",
                        Title = title ?? string.Empty,
                        Link = link ?? string.Empty,
                        PublicationDate = source.PublicationDate,
                        ReceivedAt = DateTime.Now
                    });
                }

                if (_recentImportantItems.Count > MaxStoredNotifications)
                {
                    _recentImportantItems.RemoveRange(MaxStoredNotifications, _recentImportantItems.Count - MaxStoredNotifications);
                }
            }

            PruneAndPersistRecentItemsIfNeeded();
            NotifyRecentImportantCountChanged();
        }

        private void RememberBalloonTarget(FeedItem item, bool opensLatestWindow)
        {
            lock (_syncRoot)
            {
                if (item == null)
                {
                    _pendingBalloonItem = null;
                    _pendingBalloonOpensLatestWindow = opensLatestWindow;
                    return;
                }

                _pendingBalloonItem = new ImportantNotificationItem
                {
                    FeedName = item.FeedName?.Trim() ?? "Feed",
                    Title = item.Title?.Trim() ?? string.Empty,
                    Link = item.Link?.Trim() ?? string.Empty,
                    PublicationDate = item.PublicationDate,
                    ReceivedAt = DateTime.Now
                };
                _pendingBalloonOpensLatestWindow = opensLatestWindow;
            }
        }

        private void OpenArticleFromNotification(ImportantNotificationItem item)
        {
            RunOnUiThread(() =>
            {
                if (_disposed || item == null || string.IsNullOrWhiteSpace(item.Link))
                {
                    return;
                }

                try
                {
                    MainWindow mainWindow;
                    if (WpfApplication.Current?.MainWindow is MainWindow existingWindow)
                    {
                        mainWindow = existingWindow;
                    }
                    else
                    {
                        mainWindow = new MainWindow();
                        if (WpfApplication.Current != null)
                        {
                            WpfApplication.Current.MainWindow = mainWindow;
                        }
                        mainWindow.Show();
                    }

                    if (!mainWindow.IsVisible)
                    {
                        mainWindow.Show();
                    }

                    if (mainWindow.WindowState == WpfWindowState.Minimized)
                    {
                        mainWindow.WindowState = WpfWindowState.Normal;
                    }

                    mainWindow.Activate();

                    if (mainWindow.DataContext is MainViewModel vm &&
                        vm.TryOpenArticleFromNotification(item.Link, openInSeparateWindow: true))
                    {
                        return;
                    }

                    if (!TryOpenExternalLink(item.Link))
                    {
                        ShowLatestFeedsWindow();
                    }
                }
                catch
                {
                    if (!TryOpenExternalLink(item.Link))
                    {
                        ShowLatestFeedsWindow();
                    }
                }
            });
        }

        private static bool TryOpenExternalLink(string link)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(link))
                {
                    return false;
                }

                if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri))
                {
                    return false;
                }

                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryShowToastNotification(string title, string message, string link, bool openLatestWindow)
        {
            try
            {
                EnsureToastAppRegistration();
                EnsureToastShortcut();

                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message);

                var logoPath = GetOrCreateToastLogoPath();
                if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                {
                    builder.AddAppLogoOverride(new Uri(logoPath));
                }

                var xml = new XmlDocument();
                xml.LoadXml(builder.GetToastContent().GetContent());

                var toast = new ToastNotification(xml)
                {
                    // Keep items in Windows notification center longer instead of disappearing quickly.
                    ExpirationTime = DateTimeOffset.Now.AddDays(7)
                };
                toast.Group = ImportantToastGroup;
                toast.Tag = BuildImportantToastTag(link, title, message);

                if (openLatestWindow)
                {
                    toast.Activated += (_, __) => ShowLatestFeedsWindow();
                }
                else
                {
                    var normalizedLink = link?.Trim();
                    if (!string.IsNullOrWhiteSpace(normalizedLink))
                    {
                        toast.Activated += (_, __) => OpenArticleFromNotification(new ImportantNotificationItem
                        {
                            Link = normalizedLink,
                            ReceivedAt = DateTime.Now
                        });
                    }
                }

                if (TryShowToastCore(toast, out var setting, out var firstFailure))
                {
                    WriteNotificationDiagnostic("Toast notification displayed successfully.");
                    return true;
                }

                if (setting != NotificationSetting.Enabled)
                {
                    WriteNotificationDiagnostic($"Toast notifier is not enabled. Setting={setting}.");
                    return false;
                }

                if (firstFailure is COMException)
                {
                    WriteNotificationDiagnostic("First toast send failed with COMException. Re-registering identity and retrying.", firstFailure);
                    EnsureToastAppRegistration();
                    EnsureToastShortcut();
                    ValidateToastIdentityRegistration();

                    if (TryShowToastCore(toast, out setting, out var secondFailure))
                    {
                        WriteNotificationDiagnostic("Toast notification displayed successfully after retry.");
                        return true;
                    }

                    if (setting != NotificationSetting.Enabled)
                    {
                        WriteNotificationDiagnostic($"Toast notifier is not enabled after retry. Setting={setting}.");
                        return false;
                    }

                    if (secondFailure != null)
                    {
                        WriteNotificationDiagnostic("Toast retry failed.", secondFailure);
                    }

                    return false;
                }

                if (firstFailure != null)
                {
                    WriteNotificationDiagnostic("TryShowToastNotification failed.", firstFailure);
                    return false;
                }

                WriteNotificationDiagnostic("TryShowToastNotification failed without explicit exception details.");
                return false;
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic("TryShowToastNotification failed.", ex);
                return false;
            }
        }

        private bool TryShowToastCore(ToastNotification toast, out NotificationSetting setting, out Exception failure)
        {
            setting = NotificationSetting.Enabled;
            failure = null;
            NotificationSetting localSetting = NotificationSetting.Enabled;
            Exception localFailure = null;

            void ShowAction()
            {
                try
                {
                    var notifier = ToastNotificationManager.CreateToastNotifier(PrimaryToastAppUserModelId);
                    localSetting = notifier.Setting;
                    if (localSetting != NotificationSetting.Enabled)
                    {
                        return;
                    }

                    notifier.Show(toast);
                }
                catch (Exception ex)
                {
                    localFailure = ex;
                }
            }

            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                try
                {
                    dispatcher.Invoke(ShowAction);
                }
                catch (Exception ex)
                {
                    localFailure = ex;
                }
            }
            else
            {
                ShowAction();
            }

            setting = localSetting;
            failure = localFailure;
            return localFailure == null && localSetting == NotificationSetting.Enabled;
        }

        private static string BuildImportantToastTag(string link, string title, string message)
        {
            var seed = !string.IsNullOrWhiteSpace(link)
                ? link.Trim()
                : $"{title?.Trim()}|{message?.Trim()}";

            if (string.IsNullOrWhiteSpace(seed))
            {
                seed = "important";
            }

            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(seed));
            var builder = new StringBuilder(12);
            for (var index = 0; index < 6; index++)
            {
                _ = builder.Append(hash[index].ToString("x2"));
            }

            return ImportantToastTagPrefix + builder;
        }

        private static string BuildImportantToastTitle(ImportantNotificationItem item)
        {
            var feedName = item?.FeedName?.Trim();
            return string.IsNullOrWhiteSpace(feedName)
                ? "Important article"
                : $"Important: {feedName}";
        }

        private void RemoveImportantToastHistoryEntry(ImportantNotificationItem item)
        {
            try
            {
                var tag = BuildImportantToastTag(item?.Link, BuildImportantToastTitle(item), item?.Title);
                var history = ToastNotificationManager.History;
                var removed =
                    TryInvokeToastHistoryMethod(history, "Remove",
                        new[] { typeof(string), typeof(string), typeof(string) },
                        new object[] { tag, ImportantToastGroup, PrimaryToastAppUserModelId }) ||
                    TryInvokeToastHistoryMethod(history, "Remove",
                        new[] { typeof(string), typeof(string) },
                        new object[] { tag, ImportantToastGroup }) ||
                    TryInvokeToastHistoryMethod(history, "Remove",
                        new[] { typeof(string) },
                        new object[] { tag });

                if (!removed)
                {
                    WriteNotificationDiagnostic($"Could not remove toast by tag '{tag}' from notification center.");
                }
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic("RemoveImportantToastHistoryEntry failed.", ex);
            }
        }

        private void ClearImportantToastHistory()
        {
            try
            {
                var history = ToastNotificationManager.History;
                var cleared =
                    TryInvokeToastHistoryMethod(history, "Clear",
                        new[] { typeof(string) },
                        new object[] { PrimaryToastAppUserModelId }) ||
                    TryInvokeToastHistoryMethod(history, "Clear",
                        Type.EmptyTypes,
                        Array.Empty<object>());

                if (!cleared)
                {
                    WriteNotificationDiagnostic("Could not clear app notifications from notification center.");
                }
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic("ClearImportantToastHistory failed.", ex);
            }
        }

        private static bool TryInvokeToastHistoryMethod(object history, string methodName, Type[] parameterTypes, object[] args)
        {
            if (history == null)
            {
                return false;
            }

            var method = history.GetType().GetMethod(methodName, parameterTypes);
            if (method == null)
            {
                return false;
            }

            _ = method.Invoke(history, args);
            return true;
        }

        private void EnsureToastShortcut()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    WriteNotificationDiagnostic("EnsureToastShortcut skipped because executable path is empty.");
                    return;
                }

                var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (string.IsNullOrWhiteSpace(programsPath))
                {
                    WriteNotificationDiagnostic("EnsureToastShortcut skipped because Programs path is empty.");
                    return;
                }

                RemoveLegacyToastShortcuts(programsPath);
                CreateOrUpdateToastShortcut(programsPath, exePath);

                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                if (!string.IsNullOrWhiteSpace(startMenuPath) &&
                    !string.Equals(startMenuPath, programsPath, StringComparison.OrdinalIgnoreCase))
                {
                    RemoveLegacyToastShortcuts(startMenuPath);
                    CreateOrUpdateToastShortcut(startMenuPath, exePath);
                }
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic("EnsureToastShortcut failed.", ex);
            }
        }

        private void CreateOrUpdateToastShortcut(string baseDirectory, string exePath)
        {
            var shortcutPath = Path.Combine(baseDirectory, ToastShortcutName);
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath) ?? baseDirectory);

            var shellLink = (IShellLinkW)new ShellLinkComObject();
            try
            {
                shellLink.SetPath(exePath);
                shellLink.SetArguments(string.Empty);
                shellLink.SetDescription(ToastShortcutDescription);
                shellLink.SetIconLocation(exePath, 0);
                shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);

                try
                {
                    var propertyStore = (IPropertyStore)shellLink;
                    var appId = PropVariant.FromString(PrimaryToastAppUserModelId);
                    var activatorClsid = PropVariant.FromGuid(new Guid(ToastActivatorClsid));
                    try
                    {
                        var appIdKey = PKEY_AppUserModel_ID;
                        _ = propertyStore.SetValue(ref appIdKey, ref appId);
                        var activatorKey = PKEY_AppUserModel_ToastActivatorCLSID;
                        _ = propertyStore.SetValue(ref activatorKey, ref activatorClsid);
                        _ = propertyStore.Commit();
                    }
                    finally
                    {
                        activatorClsid.Dispose();
                        appId.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    WriteNotificationDiagnostic($"Failed to set toast properties on shortcut '{shortcutPath}'.", ex);
                }

                ((IPersistFile)shellLink).Save(shortcutPath, true);
                WriteNotificationDiagnostic($"Toast shortcut written: {shortcutPath}");
            }
            finally
            {
                try
                {
                    if (Marshal.IsComObject(shellLink))
                    {
                        _ = Marshal.FinalReleaseComObject(shellLink);
                    }
                }
                catch
                {
                    // Ignore COM release failures.
                }
            }
        }

        private static void RemoveLegacyToastShortcuts(string programsPath)
        {
            try
            {
                foreach (var legacyName in LegacyToastShortcutNames)
                {
                    var legacyPath = Path.Combine(programsPath, legacyName);
                    if (File.Exists(legacyPath))
                    {
                        File.Delete(legacyPath);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic($"RemoveLegacyToastShortcuts failed for '{programsPath}'.", ex);
            }
        }

        private void EnsureToastAppRegistration()
        {
            try
            {
                var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    WriteNotificationDiagnostic("EnsureToastAppRegistration skipped because executable path is empty.");
                    return;
                }

                RemoveLegacyToastAppRegistrations();

                var iconUri = GetToastIdentityIconUri(executablePath);
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{PrimaryToastAppUserModelId}");
                if (key != null)
                {
                    key.SetValue("DisplayName", ToastDisplayName, RegistryValueKind.String);
                    key.SetValue("IconUri", iconUri, RegistryValueKind.String);
                    key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
                    WriteNotificationDiagnostic($"Toast app registration updated for '{PrimaryToastAppUserModelId}'.");
                }
                else
                {
                    WriteNotificationDiagnostic($"Failed to create registry key for '{PrimaryToastAppUserModelId}'.");
                }
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic("EnsureToastAppRegistration failed.", ex);
            }
        }

        private static void RemoveLegacyToastAppRegistrations()
        {
            try
            {
                foreach (var appId in LegacyToastAppUserModelIds)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        $@"Software\Classes\AppUserModelId\{appId}",
                        throwOnMissingSubKey: false);
                }
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic("RemoveLegacyToastAppRegistrations failed.", ex);
            }
        }

        private string GetToastIdentityIconUri(string fallbackExecutablePath)
        {
            var iconPath = GetOrCreateToastIdentityIconPath();
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                return new Uri(iconPath).AbsoluteUri;
            }

            return new Uri(fallbackExecutablePath).AbsoluteUri;
        }

        private string GetOrCreateToastIdentityIconPath()
        {
            if (!string.IsNullOrWhiteSpace(_toastIdentityIconPath) && File.Exists(_toastIdentityIconPath))
            {
                return _toastIdentityIconPath;
            }

            try
            {
                var iconDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyNewsFeeder");
                Directory.CreateDirectory(iconDir);

                var iconPath = Path.Combine(iconDir, "toast-identity.ico");
                using var icon = ResolveNotificationIcon();
                using var stream = File.Open(iconPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                icon.Save(stream);
                _toastIdentityIconPath = iconPath;
                return _toastIdentityIconPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetOrCreateToastLogoPath()
        {
            if (!string.IsNullOrWhiteSpace(_toastLogoPath) && File.Exists(_toastLogoPath))
            {
                return _toastLogoPath;
            }

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "MyNewsFeeder");
                Directory.CreateDirectory(tempDir);

                var outputPath = Path.Combine(tempDir, "toast-logo.png");
                using var icon = ResolveNotificationIcon();
                using var bitmap = icon.ToBitmap();
                bitmap.Save(outputPath, ImageFormat.Png);
                _toastLogoPath = outputPath;
                return _toastLogoPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void ValidateToastIdentityRegistration()
        {
            try
            {
                var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var shortcutInPrograms = !string.IsNullOrWhiteSpace(programsPath) &&
                                         File.Exists(Path.Combine(programsPath, ToastShortcutName));
                var shortcutInStartMenu = !string.IsNullOrWhiteSpace(startMenuPath) &&
                                          File.Exists(Path.Combine(startMenuPath, ToastShortcutName));

                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\AppUserModelId\{PrimaryToastAppUserModelId}");
                var registered = key != null;
                WriteNotificationDiagnostic(
                    $"Toast identity validation: AUMID='{PrimaryToastAppUserModelId}', registry={registered}, shortcutPrograms={shortcutInPrograms}, shortcutStartMenu={shortcutInStartMenu}.");
            }
            catch (Exception ex)
            {
                WriteNotificationDiagnostic("ValidateToastIdentityRegistration failed.", ex);
            }
        }

        private static void WriteNotificationDiagnostic(string message, Exception ex = null)
        {
            try
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyNewsFeeder",
                    "logs");
                Directory.CreateDirectory(logDirectory);

                var logPath = Path.Combine(logDirectory, "notification-diagnostics.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
                if (ex != null)
                {
                    line += $" Exception={ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}";
                    if (ex.InnerException != null)
                    {
                        line += $" | Inner={ex.InnerException.GetType().Name} (0x{ex.InnerException.HResult:X8}): {ex.InnerException.Message}";
                    }
                }

                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
                // Ignore diagnostics logging failures.
            }
        }

        private AppSettings LoadSettingsSafe()
        {
            try
            {
                return _settingsService?.LoadSettings() ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        private int GetRetentionHours()
        {
            var configuredHours = _settings?.ImportantNotificationsRetentionHours ?? DefaultRetentionHours;
            if (configuredHours == 0)
            {
                return 0;
            }

            if (configuredHours < 0 || configuredHours > MaxRetentionHours)
            {
                return DefaultRetentionHours;
            }

            return configuredHours;
        }

        private void SetRetentionHours(int hours)
        {
            if (_settings == null)
            {
                return;
            }

            var normalized = NormalizeRetentionHours(hours);
            if (_settings.ImportantNotificationsRetentionHours == normalized)
            {
                return;
            }

            _settings.ImportantNotificationsRetentionHours = normalized;
            PersistRecentItems();

            _ = PruneAndPersistRecentItemsIfNeeded();
            if (_notificationsWindow != null && _notificationsWindow.IsLoaded)
            {
                var snapshot = GetLatestItemsSnapshot();
                _ = WpfApplication.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    if (_disposed || _notificationsWindow == null || !_notificationsWindow.IsLoaded)
                    {
                        return;
                    }

                    _notificationsWindow.SetItems(snapshot);
                    _notificationsWindow.SetRetentionSelection(normalized);
                }));
            }
        }

        private static int NormalizeRetentionHours(int hours)
        {
            if (hours == 0)
            {
                return 0;
            }

            if (hours < 0)
            {
                return DefaultRetentionHours;
            }

            if (hours > MaxRetentionHours)
            {
                return MaxRetentionHours;
            }

            return hours;
        }

        private bool PruneAndPersistRecentItemsIfNeeded()
        {
            bool changed;
            lock (_syncRoot)
            {
                changed = PruneRecentItemsLocked();
            }

            if (changed)
            {
                PersistRecentItems();
                NotifyRecentImportantCountChanged();
            }

            return changed;
        }

        public int GetRecentImportantItemCount()
        {
            lock (_syncRoot)
            {
                return _recentImportantItems.Count;
            }
        }

        public List<ImportantNotificationItem> GetRecentImportantItemsSnapshot()
        {
            lock (_syncRoot)
            {
                return _recentImportantItems
                    .Select(item => new ImportantNotificationItem
                    {
                        FeedName = item.FeedName,
                        Title = item.Title,
                        Link = item.Link,
                        PublicationDate = item.PublicationDate,
                        ReceivedAt = item.ReceivedAt
                    })
                    .ToList();
            }
        }

        public int GetConfiguredRetentionHours()
        {
            return GetRetentionHours();
        }

        private void NotifyRecentImportantCountChanged(bool force = false)
        {
            int count;
            lock (_syncRoot)
            {
                count = _recentImportantItems.Count;
            }

            if (!force && count == _lastReportedRecentImportantCount)
            {
                return;
            }

            _lastReportedRecentImportantCount = count;
            var handler = RecentImportantItemCountChanged;
            if (handler == null)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                try
                {
                    handler(count);
                }
                catch
                {
                    // Ignore event listener errors; notification service should keep running.
                }
            });
        }

        private bool PruneRecentItemsLocked()
        {
            var retentionHours = GetRetentionHours();
            var cutoffUtc = retentionHours > 0
                ? DateTime.UtcNow.AddHours(-retentionHours)
                : DateTime.MinValue;
            var removedAny = _recentImportantItems.RemoveAll(item =>
            {
                if (!IsValidNotificationItem(item))
                {
                    return true;
                }

                if (item.ReceivedAt == default)
                {
                    return true;
                }

                if (retentionHours <= 0)
                {
                    return false;
                }

                return item.ReceivedAt.ToUniversalTime() < cutoffUtc;
            }) > 0;

            if (_recentImportantItems.Count > MaxStoredNotifications)
            {
                _recentImportantItems.RemoveRange(MaxStoredNotifications, _recentImportantItems.Count - MaxStoredNotifications);
                removedAny = true;
            }

            return removedAny;
        }

        private void PersistRecentItems()
        {
            if (_settings == null || _settingsService == null)
            {
                return;
            }

            List<ImportantNotificationItem> snapshot;
            lock (_syncRoot)
            {
                snapshot = _recentImportantItems
                    .Where(IsValidNotificationItem)
                    .Select(item => new ImportantNotificationItem
                    {
                        FeedName = item.FeedName?.Trim() ?? "Feed",
                        Title = item.Title?.Trim() ?? string.Empty,
                        Link = item.Link?.Trim() ?? string.Empty,
                        PublicationDate = item.PublicationDate,
                        ReceivedAt = item.ReceivedAt
                    })
                    .ToList();
            }

            try
            {
                _settings.RecentImportantNotifications = snapshot;
                _settingsService.SaveSettings(_settings);
            }
            catch
            {
                // Ignore persistence failures; in-memory list keeps working.
            }
        }

        private static bool IsValidNotificationItem(ImportantNotificationItem item)
        {
            if (item == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(item.Link) || !string.IsNullOrWhiteSpace(item.Title);
        }

        private List<ImportantNotificationItem> GetLatestItemsSnapshot()
        {
            PruneAndPersistRecentItemsIfNeeded();

            lock (_syncRoot)
            {
                return _recentImportantItems
                    .Select(item => new ImportantNotificationItem
                    {
                        FeedName = item.FeedName,
                        Title = item.Title,
                        Link = item.Link,
                        PublicationDate = item.PublicationDate,
                        ReceivedAt = item.ReceivedAt
                    })
                    .ToList();
            }
        }

        private static Icon ResolveNotificationIcon()
        {
            try
            {
                var iconStream = WpfApplication.GetResourceStream(new Uri("pack://application:,,,/Resources/mynewsfeeder.ico"));
                if (iconStream?.Stream != null)
                {
                    using (iconStream.Stream)
                    {
                        using var icon = new Icon(iconStream.Stream);
                        return (Icon)icon.Clone();
                    }
                }
            }
            catch
            {
                // Fall back to executable icon when embedded icon cannot be read.
            }

            try
            {
                var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executablePath))
                {
                    var icon = Icon.ExtractAssociatedIcon(executablePath);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
            }
            catch
            {
                // Fall back to default icon when extraction fails.
            }

            return SystemIcons.Application;
        }

        private void RecreateNotifyIcon()
        {
            RunOnUiThread(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
                    _notifyIcon.BalloonTipClicked -= NotifyIcon_BalloonTipClicked;
                    _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                CreateNotifyIcon();
            });
        }

        private void CreateNotifyIcon()
        {
            if (_disposed)
            {
                return;
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = ResolveNotificationIcon(),
                Visible = true,
                Text = "My News Feeder",
                ContextMenuStrip = null
            };
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
            _notifyIcon.BalloonTipClicked += NotifyIcon_BalloonTipClicked;
            _notifyIcon.MouseUp += NotifyIcon_MouseUp;
        }

        private static void RunOnUiThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher == null)
            {
                action();
                return;
            }

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _ = dispatcher.BeginInvoke(action);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_notificationsWindow != null)
                {
                    _notificationsWindow.Close();
                    _notificationsWindow = null;
                }
            }
            catch
            {
                // Ignore close failures during shutdown.
            }

            try
            {
                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            }
            catch
            {
                // Ignore unsubscribe failures during shutdown.
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
                _notifyIcon.BalloonTipClicked -= NotifyIcon_BalloonTipClicked;
                _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _menuHostWindow?.Dispose();
        }

        private sealed class MenuHostWindow : NativeWindow, IDisposable
        {
            private bool _isDisposed;

            public MenuHostWindow()
            {
                CreateHandle(new CreateParams
                {
                    Caption = "MyNewsFeederTrayMenuHost"
                });
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                DestroyHandle();
            }
        }
    }
}