using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface IJobManager
    {
        IEnumerable<ArchivingJob> Jobs { get; }
        
        Task InitializeAsync();
        Task AddJobAsync(ArchivingJob job);
        Task CancelJobAsync(int jobId);
        Task DeleteJobAsync(int jobId, bool deleteFiles);
        
        void MoveJob(int jobId, bool up);
    }
}
