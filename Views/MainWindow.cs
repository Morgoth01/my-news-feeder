using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;
using MaterialDesignThemes.Wpf;

namespace MyNewsFeeder.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded; // This method exists below

            // ADDED: Listen for screen resolution changes
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        // ADDED: Missing MainWindow_Loaded method
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("🔧 MainWindow_Loaded started");

            _viewModel = DataContext as MainViewModel;

            if (_viewModel != null)
            {
                System.Diagnostics.Debug.WriteLine("✅ ViewModel found");

                var linkWebView = FindName("linkWebView") as Microsoft.Web.WebView2.Wpf.WebView2;
                if (linkWebView != null)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Found linkWebView control");

                    try
                    {
                        await linkWebView.EnsureCoreWebView2Async();
                        System.Diagnostics.Debug.WriteLine("✅ CoreWebView2 ensured");

                        _viewModel.SetWebView(linkWebView);
                        System.Diagnostics.Debug.WriteLine("✅ SetWebView called");

                        _viewModel.ClearBrowserOnStartup();
                        System.Diagnostics.Debug.WriteLine("✅ Initial navigation started");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error initializing WebView2: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ linkWebView NOT FOUND in XAML!");
                }

                var articleWebView = FindName("articleWebView") as Microsoft.Web.WebView2.Wpf.WebView2;
                if (articleWebView != null)
                {
                    try
                    {
                        await articleWebView.EnsureCoreWebView2Async();
                        _viewModel.SetArticleWebView(articleWebView);
                        System.Diagnostics.Debug.WriteLine("✅ Article WebView initialized");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error initializing Article WebView: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ articleWebView NOT FOUND in XAML!");
                }

                System.Diagnostics.Debug.WriteLine("✅ MainWindow initialization completed");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ ViewModel is NULL!");
            }
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FeedItem feedItem && _viewModel != null)
            {
                System.Diagnostics.Debug.WriteLine($"🔗 Article selected: {feedItem.Title}");

                try
                {
                    _viewModel.OnArticleSelected(feedItem);
                    System.Diagnostics.Debug.WriteLine($"✅ Article selection processed: {feedItem.Link}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error processing article selection: {ex.Message}");
                }
            }
            else if (e.NewValue == null)
            {
                System.Diagnostics.Debug.WriteLine("🔗 Article selection cleared");
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var aboutWindow = new AboutWindow
                {
                    Owner = this
                };
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
                {
                    popupBox.IsPopupOpen = false;
                    System.Diagnostics.Debug.WriteLine("✅ Settings popup closed");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ SettingsPopupBox not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error closing settings popup: {ex.Message}");
            }
        }

        // ADDED: Handle GridSplitter drag completion to save heights
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
                    // Get the main grid
                    var mainGrid = ((FrameworkElement)sender).Parent as Grid;
                    if (mainGrid != null && mainGrid.RowDefinitions.Count >= 3)
                    {
                        // Get actual heights after resize
                        var articleHeight = mainGrid.RowDefinitions[0].ActualHeight;
                        var browserHeight = mainGrid.RowDefinitions[2].ActualHeight;

                        System.Diagnostics.Debug.WriteLine($"GridSplitter drag completed - Article: {articleHeight}, Browser: {browserHeight}");

                        // Update ViewModel properties (which will save to settings and mark as custom)
                        _viewModel.ArticleWindowHeight = articleHeight;
                        _viewModel.BrowserWindowHeight = browserHeight;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling GridSplitter drag: {ex.Message}");
            }
        }

        // ADDED: Handle screen resolution changes
        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
                    System.Diagnostics.Debug.WriteLine("Display settings changed, checking for dynamic resize");
                    // ViewModel will handle dynamic resizing if user hasn't customized
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling display settings change: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // ADDED: Cleanup event handler
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            base.OnClosed(e);
        }
    }
}
