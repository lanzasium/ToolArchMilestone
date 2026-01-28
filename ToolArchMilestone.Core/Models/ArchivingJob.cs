using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;

namespace ToolArchMilestone.Core.Models
{
    public class ArchivingJob : INotifyPropertyChanged
    {
        [Key]
        public int Id { get; set; } // Internal DB ID
        
        public string? PublicJobId { get; set; } // "1a", "1b" etc. as per legacy logic

        // Helper for UI binding to avoid nested x:Bind
        public string DisplayId => !string.IsNullOrEmpty(PublicJobId) ? PublicJobId : Id.ToString();

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? LastActivity { get; set; }

        // Configuration
        public ArchiveType ArchiveType { get; set; }
        public SourceType SourceType { get; set; }

        // Server Specific
        public string? ServerAddress { get; set; }
        public string? CameraName { get; set; } // Telecamera
        
        // Interval requested
        public DateTime StartTime { get; set; } // Inizio
        public DateTime EndTime { get; set; }   // Fine

        // Legal / Common
        public string? CriminalProceeding { get; set; } // Procedimento Penale
        public string? Magistrate { get; set; } // Magistrato
        public string? RitSpec { get; set; } // RIT/SPEC
        public string? Procura { get; set; } // Procura
        
        public string? WorkId { get; set; } // ID Lavoro (Fatturazione RCS)
        
        public string? Target { get; set; }
        public string? Password { get; set; }
        public string? ExportPath { get; set; } // Cartella di esportazione
        public string? Note { get; set; }

        // Results
        public string? GeneratedFilePath { get; set; } 
        public string? Duration { get; set; } // Durata
        public string? Size { get; set; } // Dimensione formatted string or bytes? Legacy uses string.

        // State
        private JobStatus _status = JobStatus.Pending;
        public JobStatus Status 
        { 
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private double _progress;
        public double Progress 
        { 
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        private string? _errorMessage;
        public string? ErrorMessage 
        { 
            get => _errorMessage;
            set {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        
        // Logs - kept in separate table usually, but maybe helpful here for runtime
        [System.ComponentModel.DataAnnotations.Schema.NotMapped] // Assuming EF or ignore for SQLite if simple
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
