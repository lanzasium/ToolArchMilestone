using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
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
        private readonly ISettingsService _settings;
        private readonly IMilestoneService _milestone;

        public LaunchViewModel(IJobManager jobManager, IFilePickerService filePicker, IMilestoneService milestone, ISettingsService settings = null)
        {
            _jobManager = jobManager;
            _filePicker = filePicker;
            _milestone = milestone;
            _settings = settings ?? new LocalSettingsService();

            // Sync Strings
            UpdateStartString();
            UpdateEndString();

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

        public bool IsServer
        {
            get => SelectedSourceType == SourceType.Server;
            set
            {
                if (value && SelectedSourceType != SourceType.Server)
                {
                    SelectedSourceType = SourceType.Server;
                }
            }
        }

        public bool IsArchive
        {
            get => SelectedSourceType == SourceType.Archive;
            set
            {
                if (value && SelectedSourceType != SourceType.Archive)
                {
                    SelectedSourceType = SourceType.Archive;
                }
            }
        }

        [ObservableProperty]
        private ArchiveType _selectedArchiveType = ArchiveType.Legal;

        // --- Server Fields ---
        [ObservableProperty]
        private string? _serverAddress = "http://localhost";

        partial void OnServerAddressChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                // Basic normalization on change, but full normalization on connect
            }
        }

        [ObservableProperty]
        private string? _cameraName;

        // Internal Dates
        [ObservableProperty] private DateTime _startTime = DateTime.Now;
        [ObservableProperty] private DateTime _endTime = DateTime.Now.AddHours(1);
        [ObservableProperty] private TimeSpan _startTimeTime = DateTime.Now.TimeOfDay;
        [ObservableProperty] private TimeSpan _endTimeTime = DateTime.Now.AddHours(1).TimeOfDay;

        // String Bindings for UI
        [ObservableProperty]
        private string _startDateString;

        partial void OnStartDateStringChanged(string value)
        {
            var dt = DateHelper.ParseDateText(value);
            if (dt != default)
            {
                StartTime = dt.Date;
                StartTimeTime = dt.TimeOfDay;
            }
        }

        [ObservableProperty]
        private string _endDateString;

        partial void OnEndDateStringChanged(string value)
        {
            var dt = DateHelper.ParseDateText(value);
            if (dt != default)
            {
                EndTime = dt.Date;
                EndTimeTime = dt.TimeOfDay;
            }
        }

        // Called when Pickers change
        public void UpdateStartString()
        {
            var dt = StartTime.Date + StartTimeTime;
            StartDateString = dt.ToString("dd/MM/yyyy HH:mm");
        }

        public void UpdateEndString()
        {
            var dt = EndTime.Date + EndTimeTime;
            EndDateString = dt.ToString("dd/MM/yyyy HH:mm");
        }

        [ObservableProperty] private bool _separateArchive;

        // --- Common Fields ---
        [ObservableProperty] private string? _criminalProceeding;
        [ObservableProperty] private string? _magistrate;
        [ObservableProperty] private string? _ritSpec;
        [ObservableProperty] private string? _procura;
        [ObservableProperty] private string? _workId;
        [ObservableProperty] private string? _target;
        [ObservableProperty] private string? _password;
        [ObservableProperty] private string? _exportPath;
        [ObservableProperty] private string? _note;

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

        [ObservableProperty]
        private string? _statusMessage;

        [ObservableProperty]
        private bool _isConnected;

        // --- Commands ---

        [RelayCommand]
        public void GeneratePassword()
        {
            Password = SecurityHelper.GenerateRandomPassword(8);
        }

        [RelayCommand]
        public async Task Connect()
        {
            if (string.IsNullOrWhiteSpace(ServerAddress))
            {
                StatusMessage = "Indirizzo server mancante.";
                return;
            }

            string normalized = TextHelper.NormalizeServerAddress(ServerAddress);
            ServerAddress = normalized;

            try
            {
                StatusMessage = "Connessione in corso...";
                IsConnected = await _milestone.ConnectAsync(ServerAddress, "", "");
                StatusMessage = IsConnected ? "Connesso." : "Connessione fallita.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore: {ex.Message}";
                IsConnected = false;
            }
        }

        [RelayCommand]
        public async Task SelectCamera()
        {
            if (!IsConnected)
            {
                StatusMessage = "Non connesso al server.";
                return;
            }

            try
            {
                var cameras = await _milestone.GetCamerasAsync();
                if (cameras != null && cameras.Count > 0)
                {
                    // For PoC: Select the first camera.
                    // In a full implementation, this would open a dialog with the list.
                    CameraName = cameras[0];
                    StatusMessage = $"Telecamera selezionata: {CameraName}";
                }
                else
                {
                    StatusMessage = "Nessuna telecamera trovata.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore selezione camera: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task BrowseFolder()
        {
            var folder = await _filePicker.PickSingleFolderAsync();
            if (!string.IsNullOrEmpty(folder))
            {
                ExportPath = folder;
            }
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

                 // Validate intervals immediately
                 var intervals = DateHelper.ParseIntervalsFromText(content, out var errors);
                 if (intervals.Count > 0)
                 {
                     StatusMessage = $"Caricati {intervals.Count} intervalli.";
                 }
                 else
                 {
                     StatusMessage = "Nessun intervallo valido trovato.";
                 }
             }
        }

        [RelayCommand]
        public void RemoveImportedFile()
        {
            IntervalsText = null;
            ImportedFileName = null;
            StatusMessage = "File intervalli rimosso.";
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
                StatusMessage = $"{fieldName} salvato come predefinito.";
            }
        }

        [RelayCommand]
        public async Task Launch()
        {
            if (!Validate()) return;

            // Normalize fields
            if (string.IsNullOrWhiteSpace(Procura)) Procura = "Procura di..."; // Or leave empty

            DateTime start = StartTime.Date + StartTimeTime;
            DateTime end = EndTime.Date + EndTimeTime;

            List<IntervalRange> intervals = new List<IntervalRange>();

            if (IsFileImported && !string.IsNullOrWhiteSpace(IntervalsText))
            {
                intervals = DateHelper.ParseIntervalsFromText(IntervalsText, out var errors);
                if (errors.Count > 0)
                {
                    StatusMessage = $"Attenzione: {errors.Count} errori nel file intervalli.";
                }
            }

            if (intervals.Count == 0)
            {
                intervals.Add(new IntervalRange { Start = start, End = end });
            }

            int count = 0;
            foreach(var interval in intervals)
            {
                // Create separate job for each interval if requested (or maybe for all?)
                // Legacy logic: if "_serverPerIntervalRadio" is checked, loop.
                // Here we assume "SeparateArchive" flag works for both modes or implies creating multiple jobs?
                // Actually the logic in legacy code loops if intervals are present.

                await CreateJob(interval.Start, interval.End);
                count++;
            }

            StatusMessage = $"Avviati {count} processi di archiviazione.";
        }

        private bool Validate()
        {
            if (IsServer && string.IsNullOrWhiteSpace(ServerAddress))
            {
                StatusMessage = "Indirizzo server obbligatorio.";
                return false;
            }
            if (IsServer && !IsConnected)
            {
                // Optional: Force connect?
                // StatusMessage = "Non connesso al server.";
                // return false;
            }
            if (string.IsNullOrWhiteSpace(ExportPath))
            {
                StatusMessage = "Percorso di esportazione obbligatorio.";
                return false;
            }
            // Add other mandatory fields validation logic here
            return true;
        }

        private async Task CreateJob(DateTime start, DateTime end)
        {
            // Build the specific destination path based on metadata
            // Using logic ported from legacy BuildExportDestinationPath
            string finalPath = ExportPath;
            try
            {
                if (!string.IsNullOrWhiteSpace(ExportPath))
                {
                    finalPath = PathHelper.BuildExportDestinationPath(
                        ExportPath,
                        CriminalProceeding ?? "",
                        RitSpec ?? "",
                        Target ?? "",
                        WorkId ?? "",
                        false, 0);

                    // Ensure directory exists
                    if (!Directory.Exists(finalPath))
                    {
                        Directory.CreateDirectory(finalPath);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore creazione percorso: {ex.Message}";
                return; // Stop if path creation fails
            }

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
                Procura = Procura,
                WorkId = WorkId,
                Target = Target,
                Password = Password,
                ExportPath = finalPath, // Use the constructed specific path
                Note = Note
            };

            await _jobManager.AddJobAsync(job);
        }
    }
}
