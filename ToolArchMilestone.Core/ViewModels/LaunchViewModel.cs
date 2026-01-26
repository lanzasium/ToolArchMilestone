using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private readonly IMilestoneService _milestone;
        private readonly IServerMappingService _serverMapping;

        public LaunchViewModel(IJobManager jobManager, IFilePickerService filePicker, IMilestoneService milestone, IServerMappingService serverMapping, ISettingsService settings = null)
        {
            _jobManager = jobManager;
            _filePicker = filePicker;
            _milestone = milestone;
            _serverMapping = serverMapping;
            _settings = settings ?? new LocalSettingsService();

            // Sync Strings
            UpdateStartString();
            UpdateEndString();

            LoadPinnedSettings();

            // Check initial state
            IsConnected = _milestone.IsConnected;
            if (IsConnected)
            {
                ConnectedServerName = _serverMapping.GetServerName(_milestone.ConnectedServerName);
            }
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

            var archivePath = await _settings.GetSettingAsync(nameof(ArchivePath));
            if (archivePath != null) ArchivePath = archivePath;

            var archiveCamera = await _settings.GetSettingAsync(nameof(ArchiveCameraName));
            if (archiveCamera != null) ArchiveCameraName = archiveCamera;
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
                    System.Diagnostics.Debug.WriteLine("DEBUG: Switched to SERVER mode");
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
                    System.Diagnostics.Debug.WriteLine("DEBUG: Switched to ARCHIVE mode");
                }
            }
        }

        [ObservableProperty]
        private ArchiveType _selectedArchiveType = ArchiveType.Legal;

        // --- Server Fields ---
        [ObservableProperty]
        private string? _serverAddress = "http://localhost";

        [ObservableProperty]
        private string? _connectedServerName;

        partial void OnServerAddressChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                // Basic normalization on change, but full normalization on connect
            }
        }

        // Legacy string property kept for single-selection compatibility if needed,
        // but UI will use collection for multi-select.
        [ObservableProperty]
        private string? _cameraName;

        // Collection for multi-selection
        [ObservableProperty]
        private ObservableCollection<string> _cameras = new ObservableCollection<string>();

        [ObservableProperty]
        private ObservableCollection<string> _selectedCameras = new ObservableCollection<string>();

        // --- Archive Fields ---
        [ObservableProperty]
        private string? _archivePath;

        partial void OnArchivePathChanged(string? value)
        {
            System.Diagnostics.Debug.WriteLine($"DEBUG: ArchivePath changed to: {value}");
        }

        [ObservableProperty]
        private string? _archiveCameraName;

        partial void OnArchiveCameraNameChanged(string? value)
        {
            System.Diagnostics.Debug.WriteLine($"DEBUG: ArchiveCameraName changed to: {value}");
        }

        [ObservableProperty]
        private List<string>? _archiveCameras = new List<string>();

        // Internal Dates
        [ObservableProperty] private DateTime _startTime = DateTime.Now;
        [ObservableProperty] private DateTime _endTime = DateTime.Now.AddHours(1);
        [ObservableProperty] private TimeSpan _startTimeTime = DateTime.Now.TimeOfDay;
        [ObservableProperty] private TimeSpan _endTimeTime = DateTime.Now.AddHours(1).TimeOfDay;

        // String Bindings for UI
        [ObservableProperty]
        private string _startDateString = string.Empty;

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
        private string _endDateString = string.Empty;

        partial void OnEndDateStringChanged(string value)
        {
            var dt = DateHelper.ParseDateText(value);
            if (dt != default)
            {
                EndTime = dt.Date;
                EndTimeTime = dt.TimeOfDay;
            }
        }

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

        // Private constructor for designer or specialized use cases if needed
        private LaunchViewModel()
        {
            _jobManager = null!;
            _filePicker = null!;
            _milestone = null!;
            _serverMapping = null!;
            _settings = null!;
        }

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

                if (IsConnected)
                {
                    ConnectedServerName = _serverMapping.GetServerName(ServerAddress);
                    StatusMessage = $"Connesso a {ConnectedServerName}.";
                }
                else
                {
                    StatusMessage = "Connessione fallita.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore: {ex.Message}";
                IsConnected = false;
            }
        }

        [RelayCommand]
        public async Task Disconnect()
        {
            if (_jobManager.Jobs.Any(j => j.Status == JobStatus.Running || j.Status == JobStatus.Pending))
            {
                StatusMessage = "Impossibile disconnettere: ci sono archiviazioni in corso o in coda.";
                return;
            }

            try
            {
                await _milestone.DisconnectAsync();
                IsConnected = false;
                ConnectedServerName = null;
                StatusMessage = "Disconnesso.";
                Cameras.Clear();
                SelectedCameras.Clear();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore disconnessione: {ex.Message}";
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
                Cameras.Clear();
                if (cameras != null)
                {
                    foreach (var cam in cameras)
                    {
                        Cameras.Add(cam);
                    }
                }

                if (Cameras.Count > 0)
                {
                     StatusMessage = $"Trovate {Cameras.Count} telecamere. Selezionale dalla lista.";
                }
                else
                {
                    StatusMessage = "Nessuna telecamera trovata.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore recupero telecamere: {ex.Message}";
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
        public async Task BrowseArchive()
        {
            try
            {
                StatusMessage = "Seleziona file archivio o cartella...";
                var archiveFile = await _filePicker.PickSingleFileAsync(new[] { ".scp", ".xpco", ".db" });

                if (!string.IsNullOrEmpty(archiveFile))
                {
                    ArchivePath = archiveFile;
                    StatusMessage = $"Archivio selezionato: {Path.GetFileName(archiveFile)}";
                    await DiscoverArchiveCameras();
                }
                else
                {
                    var archiveFolder = await _filePicker.PickSingleFolderAsync();
                    if (!string.IsNullOrEmpty(archiveFolder))
                    {
                        ArchivePath = archiveFolder;
                        StatusMessage = $"Cartella archivio selezionata: {Path.GetFileName(archiveFolder)}";
                        await DiscoverArchiveCameras();
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore selezione archivio: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task DiscoverArchiveCameras()
        {
            if (string.IsNullOrWhiteSpace(ArchivePath))
            {
                StatusMessage = "Seleziona prima un archivio.";
                return;
            }

            try
            {
                StatusMessage = "Scoperta telecamere in corso...";
                var cameras = await SimulateArchiveCameraDiscovery(ArchivePath);
                ArchiveCameras = cameras;

                if (cameras.Any())
                {
                    StatusMessage = $"Trovate {cameras.Count} telecamere nell'archivio.";
                    ArchiveCameraName = cameras.First();
                }
                else
                {
                    StatusMessage = "Nessuna telecamera trovata nell'archivio.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore scoperta telecamere: {ex.Message}";
            }
        }

        private async Task<List<string>> SimulateArchiveCameraDiscovery(string archivePath)
        {
            await Task.Delay(1000);
            var fileName = Path.GetFileName(archivePath);
            return new List<string>
            {
                $"Telecamera 01 - Ingresso ({fileName})",
                $"Telecamera 02 - Reception ({fileName})",
                $"Telecamera 03 - Parcheggio ({fileName})",
                $"Telecamera 04 - Uscita ({fileName})"
            };
        }

        [RelayCommand]
        public async Task ImportIntervalsFromFile()
        {
             var filePath = await _filePicker.PickSingleFileAsync(new[] { ".txt" });
             await ProcessIntervalFile(filePath);
        }

        public async Task ProcessIntervalFile(string filePath)
        {
             if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
             {
                 try {
                     var content = await File.ReadAllTextAsync(filePath);
                     IntervalsText = content;
                     ImportedFileName = Path.GetFileName(filePath);

                     var intervals = DateHelper.ParseIntervalsFromText(content, out var errors);
                     if (intervals.Count > 0)
                     {
                         StatusMessage = $"Caricati {intervals.Count} intervalli.";
                     }
                     else
                     {
                         StatusMessage = "Nessun intervallo valido trovato.";
                     }
                 } catch (Exception ex) {
                     StatusMessage = $"Errore lettura file: {ex.Message}";
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
            switch (fieldName)
            {
                case nameof(ServerAddress): value = ServerAddress; break;
                case nameof(CameraName): value = CameraName; break;
                case nameof(Password): value = Password; break;
                case nameof(ExportPath): value = ExportPath; break;
                case nameof(ArchivePath): value = ArchivePath; break;
                case nameof(ArchiveCameraName): value = ArchiveCameraName; break;
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
            if (string.IsNullOrWhiteSpace(Procura)) Procura = "Procura di...";

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

            // Determine cameras to process
            var targetCameras = new List<string>();
            if (IsServer)
            {
                if (SelectedCameras.Any())
                    targetCameras.AddRange(SelectedCameras);
                else if (!string.IsNullOrEmpty(CameraName))
                    targetCameras.Add(CameraName);
            }
            else
            {
                if (!string.IsNullOrEmpty(ArchiveCameraName))
                    targetCameras.Add(ArchiveCameraName);
            }

            // Calculate effective intervals based on "Separate Archive" flag
            List<IntervalRange> effectiveIntervals = new List<IntervalRange>();

            if (SeparateArchive)
            {
                // Process each interval individually
                effectiveIntervals.AddRange(intervals);
            }
            else
            {
                // Merge all intervals into one global range (min start to max end)
                if (intervals.Count > 0)
                {
                    var minStart = intervals.Min(i => i.Start);
                    var maxEnd = intervals.Max(i => i.End);
                    effectiveIntervals.Add(new IntervalRange { Start = minStart, End = maxEnd });
                }
            }

            // Create jobs loop (Effective Intervals x Cameras)
            foreach (var cam in targetCameras)
            {
                foreach(var interval in effectiveIntervals)
                {
                    await CreateJob(interval.Start, interval.End, cam, count);
                    count++;
                }
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
                 StatusMessage = "Non connesso al server.";
                 return false;
            }
            if (IsServer && SelectedCameras.Count == 0 && string.IsNullOrEmpty(CameraName))
            {
                StatusMessage = "Seleziona almeno una telecamera.";
                return false;
            }

            if (IsArchive && string.IsNullOrWhiteSpace(ArchivePath))
            {
                StatusMessage = "Percorso archivio obbligatorio.";
                return false;
            }
            if (IsArchive && string.IsNullOrWhiteSpace(ArchiveCameraName))
            {
                StatusMessage = "Seleziona una telecamera dall'archivio.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ExportPath))
            {
                StatusMessage = "Percorso di esportazione obbligatorio.";
                return false;
            }
            return true;
        }

        private async Task CreateJob(DateTime start, DateTime end, string camera, int index)
        {
            string finalPath = ExportPath;
            try
            {
                if (!string.IsNullOrWhiteSpace(ExportPath))
                {
                    // Assuming we might launch multiple jobs at once (multi-launch), we pass true/index if > 1 camera or > 1 interval
                    bool isMulti = SelectedCameras.Count > 1 || (IsFileImported);

                    finalPath = PathHelper.BuildExportDestinationPath(
                        ExportPath,
                        CriminalProceeding ?? "",
                        RitSpec ?? "",
                        Target ?? "",
                        WorkId ?? "",
                        isMulti, index);

                    if (!Directory.Exists(finalPath))
                    {
                        Directory.CreateDirectory(finalPath);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore creazione percorso: {ex.Message}";
                return;
            }

            var job = new ArchivingJob
            {
                ArchiveType = SelectedArchiveType,
                SourceType = SelectedSourceType,
                ServerAddress = SelectedSourceType == SourceType.Server ? ServerAddress : ArchivePath,
                CameraName = camera,
                StartTime = start,
                EndTime = end,
                CriminalProceeding = CriminalProceeding,
                Magistrate = Magistrate,
                RitSpec = RitSpec,
                Procura = Procura,
                WorkId = WorkId,
                Target = Target,
                Password = Password,
                ExportPath = finalPath,
                Note = Note
            };

            await _jobManager.AddJobAsync(job);
        }
    }
}
