using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TerraformLogViewer.Models
{
    public class LogFile
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [MaxLength(500)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public long FileSize { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }

        [MaxLength(20)]
        public string FileType { get; set; } = "Text"; // Text, JSON

        public int TotalEntries { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }

        // Навигационные свойства
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
        public virtual ICollection<LogEntry> LogEntries { get; set; } = new List<LogEntry>();
    }
}