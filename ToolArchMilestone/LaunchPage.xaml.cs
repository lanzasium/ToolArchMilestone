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
            ViewModel = new LaunchViewModel(App.JobManager, filePicker);
        }
    }
}
