using Microsoft.UI.Xaml;
using ToolArchMilestone.Core.Helpers;
using ToolArchMilestone.Core.Services;
using ToolArchMilestone.Services;
using System;
using System.Diagnostics;

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

            #if DEBUG && !DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION
            UnhandledException += (sender, e) =>
            {
                Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Exception}");
                // Break only if attached, but log always
                if (global::System.Diagnostics.Debugger.IsAttached) global::System.Diagnostics.Debugger.Break();
            };
            #endif
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Create Window FIRST to ensure we have a UI thread context fully established
            // and so we can show it even if initialization fails (though we won't have data).
            Window = new MainWindow();

            try
            {
                // Initialize Services
                var dbService = new SqliteDatabaseService("toolarch.db");
                var milestoneService = new MockMilestoneService();

                // Dispatcher must be created on the UI thread
                var dispatcher = new WinUIDispatcherService();

                JobManager = new JobManager(dbService, milestoneService);
                JobManager.SetDispatcher(dispatcher);

                await JobManager.InitializeAsync();

                // Populate Demo Data
                try
                {
                    if ((await dbService.GetAllJobsAsync()).Count == 0)
                    {
                        await DemoDataHelper.Populate(JobManager);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Demo Data Failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"INITIALIZATION FAILED: {ex}");
                // In a real app, we might show a ContentDialog here using the Window.
            }

            Window.Activate();
        }
    }
}
