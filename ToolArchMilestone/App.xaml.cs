using Microsoft.UI.Xaml;
using ToolArchMilestone.Core.Helpers;
using ToolArchMilestone.Core.Services;
using ToolArchMilestone.Services;
using System;

namespace ToolArchMilestone
{
    public partial class App : Application
    {
        // Allow null initially, but they are set in OnLaunched.
        public static Window? Window { get; private set; }
        public static JobManager? JobManager { get; private set; }

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

            // Populate Demo Data (Only if DB is empty?)
            if ((await dbService.GetAllJobsAsync()).Count == 0)
            {
                await DemoDataHelper.Populate(JobManager);
            }

            Window = new MainWindow();
            Window.Activate();
        }
    }
}
