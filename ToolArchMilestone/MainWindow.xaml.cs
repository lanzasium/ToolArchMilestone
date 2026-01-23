using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

namespace ToolArchMilestone
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            // Force Dark Mode
            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = ElementTheme.Dark;
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            // Select the first item
            NavView.SelectedItem = NavView.MenuItems.Cast<NavigationViewItem>().First();
            // Trigger selection logic manually or let it happen?
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
