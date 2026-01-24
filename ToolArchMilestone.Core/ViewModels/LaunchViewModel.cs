using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Helpers;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.ViewModels
{
    public partial class LaunchViewModel : ObservableObject
    {
        private readonly IJobManager _jobManager;
        private readonly IFilePickerService _filePicker;
        private readonly ISettingsService _settings;

        // Constructor with Settings injection
        public LaunchViewModel(IJobManager jobManager, IFilePickerService filePicker, ISettingsService settings = null)
        {
            _jobManager = jobManager;
            _filePicker = filePicker;
            _settings = settings ?? new LocalSettingsService(); // Default if not injected

            StartTimeTime = StartTime.TimeOfDay;
            EndTimeTime = EndTime.TimeOfDay;

            // Load Settings
            LoadPinnedSettings();
        }

        private async void LoadPinnedSettings()
        {
            var server = await _settings.GetSettingAsync(nameof(ServerAddress));
            if (server != null) ServerAddress = server;

            var camera = await _settings.GetSettingAsync(nameof(CameraName));
            if (camera != null) CameraName = camera;

            var pass = await _settings.GetSettingAsync(nameof(Password));
            if (pass != null) Password = pass;

            var path = await _settings.GetSettingAsync(nameof(ExportPath));
            if (path != null) ExportPath = path;
        }

        // --- Selection ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsServer))]
        [NotifyPropertyChangedFor(nameof(IsArchive))]
        private SourceType _selectedSourceType = SourceType.Server;

        public bool IsServer => SelectedSourceType == SourceType.Server;
        public bool IsArchive => SelectedSourceType == SourceType.Archive;

        [ObservableProperty]
        private ArchiveType _selectedArchiveType = ArchiveType.Legal;

        // --- Server Fields ---
        [ObservableProperty] private string? _serverAddress = "http://localhost";
        [ObservableProperty] private string? _cameraName;

        [ObservableProperty]
        private DateTime _startTime = DateTime.Today;

        [ObservableProperty]
        private TimeSpan _startTimeTime;

        [ObservableProperty]
        private DateTime _endTime = DateTime.Today;

        [ObservableProperty]
        private TimeSpan _endTimeTime;

        [ObservableProperty] private bool _separateArchive;

        // --- Common Fields ---
        [ObservableProperty] private string? _criminalProceeding;
        [ObservableProperty] private string? _magistrate;
        [ObservableProperty] private string? _ritSpec;
        [ObservableProperty] private string? _workId;
        [ObservableProperty] private string? _target;
        [ObservableProperty] private string? _password;
        [ObservableProperty] private string? _exportPath;

        // --- Text Import ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFileImported))]
        [NotifyPropertyChangedFor(nameof(IsManualIntervalEnabled))]
        private string? _intervalsText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFileImported))]
        private string? _importedFileName;

        public bool IsFileImported => !string.IsNullOrEmpty(ImportedFileName);
        public bool IsManualIntervalEnabled => !IsFileImported;

        // --- Commands ---

        [RelayCommand]
        public void GeneratePassword()
        {
            Password = Guid.NewGuid().ToString().Substring(0, 8);
        }

        [RelayCommand]
        public async Task ImportIntervalsFromFile()
        {
             var filePath = await _filePicker.PickSingleFileAsync(new[] { ".txt" });
             if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
             {
                 var content = await File.ReadAllTextAsync(filePath);
                 IntervalsText = content;
                 ImportedFileName = Path.GetFileName(filePath);
             }
        }

        [RelayCommand]
        public void RemoveImportedFile()
        {
            IntervalsText = null;
            ImportedFileName = null;
        }

        [RelayCommand]
        public async Task PinField(string fieldName)
        {
            string value = "";
            switch(fieldName)
            {
                case nameof(ServerAddress): value = ServerAddress; break;
                case nameof(CameraName): value = CameraName; break;
                case nameof(Password): value = Password; break;
                case nameof(ExportPath): value = ExportPath; break;
            }

            if (!string.IsNullOrEmpty(value))
            {
                await _settings.SaveSettingAsync(fieldName, value);
            }
        }

        [RelayCommand]
        public async Task Launch()
        {
            if (!Validate()) return;

            DateTime start = StartTime.Date + StartTimeTime;
            DateTime end = EndTime.Date + EndTimeTime;

            var intervals = IntervalParser.ParseContent(IntervalsText ?? "");

            if (IsFileImported && intervals.Count > 0)
            {
                foreach(var (s, e) in intervals)
                {
                    await CreateJob(s, e);
                }
            }
            else
            {
                await CreateJob(start, end);
            }
        }

        private bool Validate()
        {
            if (IsServer && string.IsNullOrWhiteSpace(ServerAddress)) return false;
            if (string.IsNullOrWhiteSpace(ExportPath)) return false;
            if (string.IsNullOrWhiteSpace(CriminalProceeding)) return false;
            if (string.IsNullOrWhiteSpace(WorkId)) return false;
            return true;
        }

        private async Task CreateJob(DateTime start, DateTime end)
        {
            var job = new ArchivingJob
            {
                ArchiveType = SelectedArchiveType,
                SourceType = SelectedSourceType,
                ServerAddress = ServerAddress,
                CameraName = CameraName ?? "Unknown Camera",
                StartTime = start,
                EndTime = end,
                CriminalProceeding = CriminalProceeding,
                Magistrate = Magistrate,
                RitSpec = RitSpec,
                WorkId = WorkId,
                Target = Target,
                Password = Password,
                ExportPath = ExportPath
            };

            await _jobManager.AddJobAsync(job);
        }
    }
}
