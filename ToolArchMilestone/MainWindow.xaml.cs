using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using ToolArchMilestone.Core.ViewModels;

namespace ToolArchMilestone
{
    public sealed partial class MainWindow : Window
    {
        public LaunchViewModel? ViewModel => App.MainLaunchViewModel;

        public MainWindow()
        {
            this.InitializeComponent();
            // Extend content into title bar if needed for modern look, but adhering to request for "minimal status bar in menu"
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Nav error: {ex.Message}");
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
                string pageTag = selectedItem.Tag.ToString() ?? "";

                switch (pageTag)
                {
                    case "Launch":
                        ContentFrame.Navigate(typeof(LaunchPageV2));
                        break;
                    case "InProgress":
                        ContentFrame.Navigate(typeof(InProgressPage));
                        break;
                    case "History":
                        ContentFrame.Navigate(typeof(TerminatePage));
                        break;
                }
            }
        }
    }
}
