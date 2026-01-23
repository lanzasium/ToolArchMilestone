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
        public int Id { get; set; } // ID Job (Numero di lancio / Tool management) - AutoIncrement by DB

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Configuration
        public ArchiveType ArchiveType { get; set; }
        public SourceType SourceType { get; set; }

        // Server Specific
        public string? ServerAddress { get; set; }
        public string? CameraName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        // Legal / Common
        public string? CriminalProceeding { get; set; } // Procedimento Penale
        public string? Magistrate { get; set; } // Magistrato
        public string? RitSpec { get; set; } // RIT/SPEC

        public string? WorkId { get; set; } // ID Lavoro (Fatturazione RCS)

        public string? Target { get; set; }
        public string? Password { get; set; }
        public string? ExportPath { get; set; }

        public string? GeneratedFilePath { get; set; } // Specific file created by the export

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
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        // Logs
        public List<LogEntry> Logs { get; set; } = new List<LogEntry>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
