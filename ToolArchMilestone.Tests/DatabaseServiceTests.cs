using System;
using System.IO;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services;
using Xunit;

namespace ToolArchMilestone.Tests
{
    public class DatabaseServiceTests
    {
        private readonly string _dbPath;
        private readonly SqliteDatabaseService _service;

        public DatabaseServiceTests()
        {
            _dbPath = Path.GetTempFileName();
            _service = new SqliteDatabaseService(_dbPath, true);
        }

        [Fact]
        public async Task SaveAndGetJob_ShouldWork()
        {
            await _service.InitializeAsync();

            var job = new ArchivingJob
            {
                Target = "Test Target",
                Status = JobStatus.Pending
            };

            await _service.SaveJobAsync(job);

            var savedJob = await _service.GetJobByIdAsync(job.Id);
            Assert.NotNull(savedJob);
            Assert.Equal("Test Target", savedJob.Target);
        }

        [Fact]
        public async Task UpdateJob_ShouldWork()
        {
            await _service.InitializeAsync();
            var job = new ArchivingJob { Target = "Original" };
            await _service.SaveJobAsync(job);

            job.Target = "Updated";
            await _service.UpdateJobAsync(job);

            var updatedJob = await _service.GetJobByIdAsync(job.Id);
            Assert.Equal("Updated", updatedJob.Target);
        }

        [Fact]
        public async Task DeleteJob_ShouldWork()
        {
            await _service.InitializeAsync();
            var job = new ArchivingJob { Target = "DeleteMe" };
            await _service.SaveJobAsync(job);

            await _service.DeleteJobAsync(job.Id);

            var deletedJob = await _service.GetJobByIdAsync(job.Id);
            Assert.Null(deletedJob);
        }
    }
}
