using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface IMilestoneService
    {
        Task<bool> ConnectAsync(string serverAddress, string username, string password);
        Task<List<string>> GetCamerasAsync();

        // Returns the path of the generated file
        Task<string> ExportVideoAsync(
            string serverAddress,
            string cameraName,
            DateTime start,
            DateTime end,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken);
    }
}
