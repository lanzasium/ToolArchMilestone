using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Helpers;
using ToolArchMilestone.Core.Models;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.ViewModels
{
    public partial class LaunchViewModel : ObservableObject
    {
        private readonly IJobManager _jobManager;
        private readonly IFilePickerService _filePicker;

        public LaunchViewModel(IJobManager jobManager, IFilePickerService filePicker)
        {
            _jobManager = jobManager;
            _filePicker = filePicker;
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
        [ObservableProperty] private DateTime _startTime = DateTime.Now;
        [ObservableProperty] private DateTime _endTime = DateTime.Now.AddHours(1);
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
        [ObservableProperty] private string? _intervalsText;

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
             }
        }

        [RelayCommand]
        public async Task Launch()
        {
            if (!Validate()) return;

            // Check if we have multiple intervals from text
            var intervals = IntervalParser.ParseContent(IntervalsText ?? "");

            if (intervals.Count > 0)
            {
                foreach(var (start, end) in intervals)
                {
                    await CreateJob(start, end);
                }
            }
            else
            {
                // Use single interval pickers
                await CreateJob(StartTime, EndTime);
            }
        }

        private bool Validate()
        {
            // Basic Validation - in a real app this would likely use INotifyDataErrorInfo
            // but for now we block Launch if critical fields are missing.

            if (IsServer && string.IsNullOrWhiteSpace(ServerAddress)) return false;
            // if (IsServer && string.IsNullOrWhiteSpace(CameraName)) return false; // Maybe optional?

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
