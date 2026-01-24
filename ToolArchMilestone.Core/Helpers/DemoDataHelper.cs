using System;
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
                StartTime = DateTime.Now.AddHours(-1),
                EndTime = DateTime.Now,
                Target = "Suspect 1",
                SourceType = SourceType.Server,
                ServerAddress = "192.168.1.10"
            };
            await jobManager.AddJobAsync(job1);
            // FORCE Running status so it shows up in "In Progress" correctly and distinctively
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
                Target = "Vehicle A"
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
                Target = "Check B"
            };
            await jobManager.AddJobAsync(job4);
            job4.Status = JobStatus.Failed; // Force back
        }
    }
}
