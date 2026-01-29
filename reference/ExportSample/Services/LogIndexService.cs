using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace ExportSample.Services
{
    /// <summary>
    /// Classe che gestisce log index service all'interno del tool.
    /// </summary>
    internal sealed class LogIndexService
    {
        /// <summary>
        /// Classe che gestisce log index entry all'interno del tool.
        /// </summary>
        internal sealed class LogIndexEntry
        {
            /// <summary>
            /// Identificativo per job.
            /// </summary>
            public string JobId { get; set; }
            /// <summary>
            /// Percorso configurato per file path.
            /// </summary>
            public string FilePath { get; set; }
            /// <summary>
            /// Valore status esposto pubblicamente.
            /// </summary>
            public string Status { get; set; }
            /// <summary>
            /// Valore progress esposto pubblicamente.
            /// </summary>
            public int Progress { get; set; }
            /// <summary>
            /// Valore last event esposto pubblicamente.
            /// </summary>
            public string LastEvent { get; set; }
            /// <summary>
            /// Valore created utc esposto pubblicamente.
            /// </summary>
            public DateTime CreatedUtc { get; set; }
            /// <summary>
            /// Timestamp relativo a last updated utc.
            /// </summary>
            public DateTime LastUpdatedUtc { get; set; }
            public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly string _indexFilePath;
        private readonly object _lock = new object();
        private Dictionary<string, LogIndexEntry> _entries = new Dictionary<string, LogIndexEntry>(StringComparer.OrdinalIgnoreCase);
        private bool _loaded;

        /// <summary>
        /// Costruttore di LogIndexService, inizializza il contesto senza effetti collaterali.
        /// </summary>
        public LogIndexService(string logsDirectory)
        {
            if (string.IsNullOrWhiteSpace(logsDirectory))
                throw new ArgumentException("logsDirectory must be provided", nameof(logsDirectory));

            _indexFilePath = Path.Combine(logsDirectory, "log_index.json");
        }

        /// <summary>
        /// Esegue la logica try get log path senza cambiare il comportamento.
        /// </summary>
        public bool TryGetLogPath(string jobId, out string filePath)
        {
            filePath = null;
            if (string.IsNullOrWhiteSpace(jobId))
                return false;

            EnsureLoaded();
            lock (_lock)
            {
                if (_entries.TryGetValue(jobId, out var entry))
                {
                    filePath = entry?.FilePath;
                    return !string.IsNullOrWhiteSpace(filePath);
                }
            }

            return false;
        }

        /// <summary>
        /// Esegue la logica register or update senza cambiare il comportamento.
        /// </summary>
        public void RegisterOrUpdate(LogIndexEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.JobId))
                return;

            EnsureLoaded();
            lock (_lock)
            {
                if (!_entries.TryGetValue(entry.JobId, out var existing))
                {
                    entry.CreatedUtc = entry.CreatedUtc == default ? DateTime.UtcNow : entry.CreatedUtc;
                    entry.LastUpdatedUtc = DateTime.UtcNow;
                    _entries[entry.JobId] = entry;
                }
                else
                {
                    existing.FilePath = !string.IsNullOrWhiteSpace(entry.FilePath) ? entry.FilePath : existing.FilePath;
                    if (!string.IsNullOrWhiteSpace(entry.Status))
                        existing.Status = entry.Status;
                    if (entry.Progress >= 0)
                        existing.Progress = entry.Progress;
                    if (!string.IsNullOrWhiteSpace(entry.LastEvent))
                        existing.LastEvent = entry.LastEvent;
                    if (entry.Metadata != null && entry.Metadata.Count > 0)
                        existing.Metadata = new Dictionary<string, string>(entry.Metadata, StringComparer.OrdinalIgnoreCase);
                    existing.LastUpdatedUtc = DateTime.UtcNow;
                }

                PersistUnsafe();
            }
        }

        /// <summary>
        /// Esegue la logica register event senza cambiare il comportamento.
        /// </summary>
        public void RegisterEvent(string jobId, string eventType, string status, int progress)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return;

            EnsureLoaded();
            lock (_lock)
            {
                if (!_entries.TryGetValue(jobId, out var entry))
                {
                    entry = new LogIndexEntry
                    {
                        JobId = jobId,
                        CreatedUtc = DateTime.UtcNow
                    };
                    _entries[jobId] = entry;
                }

                if (!string.IsNullOrWhiteSpace(status))
                    entry.Status = status;

                if (progress >= 0)
                    entry.Progress = progress;

                entry.LastEvent = string.IsNullOrWhiteSpace(eventType) ? entry.LastEvent : eventType;
                entry.LastUpdatedUtc = DateTime.UtcNow;
                PersistUnsafe();
            }
        }

        /// <summary>
        /// Esegue la logica register completion senza cambiare il comportamento.
        /// </summary>
        public void RegisterCompletion(string jobId, string status, string filePath, long? sizeBytes, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return;

            EnsureLoaded();
            lock (_lock)
            {
                if (!_entries.TryGetValue(jobId, out var entry))
                {
                    entry = new LogIndexEntry
                    {
                        JobId = jobId,
                        CreatedUtc = DateTime.UtcNow
                    };
                    _entries[jobId] = entry;
                }

                entry.Status = status ?? entry.Status;
                entry.Progress = status != null && status.IndexOf("erro", StringComparison.OrdinalIgnoreCase) >= 0
                    ? entry.Progress
                    : 100;
                entry.FilePath = !string.IsNullOrWhiteSpace(filePath) ? filePath : entry.FilePath;
                entry.LastEvent = "completed";
                entry.LastUpdatedUtc = DateTime.UtcNow;

                if (entry.Metadata == null)
                    entry.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (sizeBytes.HasValue)
                    entry.Metadata["sizeBytes"] = sizeBytes.Value.ToString();

                if (!string.IsNullOrWhiteSpace(outputFolder))
                    entry.Metadata["outputFolder"] = outputFolder;

                PersistUnsafe();
            }
        }

        /// <summary>
        /// Esegue la logica swap job ids senza cambiare il comportamento.
        /// </summary>
        public void SwapJobIds(string firstId, string secondId)
        {
            if (string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId))
                return;

            if (string.Equals(firstId, secondId, StringComparison.OrdinalIgnoreCase))
                return;

            EnsureLoaded();
            lock (_lock)
            {
                _entries.TryGetValue(firstId, out var firstEntry);
                _entries.TryGetValue(secondId, out var secondEntry);

                if (firstEntry == null && secondEntry == null)
                    return;

                if (firstEntry != null)
                    _entries.Remove(firstId);
                if (secondEntry != null)
                    _entries.Remove(secondId);

                if (firstEntry != null)
                {
                    firstEntry.JobId = secondId;
                    _entries[secondId] = firstEntry;
                }

                if (secondEntry != null)
                {
                    secondEntry.JobId = firstId;
                    _entries[firstId] = secondEntry;
                }

                PersistUnsafe();
            }
        }

        /// <summary>
        /// Si assicura che la parte loaded sia pronta prima di procedere.
        /// </summary>
        private void EnsureLoaded()
        {
            if (_loaded)
                return;

            lock (_lock)
            {
                if (_loaded)
                    return;

                try
                {
                    if (File.Exists(_indexFilePath))
                    {
                        var json = File.ReadAllText(_indexFilePath, Encoding.UTF8);
                        var deserialized = JsonConvert.DeserializeObject<Dictionary<string, LogIndexEntry>>(json);
                        _entries = deserialized != null
                            ? new Dictionary<string, LogIndexEntry>(deserialized, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, LogIndexEntry>(StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        _entries = new Dictionary<string, LogIndexEntry>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LogIndexService: errore caricamento index - {ex.Message}");
                    _entries = new Dictionary<string, LogIndexEntry>(StringComparer.OrdinalIgnoreCase);
                }

                _loaded = true;
            }
        }

        /// <summary>
        /// Esegue la logica persist unsafe senza cambiare il comportamento.
        /// </summary>
        private void PersistUnsafe()
        {
            try
            {
                var directory = Path.GetDirectoryName(_indexFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonConvert.SerializeObject(_entries, Formatting.Indented);
                File.WriteAllText(_indexFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogIndexService: errore salvataggio index - {ex.Message}");
            }
        }
    }
}



