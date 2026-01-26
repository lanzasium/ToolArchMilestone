using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.Services
{
    public class SqliteDatabaseService : IDatabaseService
    {
        private readonly string _dbPath;

        public SqliteDatabaseService(string dbName = "toolarch.db")
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(folder, "ToolArchMilestone");
            Directory.CreateDirectory(appFolder);
            _dbPath = Path.Combine(appFolder, dbName);
        }

        public SqliteDatabaseService(string customPath, bool isFullPath) 
        {
            _dbPath = customPath;
        }

        private AppDbContext CreateContext()
        {
            return new AppDbContext(_dbPath);
        }

        public async Task InitializeAsync()
        {
            using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
        }

        public async Task<List<ArchivingJob>> GetAllJobsAsync()
        {
            using var context = CreateContext();
            return await context.Jobs.Include(j => j.Logs).OrderByDescending(j => j.CreatedAt).ToListAsync();
        }

        public async Task<ArchivingJob?> GetJobByIdAsync(int id)
        {
            using var context = CreateContext();
            return await context.Jobs.Include(j => j.Logs).FirstOrDefaultAsync(j => j.Id == id);
        }

        public async Task SaveJobAsync(ArchivingJob job)
        {
            using var context = CreateContext();
            context.Jobs.Add(job);
            await context.SaveChangesAsync();
        }

        public async Task UpdateJobAsync(ArchivingJob job)
        {
            using var context = CreateContext();
            context.Jobs.Update(job);
            await context.SaveChangesAsync();
        }

        public async Task DeleteJobAsync(int id)
        {
            using var context = CreateContext();
            var job = await context.Jobs.FindAsync(id);
            if (job != null)
            {
                context.Jobs.Remove(job);
                await context.SaveChangesAsync();
            }
        }

        public async Task AddLogAsync(LogEntry log)
        {
            using var context = CreateContext();
            context.Logs.Add(log);
            await context.SaveChangesAsync();
        }

        public async Task<List<LogEntry>> GetLogsForJobAsync(int jobId)
        {
            using var context = CreateContext();
            return await context.Logs
                .Where(l => l.JobId == jobId)
                .OrderBy(l => l.Timestamp)
                .ToListAsync();
        }
    }
}
