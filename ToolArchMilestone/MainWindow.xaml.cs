using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI; // Required for WindowId
using System;
using System.Linq;
using System.Diagnostics;

namespace ToolArchMilestone
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            
            // Set initial size
            try 
            {
                var appWindow = GetAppWindowForCurrentWindow();
                if (appWindow != null)
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32(950, 750));
                }
            }
            catch { /* Ignore resizing errors */ }

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
            try 
            {
                NavView.SelectedItem = NavView.MenuItems.Cast<NavigationViewItem>().First();
                Navigate("Launch");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NAVIGATION FAILED: {ex}");
                // In production, we might show a dialog, but here we just prevent the crash
            }
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
                    try
                    {
                        Navigate(tag);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"NAVIGATION SELECTION FAILED: {ex}");
                    }
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
