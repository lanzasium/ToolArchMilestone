using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services;
using Xunit;

namespace ToolArchMilestone.Tests
{
    public class JobManagerTests
    {
        private readonly JobManager _manager;
        private readonly SqliteDatabaseService _db;

        public JobManagerTests()
        {
            var dbPath = Path.GetTempFileName();
            _db = new SqliteDatabaseService(dbPath, true);
            var milestone = new MockMilestoneService();
            _manager = new JobManager(_db, milestone);
        }

        [Fact]
        public async Task AddJob_ShouldQueueJob_AndAssignId()
        {
            await _manager.InitializeAsync();

            var job = new ArchivingJob
            {
                SourceType = SourceType.Server,
                Status = JobStatus.Pending
            };

            await _manager.AddJobAsync(job);

            Assert.Single(_manager.Jobs);
            var addedJob = _manager.Jobs.First();
            Assert.True(addedJob.Id > 0); // Check auto-increment ID
        }

        [Fact]
        public async Task MoveJob_ShouldReorder()
        {
            await _manager.InitializeAsync();
            var j1 = new ArchivingJob { WorkId = "W1", Status = JobStatus.Pending };
            var j2 = new ArchivingJob { WorkId = "W2", Status = JobStatus.Pending };

            await _manager.AddJobAsync(j1); // ID 1
            await _manager.AddJobAsync(j2); // ID 2

            var id2 = _manager.Jobs.Last().Id;

            // j1 is index 0, j2 is index 1
            _manager.MoveJob(id2, true); // Move j2 Up

            Assert.Equal(id2, _manager.Jobs.First().Id);
        }
    }
}
