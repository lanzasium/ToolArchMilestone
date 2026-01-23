using Microsoft.UI.Xaml.Controls;
using ToolArchMilestone.Core.ViewModels;
using ToolArchMilestone.Services;

namespace ToolArchMilestone
{
    public sealed partial class LaunchPage : Page
    {
        public LaunchViewModel ViewModel { get; }

        public LaunchPage()
        {
            this.InitializeComponent();
            // Inject WinUI File Picker
            var filePicker = new WinUIFilePickerService();
            // App.JobManager might be null if accessed before OnLaunched, but LaunchPage is created after.
            // Using ! to suppress warning as we expect it to be initialized.
            ViewModel = new LaunchViewModel(App.JobManager!, filePicker);
        }
    }
}
