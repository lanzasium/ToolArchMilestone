using Microsoft.UI.Xaml.Controls;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.ViewModels;

namespace ToolArchMilestone
{
    public sealed partial class InProgressPage : Page
    {
        public InProgressViewModel ViewModel { get; }

        public InProgressPage()
        {
            this.InitializeComponent();
            ViewModel = new InProgressViewModel(App.JobManager!);
        }

        private void ShowLog_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ArchivingJob job)
            {
                 // Create a flyout or dialog to show logs
                 // For simplicity, let's assume we bind the Flyout in XAML or just show a simple content dialog
            }
        }
    }
}
