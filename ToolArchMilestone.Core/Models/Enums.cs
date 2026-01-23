namespace ToolArchMilestone.Core.Models
{
    public enum ArchiveType
    {
        Legal,
        Digital
    }

    public enum SourceType
    {
        Archive,
        Server
    }

    public enum JobStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Stopped
    }
}
