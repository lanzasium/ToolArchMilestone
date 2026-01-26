using System;
using System.IO;
using ToolArchMilestone.Core.Services;
using ToolArchMilestone.Core.ViewModels;
using ToolArchMilestone.Core.Services.Interfaces;
using System.Threading.Tasks;
using Xunit;

namespace ToolArchMilestone.Tests
{
    public class MockFilePicker : IFilePickerService
    {
        public Task<string> PickSingleFileAsync(string[] fileTypes)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<string> PickSingleFolderAsync()
        {
            return Task.FromResult(string.Empty);
        }
    }

    public class ViewModelTests
    {
        [Fact]
        public void LaunchViewModel_ShouldInitialize()
        {
            var dbPath = Path.GetTempFileName();
            var db = new SqliteDatabaseService(dbPath, true);
            var milestone = new MockMilestoneService();
            var manager = new JobManager(db, milestone);
            var picker = new MockFilePicker();
            var settings = new LocalSettingsService();
            var serverMapping = new ServerMappingService();

            var vm = new LaunchViewModel(manager, picker, milestone, serverMapping, settings);

            Assert.NotNull(vm);
            vm.ServerAddress = "127.0.0.1";
            Assert.Equal("127.0.0.1", vm.ServerAddress);
        }
    }
}
