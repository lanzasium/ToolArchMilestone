using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface IMilestoneService
    {
        bool IsConnected { get; }
        string ConnectedServerName { get; }

        Task<bool> ConnectAsync(string serverAddress, string username, string password);
        Task DisconnectAsync();
        Task<List<string>> GetCamerasAsync();

        // Returns the path of the generated file - Server mode
        Task<string> ExportVideoAsync(
            string serverAddress,
            string cameraName,
            DateTime start,
            DateTime end,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken);

        // Returns the path of the generated file - With source type support
        Task<string> ExportVideoAsync(
            string serverAddress,
            string cameraName,
            DateTime start,
            DateTime end,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken,
            SourceType sourceType);
    }
}
