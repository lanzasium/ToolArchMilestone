using Microsoft.UI.Xaml.Controls;
using ToolArchMilestone.Core.ViewModels;

namespace ToolArchMilestone
{
    public sealed partial class TerminatePage : Page
    {
        public HistoryViewModel ViewModel { get; }

        public TerminatePage()
        {
            this.InitializeComponent();
            ViewModel = new HistoryViewModel(App.JobManager!);
        }
    }
}
