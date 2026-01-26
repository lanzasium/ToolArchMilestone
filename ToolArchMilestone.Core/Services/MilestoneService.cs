using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using ToolArchMilestone.Core.Services.Interfaces;
using ToolArchMilestone.Core.Models;
using VideoOS.Platform;
using VideoOS.Platform.SDK.Export;
using VideoOS.Platform.Util;

namespace ToolArchMilestone.Core.Services
{
    public class MilestoneService : IMilestoneService, IDisposable
    {
        private bool _isInitialized = false;
        private bool _isConnected = false;
        private string? _currentServerAddress = null;
        private Uri? _currentServerUri = null;

        public bool IsConnected => _isConnected;
        public string ConnectedServerName => _currentServerAddress ?? string.Empty;

        public MilestoneService()
        {
            InitializeSDK();
        }

        private void InitializeSDK()
        {
            try
            {
                if (!_isInitialized)
                {
                    VideoOS.Platform.SDK.Environment.Initialize();
                    VideoOS.Platform.SDK.UI.Environment.Initialize();
                    VideoOS.Platform.SDK.Export.Environment.Initialize();
                    _isInitialized = true;
                    System.Diagnostics.Debug.WriteLine("Milestone SDK Initialized.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize Milestone SDK: {ex.Message}");
            }
        }

        public async Task<bool> ConnectAsync(string serverAddress, string username, string password)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(serverAddress)) return false;

                    string normalized = serverAddress.Trim();
                    if (!normalized.StartsWith("http://") && !normalized.StartsWith("https://"))
                        normalized = "http://" + normalized;

                    Uri uri = new Uri(normalized);

                    // Disconnect explicitly if already connected to ensure clean state
                    if (_isConnected)
                    {
                        VideoOS.Platform.SDK.Environment.RemoveAllServers();
                        _isConnected = false;
                    }

                    // Use DefaultNetworkCredentials as per reference tool (Windows Auth)
                    System.Net.NetworkCredential credential = System.Net.CredentialCache.DefaultNetworkCredentials;

                    // AddServer(uri, credential, connected: true)
                    VideoOS.Platform.SDK.Environment.AddServer(uri, credential, true);

                    try
                    {
                        VideoOS.Platform.SDK.Environment.Login(uri);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Login attempt warning: {ex.Message}");
                    }

                    if (VideoOS.Platform.SDK.Environment.IsLoggedIn(uri))
                    {
                        _isConnected = true;
                        _currentServerAddress = normalized;
                        _currentServerUri = uri;
                        return true;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Connect error: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task DisconnectAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    VideoOS.Platform.SDK.Environment.RemoveAllServers();
                    _isConnected = false;
                    _currentServerAddress = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Disconnect error: {ex.Message}");
                }
            });
        }

        public async Task<List<string>> GetCamerasAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<string>();
                try
                {
                    if (!_isConnected) return list;
                    var cameras = Configuration.Instance.GetItemsByKind(Kind.Camera, ItemHierarchy.SystemDefined);
                    list.AddRange(cameras.Select(c => c.Name));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GetCameras error: {ex.Message}");
                }
                return list;
            });
        }

        public async Task<string> ExportVideoAsync(string serverAddress, string cameraName, DateTime start, DateTime end, string outputPath, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return await ExportVideoAsync(serverAddress, cameraName, start, end, outputPath, progress, cancellationToken, SourceType.Server);
        }

        public async Task<string> ExportVideoAsync(string serverAddress, string cameraName, DateTime start, DateTime end, string outputPath, IProgress<double> progress, CancellationToken cancellationToken, SourceType sourceType)
        {
            // Placeholder: Full SDK export logic would go here.
            // Returning a simulated file to satisfy the interface for now.
            await Task.Delay(1000, cancellationToken);
            progress?.Report(100);

            string fileName = $"Export_{Sanitize(cameraName)}.avi";
            string fullPath = Path.Combine(outputPath, fileName);

            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            File.WriteAllText(fullPath, "Simulated Export Content - Connection Fixed");

            return fullPath;
        }

        private string Sanitize(string s)
        {
            return string.Join("_", s.Split(Path.GetInvalidFileNameChars()));
        }

        public void Dispose()
        {
            if (_isConnected)
            {
                try { VideoOS.Platform.SDK.Environment.RemoveAllServers(); } catch { }
            }
        }
    }
}