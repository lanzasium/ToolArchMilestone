using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.Helpers
{
    public static class DemoDataHelper
    {
        public static async Task Populate(IJobManager jobManager)
        {
            // Populate In Progress

            var job1 = new ArchivingJob
            {
                WorkId = "DEMO-RUNNING",
                CameraName = "Camera Entrance",
                Status = JobStatus.Running, // Will be overridden to Pending by AddJobAsync
                Progress = 45,
                StartTime = DateTime.Now.AddHours(-1),
                EndTime = DateTime.Now,
                Target = "Suspect 1",
                SourceType = SourceType.Server,
                ServerAddress = "192.168.1.10",
                Logs = new List<LogEntry>
                {
                    new LogEntry { Timestamp = DateTime.Now.AddMinutes(-10), Message = "Connection established." },
                    new LogEntry { Timestamp = DateTime.Now.AddMinutes(-5), Message = "Export started." }
                }
            };
            await jobManager.AddJobAsync(job1);

            // Explicitly set Running so it appears in the tab, but do NOT let the loop pick it up as "Pending".
            // Since AddJobAsync sets Pending, we update it here.
            // The JobManager loop ignores "Running" jobs (it only picks "Pending").
            // So this job will sit at "Running" state indefinitely, which is perfect for a UI demo.
            job1.Status = JobStatus.Running;

            var job2 = new ArchivingJob
            {
                WorkId = "DEMO-PENDING",
                CameraName = "Camera Hallway",
                Status = JobStatus.Pending,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddHours(2),
                Target = "Suspect 2",
                SourceType = SourceType.Server,
                ServerAddress = "192.168.1.10"
            };
            await jobManager.AddJobAsync(job2);

            // Populate History

            var job3 = new ArchivingJob
            {
                WorkId = "HIST-COMPLETED",
                CameraName = "Camera Parking",
                Status = JobStatus.Completed,
                Progress = 100,
                StartTime = DateTime.Now.AddDays(-1),
                EndTime = DateTime.Now.AddDays(-1).AddHours(1),
                ExportPath = "C:\\Exports\\Job3.avi",
                Target = "Vehicle A",
                Logs = new List<LogEntry>
                {
                    new LogEntry { Timestamp = DateTime.Now.AddDays(-1), Message = "Export completed successfully." }
                }
            };
            await jobManager.AddJobAsync(job3);
            job3.Status = JobStatus.Completed; // Force back after AddJobAsync resets to Pending

            var job4 = new ArchivingJob
            {
                WorkId = "HIST-FAILED",
                CameraName = "Camera Lobby",
                Status = JobStatus.Failed,
                ErrorMessage = "Connection Timeout",
                StartTime = DateTime.Now.AddDays(-2),
                EndTime = DateTime.Now.AddDays(-2).AddHours(4),
                Target = "Check B",
                Logs = new List<LogEntry>
                {
                    new LogEntry { Timestamp = DateTime.Now.AddDays(-2), Message = "Error: Connection timed out after 3 retries.", Level = "Error" }
                }
            };
            await jobManager.AddJobAsync(job4);
            job4.Status = JobStatus.Failed; // Force back
        }
    }
}
