using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
            if (e.NewValue is FeedItem article && DataContext is MainViewModel vm)
                vm.OnArticleSelected(article);
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var aboutWindow = new AboutWindow { Owner = this };
                aboutWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening About window: {ex.Message}");
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
    }
}