using Microsoft.UI.Xaml;
using ToolArchMilestone.Core.Services;
using ToolArchMilestone.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;

namespace ToolArchMilestone
{
    public partial class App : Application
    {
        public static Window? Window { get; private set; }
        public static JobManager? JobManager { get; private set; }
        public static Core.Services.Interfaces.IFilePickerService? FilePicker { get; private set; }
        public static Core.Services.Interfaces.IMilestoneService? MilestoneService { get; private set; }
        public static Core.Services.Interfaces.ISettingsService? SettingsService { get; private set; }

        public App()
        {
            this.InitializeComponent();

            UnhandledException += (sender, e) =>
            {
                Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Exception}");
                e.Handled = true; // Prevent crash
            };
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                // Create Window first
                Window = new MainWindow();

                // Initialize services with minimal configuration
                InitializeServices();

                Window.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"INITIALIZATION FAILED: {ex}");

                // Create minimal window even if services fail
                if (Window == null)
                {
                    Window = new MainWindow();
                    Window.Activate();
                }
            }
        }

        private void InitializeServices()
        {
            try
            {
                // Simple in-memory database for testing
                var dbService = new MockDatabaseService();
                MilestoneService = new MilestoneService();
                FilePicker = new WinUIFilePickerService();
                SettingsService = new LocalSettingsService();

                var dispatcher = new WinUIDispatcherService();

                JobManager = new JobManager(dbService, MilestoneService);
                JobManager.SetDispatcher(dispatcher);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SERVICE INITIALIZATION FAILED: {ex}");
                // Services will be null, but app won't crash
            }
        }
    }

    // Temporary mock database service to avoid Entity Framework complexity
    public class MockDatabaseService : Core.Services.Interfaces.IDatabaseService
    {
        private List<ArchivingJob> _jobs = new();
        private List<LogEntry> _logs = new();
        private int _nextId = 1;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<List<ArchivingJob>> GetAllJobsAsync()
        {
            return Task.FromResult(_jobs);
        }

        public Task<ArchivingJob?> GetJobByIdAsync(int id)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == id);
            return Task.FromResult(job);
        }

        public Task SaveJobAsync(ArchivingJob job)
        {
            if (job.Id == 0)
            {
                job.Id = _nextId++;
                _jobs.Add(job);
            }
            return Task.CompletedTask;
        }

        public Task UpdateJobAsync(ArchivingJob job)
        {
            var existing = _jobs.FirstOrDefault(j => j.Id == job.Id);
            if (existing != null)
            {
                var index = _jobs.IndexOf(existing);
                _jobs[index] = job;
            }
            return Task.CompletedTask;
        }

        public Task DeleteJobAsync(int jobId)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null)
            {
                _jobs.Remove(job);
            }
            return Task.CompletedTask;
        }

        public Task AddLogAsync(LogEntry logEntry)
        {
            _logs.Add(logEntry);
            return Task.CompletedTask;
        }

        public Task<List<LogEntry>> GetLogsForJobAsync(int jobId)
        {
            var jobLogs = _logs.Where(l => l.Id == jobId).ToList();
            return Task.FromResult(jobLogs);
        }
    }
}
