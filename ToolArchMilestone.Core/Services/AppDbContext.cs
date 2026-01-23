using Microsoft.EntityFrameworkCore;
using ToolArchMilestone.Core.Models;

namespace ToolArchMilestone.Core.Services
{
    public class AppDbContext : DbContext
    {
        public DbSet<ArchivingJob> Jobs { get; set; }
        public DbSet<LogEntry> Logs { get; set; }

        private readonly string _dbPath;

        public AppDbContext()
        {
            // Default constructor for design-time tools or default path
            _dbPath = "toolarch.db";
        }

        public AppDbContext(string dbPath)
        {
            _dbPath = dbPath;
        }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                if (!string.IsNullOrEmpty(_dbPath))
                    optionsBuilder.UseSqlite($"Data Source={_dbPath}");
            }
        }
    }
}
