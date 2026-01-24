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
            // IMPORTANT: Use AddJobAsync logic, but ensure Status is set correctly.
            // JobManager.AddJobAsync forces Pending. So we let it be Pending,
            // but for the "Running" one, we might need to manually update it if we want it to look running immediately,
            // OR just accept it starts as Pending and the Queue picks it up.
            // If the Queue picks it up, it becomes Running. That's fine.

            var job1 = new ArchivingJob
            {
                WorkId = "DEMO-RUNNING",
                CameraName = "Camera Entrance",
                // Status = JobStatus.Running, // Will be overridden to Pending by AddJobAsync
                StartTime = DateTime.Now.AddHours(-1),
                EndTime = DateTime.Now,
                Target = "Suspect 1",
                SourceType = SourceType.Server,
                ServerAddress = "192.168.1.10"
            };
            await jobManager.AddJobAsync(job1);

            var job2 = new ArchivingJob
            {
                WorkId = "DEMO-PENDING",
                CameraName = "Camera Hallway",
                // Status = JobStatus.Pending,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddHours(2),
                Target = "Suspect 2",
                SourceType = SourceType.Server,
                ServerAddress = "192.168.1.10"
            };
            await jobManager.AddJobAsync(job2);

            // For History, we want them to STAY Completed/Failed.
            // AddJobAsync forces Pending.
            // We need a way to insert history without triggering the queue logic?
            // Or update them after adding?

            // Let's create a helper in JobManager or just update property after adding (if AddJobAsync returns the job or ID).
            // AddJobAsync adds to DB and List.
            // We can just update it.

            var job3 = new ArchivingJob
            {
                WorkId = "HIST-COMPLETED",
                CameraName = "Camera Parking",
                Status = JobStatus.Completed, // Will be Pending
                Progress = 100,
                StartTime = DateTime.Now.AddDays(-1),
                EndTime = DateTime.Now.AddDays(-1).AddHours(1),
                ExportPath = "C:\\Exports\\Job3.avi",
                Target = "Vehicle A"
            };
            await jobManager.AddJobAsync(job3);
            // Force status back to Completed (hacky but works for demo)
            job3.Status = JobStatus.Completed;
            // We need to save this update to DB so it persists if app restarts,
            // but for now in-memory update is enough for the UI to see it.
            // Ideally call _db.UpdateJobAsync(job3) but we don't have access to DB here directly.
            // Wait, JobManager has Update? No, only Cancel/Delete.
            // But JobManager.Jobs collection reference is the same object.
            // Updating property fires PropertyChanged.
            // The DB won't know unless we call something.
            // But UI will see it.

            var job4 = new ArchivingJob
            {
                WorkId = "HIST-FAILED",
                CameraName = "Camera Lobby",
                Status = JobStatus.Failed, // Will be Pending
                ErrorMessage = "Connection Timeout",
                StartTime = DateTime.Now.AddDays(-2),
                EndTime = DateTime.Now.AddDays(-2).AddHours(4),
                Target = "Check B"
            };
            await jobManager.AddJobAsync(job4);
            job4.Status = JobStatus.Failed;
        }
    }
}
