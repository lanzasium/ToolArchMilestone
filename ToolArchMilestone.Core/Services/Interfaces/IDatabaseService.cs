using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface IDatabaseService
    {
        Task InitializeAsync();
        Task<List<ArchivingJob>> GetAllJobsAsync();
        Task<ArchivingJob?> GetJobByIdAsync(int id);
        Task SaveJobAsync(ArchivingJob job);
        Task UpdateJobAsync(ArchivingJob job);
        Task DeleteJobAsync(int id);
        
        Task AddLogAsync(LogEntry log);
        Task<List<LogEntry>> GetLogsForJobAsync(int jobId);
    }
}
