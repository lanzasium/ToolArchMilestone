using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.ViewModels
{
    public partial class InProgressViewModel : ObservableObject
    {
        private readonly IJobManager _jobManager;

        public ObservableCollection<ArchivingJob> Jobs { get; } = new ObservableCollection<ArchivingJob>();

        [ObservableProperty]
        private string _searchText = "";

        partial void OnSearchTextChanged(string value)
        {
            Refilter();
        }

        public InProgressViewModel(IJobManager jobManager)
        {
            _jobManager = jobManager;
            SyncJobs();

            if (_jobManager.Jobs is ObservableCollection<ArchivingJob> obsJobs)
            {
                obsJobs.CollectionChanged += (s, e) =>
                {
                    if (e.NewItems != null)
                        foreach (ArchivingJob item in e.NewItems) { MonitorJob(item); CheckAndAdd(item); }
                    if (e.OldItems != null)
                        foreach (ArchivingJob item in e.OldItems) Jobs.Remove(item);
                };
            }
        }

        private void SyncJobs()
        {
            foreach(var job in _jobManager.Jobs)
            {
                MonitorJob(job);
                CheckAndAdd(job);
            }
        }

        private void MonitorJob(ArchivingJob job)
        {
             job.PropertyChanged += (s, e) =>
             {
                 if (e.PropertyName == nameof(ArchivingJob.Status))
                 {
                     CheckAndAdd(job);
                 }
             };
        }

        private void Refilter()
        {
            Jobs.Clear();
            foreach(var job in _jobManager.Jobs)
            {
                CheckAndAdd(job);
            }
        }

        private void CheckAndAdd(ArchivingJob job)
        {
             bool shouldBeVisible = (job.Status == JobStatus.Pending || job.Status == JobStatus.Running);

             if (shouldBeVisible && MatchesSearch(job))
             {
                 if (!Jobs.Contains(job)) Jobs.Add(job);
             }
             else
             {
                 if (Jobs.Contains(job)) Jobs.Remove(job);
             }
        }

        private bool MatchesSearch(ArchivingJob job)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            var term = SearchText.Trim().ToLower();

            if (job.Id.ToString().Contains(term)) return true;
            if (job.WorkId?.ToLower().Contains(term) == true) return true;
            if (job.CameraName?.ToLower().Contains(term) == true) return true;
            if (job.CriminalProceeding?.ToLower().Contains(term) == true) return true;
            if (job.RitSpec?.ToLower().Contains(term) == true) return true;
            if (job.Target?.ToLower().Contains(term) == true) return true;

            if (job.StartTime.ToString().Contains(term)) return true;
            if (job.EndTime.ToString().Contains(term)) return true;

            return false;
        }

        [RelayCommand]
        public async Task StopJob(int jobId)
        {
            await _jobManager.CancelJobAsync(jobId);
        }

        [RelayCommand]
        public void MoveUp(int jobId)
        {
            _jobManager.MoveJob(jobId, true);
        }

        [RelayCommand]
        public void MoveDown(int jobId)
        {
            _jobManager.MoveJob(jobId, false);
        }
    }
}
