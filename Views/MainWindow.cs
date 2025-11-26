using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;
using MyNewsFeeder.ViewModels;
using MaterialDesignThemes.Wpf;

namespace MyNewsFeeder.Views
{
    public partial class MainWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(new FeedService(), new SettingsService(), new BrowserService());
            DataContext = _viewModel;
            _viewModel.SelectionRestoreRequested += ViewModel_SelectionRestoreRequested;
            _viewModel.RequestTreeScrollOffset = GetTreeScrollOffset;
            _viewModel.ScrollOffsetRestoreRequested += ViewModel_ScrollOffsetRestoreRequested;

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // dark title‐bar on Win10+
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, Marshal.SizeOf(typeof(int)));

            // initialize webviews
            _viewModel = DataContext as MainViewModel;
            await linkWebView.EnsureCoreWebView2Async();
            await articleWebView.EnsureCoreWebView2Async();
            _viewModel.SetWebView(linkWebView);
            _viewModel.SetArticleWebView(articleWebView);
            _viewModel.ClearBrowserOnStartup();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.SuppressSelectionDuringRefresh)
                {
                    return;
                }

                if (e.NewValue is FeedItem article)
                {
                    vm.OnArticleSelected(article);
                }
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var aboutWindow = new AboutWindow { Owner = this };
                aboutWindow.ShowDialog();
            }
            catch (Exception)
            {
                // Ignore failures when opening the About window.
            }
        }

        private void CloseSettingsPopup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var popupBox = FindName("SettingsPopupBox") as PopupBox;
                if (popupBox != null)
                    popupBox.IsPopupOpen = false;
            }
            catch { }
        }

        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            try
            {
                if (_viewModel != null &&
                    ((FrameworkElement)sender).Parent is Grid mainGrid &&
                    mainGrid.RowDefinitions.Count >= 3)
                {
                    _viewModel.ArticleWindowHeight = mainGrid.RowDefinitions[0].ActualHeight;
                    _viewModel.BrowserWindowHeight = mainGrid.RowDefinitions[2].ActualHeight;
                }
            }
            catch { }
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            // handled in ViewModel
        }

        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            base.OnClosed(e);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SaveCategoryExpandedStates();
                vm.SaveFeedExpandedStates();
            }
        }

        private void TreeViewItem_ExpandedCollapsed(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, e.OriginalSource) && DataContext is MainViewModel vm)
            {
                vm.SaveCategoryExpandedStates();
                vm.SaveFeedExpandedStates();
            }
        }

        private void ViewModel_SelectionRestoreRequested(string linkToRestore)
        {
            if (string.IsNullOrWhiteSpace(linkToRestore) || FeedTreeView == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    FeedTreeView.UpdateLayout();

                    foreach (var categoryObj in FeedTreeView.Items)
                    {
                        if (categoryObj is CategoryGroupViewModel category)
                        {
                            var catContainer = FeedTreeView.ItemContainerGenerator.ContainerFromItem(category) as TreeViewItem;
                            catContainer?.UpdateLayout();
                            catContainer?.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);

                            if (category.Feeds == null) continue;
                            foreach (var feed in category.Feeds)
                            {
                                var feedContainer = catContainer?.ItemContainerGenerator.ContainerFromItem(feed) as TreeViewItem;
                                feedContainer?.UpdateLayout();
                                feedContainer?.SetCurrentValue(TreeViewItem.IsExpandedProperty, feed.IsExpanded);

                                if (feed.Items == null) continue;
                                foreach (var article in feed.Items)
                                {
                                    if (string.IsNullOrWhiteSpace(article.Link) ||
                                        !string.Equals(article.Link.Trim(), linkToRestore.Trim(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    var articleContainer = feedContainer?.ItemContainerGenerator.ContainerFromItem(article) as TreeViewItem;
                                    if (articleContainer != null)
                                    {
                                        articleContainer.IsSelected = true;
                                    }
                                    return;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore selection restore failures; user selection remains unchanged.
                }
            }), DispatcherPriority.Background);
        }

        private double GetTreeScrollOffset()
        {
            var sv = FindScrollViewer(FeedTreeView);
            return sv?.VerticalOffset ?? 0;
        }

        private void ViewModel_ScrollOffsetRestoreRequested(double offset)
        {
            if (FeedTreeView == null)
            {
                return;
            }

            void ApplyOffset()
            {
                try
                {
                    FeedTreeView.UpdateLayout();
                    var sv = FindScrollViewer(FeedTreeView);
                    if (sv != null)
                    {
                        sv.ScrollToVerticalOffset(offset);
                    }
                }
                catch
                {
                    // Ignore scrolling failures.
                }
            }

            // Apply once after current layout work, then once more after render to fight virtualization resets.
            Dispatcher.BeginInvoke(new Action(ApplyOffset), DispatcherPriority.ContextIdle);
            Dispatcher.BeginInvoke(new Action(ApplyOffset), DispatcherPriority.ApplicationIdle);
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            if (root is ScrollViewer viewer) return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }

            return null;
        }
    }
}