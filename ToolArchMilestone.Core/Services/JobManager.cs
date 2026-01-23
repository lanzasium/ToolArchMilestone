using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.Services
{
    public class JobManager : IJobManager
    {
        private readonly IDatabaseService _db;
        private readonly IMilestoneService _milestone;
        private IDispatcherService? _dispatcher;

        public IEnumerable<ArchivingJob> Jobs => _jobs;
        private readonly ObservableCollection<ArchivingJob> _jobs = new ObservableCollection<ArchivingJob>();

        private CancellationTokenSource? _currentJobCts;
        private bool _isProcessing;

        public JobManager(IDatabaseService db, IMilestoneService milestone)
        {
            _db = db;
            _milestone = milestone;
        }

        public void SetDispatcher(IDispatcherService dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async Task InitializeAsync()
        {
            await _db.InitializeAsync();
            var jobs = await _db.GetAllJobsAsync();

            _jobs.Clear();
            foreach (var job in jobs)
            {
                if (job.Status == JobStatus.Running)
                {
                    job.Status = JobStatus.Stopped;
                    job.ErrorMessage = "App restarted.";
                    await _db.UpdateJobAsync(job);
                }
                _jobs.Add(job);
            }

            _ = Task.Run(ProcessQueueLoop);
        }

        public async Task AddJobAsync(ArchivingJob job)
        {
            job.Status = JobStatus.Pending;
            job.CreatedAt = DateTime.Now;

            // Save to DB to get ID
            await _db.SaveJobAsync(job);

            Dispatch(() => _jobs.Add(job));
        }

        public async Task CancelJobAsync(int jobId)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null) return;

            if (job.Status == JobStatus.Running)
            {
                _currentJobCts?.Cancel();
            }
            else if (job.Status == JobStatus.Pending)
            {
                job.Status = JobStatus.Stopped;
                await _db.UpdateJobAsync(job);
            }
        }

        public async Task DeleteJobAsync(int jobId, bool deleteFiles)
        {
             var job = _jobs.FirstOrDefault(j => j.Id == jobId);
             if (job != null)
             {
                 if (deleteFiles)
                 {
                     try
                     {
                         // Safe deletion: Only delete the generated file if tracked.
                         if (!string.IsNullOrEmpty(job.GeneratedFilePath) && System.IO.File.Exists(job.GeneratedFilePath))
                         {
                             System.IO.File.Delete(job.GeneratedFilePath);
                         }

                         // If we want to delete the folder ONLY if it was created for this specific job and is empty?
                         // Too risky for now. Just deleting the file is safer.
                     }
                     catch (Exception ex)
                     {
                         Console.WriteLine($"Delete File Failed: {ex.Message}");
                     }
                 }

                 Dispatch(() => _jobs.Remove(job));
                 await _db.DeleteJobAsync(jobId);
             }
        }

        public void MoveJob(int jobId, bool up)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null || job.Status != JobStatus.Pending) return;

            int oldIndex = _jobs.IndexOf(job);
            int newIndex = up ? oldIndex - 1 : oldIndex + 1;

            if (newIndex >= 0 && newIndex < _jobs.Count)
            {
                Dispatch(() => _jobs.Move(oldIndex, newIndex));
            }
        }

        private void Dispatch(Action action)
        {
            if (_dispatcher != null)
                _dispatcher.TryEnqueue(action);
            else
                action();
        }

        private async Task ProcessQueueLoop()
        {
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                while (true)
                {
                    var nextJob = _jobs.FirstOrDefault(j => j.Status == JobStatus.Pending);

                    if (nextJob == null)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    await RunJob(nextJob);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Queue Loop Failed: {ex}");
                _isProcessing = false;
            }
        }

        private async Task RunJob(ArchivingJob job)
        {
            Dispatch(() =>
            {
                job.Status = JobStatus.Running;
                job.Progress = 0;
            });
            await _db.UpdateJobAsync(job);

            _currentJobCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<double>(p =>
                {
                    Dispatch(() => job.Progress = p);
                });

                if (job.SourceType == SourceType.Server)
                {
                    await _milestone.ConnectAsync(job.ServerAddress ?? "", "", job.Password ?? "");
                    var file = await _milestone.ExportVideoAsync(
                        job.ServerAddress ?? "",
                        job.CameraName ?? "",
                        job.StartTime,
                        job.EndTime,
                        job.ExportPath ?? "",
                        progress,
                        _currentJobCts.Token);

                    job.GeneratedFilePath = file;
                }
                else
                {
                    await Task.Delay(2000, _currentJobCts.Token);
                }

                Dispatch(() =>
                {
                    job.Status = JobStatus.Completed;
                    job.Progress = 100;
                });
            }
            catch (OperationCanceledException)
            {
                Dispatch(() => job.Status = JobStatus.Stopped);
            }
            catch (Exception ex)
            {
                Dispatch(() =>
                {
                    job.Status = JobStatus.Failed;
                    job.ErrorMessage = ex.Message;
                });
                await _db.AddLogAsync(new LogEntry { JobId = job.Id, Level = "Error", Message = ex.Message });
            }
            finally
            {
                await _db.UpdateJobAsync(job);
                _currentJobCts = null;
            }
        }
    }
}
