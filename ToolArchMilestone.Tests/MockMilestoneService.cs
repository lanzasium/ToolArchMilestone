using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Tests
{
    public class MockMilestoneService : IMilestoneService
    {
        public bool IsConnected { get; private set; } = false;
        public string ConnectedServerName { get; private set; } = string.Empty;

        public Task<bool> ConnectAsync(string serverAddress, string username, string password)
        {
            IsConnected = true;
            ConnectedServerName = serverAddress;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<List<string>> GetCamerasAsync()
        {
            return Task.FromResult(new List<string> { "Test Camera 1", "Test Camera 2" });
        }

        public Task<string> ExportVideoAsync(string serverAddress, string cameraName, DateTime start, DateTime end, string outputPath, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return ExportVideoAsync(serverAddress, cameraName, start, end, outputPath, progress, cancellationToken, SourceType.Server);
        }

        public Task<string> ExportVideoAsync(string serverAddress, string cameraName, DateTime start, DateTime end, string outputPath, IProgress<double> progress, CancellationToken cancellationToken, SourceType sourceType)
        {
            return Task.FromResult("mock_export.avi");
        }
    }
}
