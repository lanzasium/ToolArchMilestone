using Microsoft.UI.Xaml;
using ToolArchMilestone.Core.Services;
using ToolArchMilestone.Services;
using System;

namespace ToolArchMilestone
{
    public partial class App : Application
    {
        public static Window Window { get; private set; }
        public static JobManager JobManager { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Initialize Services
            var dbService = new SqliteDatabaseService("toolarch.db");
            var milestoneService = new MockMilestoneService();

            // Dispatcher must be created on the UI thread
            var dispatcher = new WinUIDispatcherService();

            JobManager = new JobManager(dbService, milestoneService);
            JobManager.SetDispatcher(dispatcher);

            await JobManager.InitializeAsync();

            Window = new MainWindow();
            Window.Activate();
        }
    }
}
