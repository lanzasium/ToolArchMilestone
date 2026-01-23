using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.Services
{
    public class MockMilestoneService : IMilestoneService
    {
        private bool _isConnected = false;

        public async Task<bool> ConnectAsync(string serverAddress, string username, string password)
        {
            await Task.Delay(1000);
            _isConnected = true;
            return true;
        }

        public async Task<List<string>> GetCamerasAsync()
        {
            if (!_isConnected) throw new InvalidOperationException("Not connected to Milestone server.");

            await Task.Delay(500);
            return new List<string>
            {
                "Camera 01 - Entrance",
                "Camera 02 - Hallway",
                "Camera 03 - Parking",
                "Camera 04 - Server Room"
            };
        }

        public async Task<string> ExportVideoAsync(
            string serverAddress,
            string cameraName,
            DateTime start,
            DateTime end,
            string outputPath,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            int steps = 20;
            for (int i = 0; i <= steps; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(200, cancellationToken);
                double percent = (double)i / steps * 100;
                progress?.Report(percent);
            }

            try
            {
                if (!System.IO.Directory.Exists(outputPath))
                {
                     System.IO.Directory.CreateDirectory(outputPath);
                }

                var fileName = $"Export_{cameraName?.Replace(" ", "_") ?? "Unknown"}_{DateTime.Now:yyyyMMddHHmmss}.txt";
                var fullPath = System.IO.Path.Combine(outputPath, fileName);
                await System.IO.File.WriteAllTextAsync(fullPath, "This is a mock video export file.");
                return fullPath;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
