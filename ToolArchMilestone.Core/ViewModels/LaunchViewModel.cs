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
        [ObservableProperty] private string? _serverAddress;
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
            if (string.IsNullOrWhiteSpace(ExportPath)) return; // Simple Validation

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

        private async Task CreateJob(DateTime start, DateTime end)
        {
            var job = new ArchivingJob
            {
                ArchiveType = SelectedArchiveType,
                SourceType = SelectedSourceType,
                ServerAddress = ServerAddress,
                CameraName = CameraName,
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
