using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface IMilestoneService
    {
        Task<bool> ConnectAsync(string serverAddress, string username, string password);
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
