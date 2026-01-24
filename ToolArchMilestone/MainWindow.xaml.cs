using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI; // Required for WindowId
using System;
using System.Linq;

namespace ToolArchMilestone
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            // Set initial size
            var appWindow = GetAppWindowForCurrentWindow();
            if (appWindow != null)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32(950, 750));
            }

            // Force Dark Mode
            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = ElementTheme.Dark;
            }
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId myWndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(myWndId);
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavView.MenuItems.Cast<NavigationViewItem>().First();
            Navigate("Launch");
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                // ContentFrame.Navigate(typeof(SettingsPage));
            }
            else
            {
                var selectedItem = (NavigationViewItem)args.SelectedItem;
                if (selectedItem?.Tag is string tag)
                {
                    Navigate(tag);
                }
            }
        }

        private void Navigate(string? tag)
        {
            if (string.IsNullOrEmpty(tag)) return;

            Type? pageType = null;
            switch (tag)
            {
                case "Launch":
                    pageType = typeof(LaunchPage);
                    break;
                case "InProgress":
                    pageType = typeof(InProgressPage);
                    break;
                case "History":
                    pageType = typeof(TerminatePage);
                    break;
            }

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
