using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Helpers;
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

        // Job counter for Public ID generation
        private int _jobCounterValue = 0;
        private int _jobCounterSuffixIndex = -1;

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

            // Initialize counters based on existing jobs?
            // Ideally we should save this state in settings, but for now let's just ensure uniqueness locally if possible
            // or reset daily. The legacy code saves it in settings. I'll implement a simple generator.

            _ = Task.Run(ProcessQueueLoop);
        }

        public async Task AddJobAsync(ArchivingJob job)
        {
            job.Status = JobStatus.Pending;
            job.CreatedAt = DateTime.Now;
            job.PublicJobId = GenerateNextPublicId();

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

                         // If folder was created specifically for this export, consider deleting it?
                         // Legacy code deletes the folder if confirmed.
                         // For now, let's stick to deleting the generated file.
                         if (!string.IsNullOrEmpty(job.ExportPath) && System.IO.Directory.Exists(job.ExportPath))
                         {
                             // Only delete if empty or force?
                             // Be careful. For now, we won't delete the whole folder.
                         }
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
                // Only swap with other pending jobs ideally
                var otherJob = _jobs[newIndex];
                if (otherJob.Status == JobStatus.Pending)
                {
                    Dispatch(() => _jobs.Move(oldIndex, newIndex));
                }
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
                    // Simple queue processing: first pending job
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
                job.StartedAt = DateTime.Now;
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
                    // Legacy: Check connection first
                    // await _milestone.ConnectAsync(job.ServerAddress ?? "", "", job.Password ?? ""); // Assuming already connected or connect now

                    // Actually, connect logic is usually pre-launch. But we can ensure connection here.

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
                    // Archive Mode (Copy packages)
                    // Mocking the copy process
                    int totalFiles = 20; // Mock
                    for (int i = 0; i <= totalFiles; i++)
                    {
                        _currentJobCts.Token.ThrowIfCancellationRequested();
                        await Task.Delay(100, _currentJobCts.Token); // Simulate file copy
                        double pct = (double)i / totalFiles * 100;
                        Dispatch(() => job.Progress = pct);
                    }

                    job.GeneratedFilePath = System.IO.Path.Combine(job.ExportPath ?? "", "MockArchiveResult.txt");
                    if (!System.IO.File.Exists(job.GeneratedFilePath))
                        await System.IO.File.WriteAllTextAsync(job.GeneratedFilePath, "Mock Archive Copy Complete");
                }

                DoPostProcessingForJob(job);

                Dispatch(() =>
                {
                    job.Status = JobStatus.Completed;
                    job.Progress = 100;
                    job.CompletedAt = DateTime.Now;

                    if (job.StartedAt.HasValue)
                    {
                        var duration = job.CompletedAt.Value - job.StartedAt.Value;
                        job.Duration = duration.ToString(@"hh\:mm\:ss");
                    }

                    // Update size mock
                    job.Size = "1.2 GB"; // Mock
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

        private string GenerateNextPublicId()
        {
            _jobCounterValue++;
            if (_jobCounterValue > 999)
            {
                _jobCounterValue = 1;
                _jobCounterSuffixIndex++;
            }

            string suffix = "";
            if (_jobCounterSuffixIndex >= 0)
            {
                // Convert index to 'a', 'b'... 'z', 'aa', etc.
                int index = _jobCounterSuffixIndex;
                while (index >= 0)
                {
                    int remainder = index % 26;
                    suffix = (char)('a' + remainder) + suffix;
                    index = index / 26 - 1;
                }
            }

            return $"{_jobCounterValue}{suffix}";
        }

        private void DoPostProcessingForJob(ArchivingJob job)
        {
            if (string.IsNullOrEmpty(job.ExportPath) || !System.IO.Directory.Exists(job.ExportPath)) return;

            try
            {
                // Structure:
                // ExportPath/
                //   Client Files/
                //     Data/ (Moved from root Data)
                //     Client/ (Copied from resources)
                //     Project.scp (Moved from root)
                //   SmartClient-Player.exe (Copied from resources)

                string clientFiles = System.IO.Path.Combine(job.ExportPath, "Client Files");
                System.IO.Directory.CreateDirectory(clientFiles);

                // 1. Move Data folder
                string sourceData = System.IO.Path.Combine(job.ExportPath, "Data");
                string destData = System.IO.Path.Combine(clientFiles, "Data");
                if (System.IO.Directory.Exists(sourceData))
                {
                    // Basic move or copy
                    if (!System.IO.Directory.Exists(destData))
                    {
                        System.IO.Directory.Move(sourceData, destData);
                    }
                }

                // 2. Move Project.scp
                string sourceProject = System.IO.Path.Combine(job.ExportPath, "Project.scp");
                string destProject = System.IO.Path.Combine(clientFiles, "Project.scp");
                if (System.IO.File.Exists(sourceProject))
                {
                    System.IO.File.Move(sourceProject, destProject, true);
                }

                // 3. Copy SmartClient-Player.exe (Mock source)
                string playerSource = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ClientPayload", "SmartClient-Player.exe");
                if (System.IO.File.Exists(playerSource))
                {
                    System.IO.File.Copy(playerSource, System.IO.Path.Combine(job.ExportPath, "SmartClient-Player.exe"), true);
                }
                else
                {
                    // Create dummy if not found for testing
                    System.IO.File.WriteAllText(System.IO.Path.Combine(job.ExportPath, "SmartClient-Player.exe"), "Mock Player Executable");
                }

                // 4. Copy Client folder (Mock source)
                string clientSource = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ClientPayload", "Client");
                string clientDest = System.IO.Path.Combine(clientFiles, "Client");
                if (System.IO.Directory.Exists(clientSource))
                {
                    // Mock recursive copy
                    System.IO.Directory.CreateDirectory(clientDest);
                    // Copy logic omitted for brevity in mock, just creating folder
                }
                else
                {
                    System.IO.Directory.CreateDirectory(clientDest);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Post-processing failed: {ex.Message}");
                // Log error but don't fail job?
                Dispatch(() => job.ErrorMessage = $"Post-processing warning: {ex.Message}");
            }
        }
    }
}
