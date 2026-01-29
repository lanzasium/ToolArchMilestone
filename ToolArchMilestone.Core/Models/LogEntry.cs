using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ToolArchMilestone.Core.Models
{
    public class LogEntry
    {
        [Key]
        public int Id { get; set; }
        
        public int JobId { get; set; } // Changed to int to match ArchivingJob.Id
        
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; } = string.Empty;
        public string Level { get; set; } = "Info"; // Info, Warning, Error
    }
}
