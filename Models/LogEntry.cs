using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TerraformLogViewer.Models
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error,
        Trace,
        Unknown
    }

    public enum TerraformPhase
    {
        Unknown,
        Plan,
        Apply,
        Init,
        Destroy,
        Refresh
    }

    public enum EntryStatus
    {
        Unread,
        Read,
        Important
    }

    public class LogEntry
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid LogFileId { get; set; }

        public DateTime? Timestamp { get; set; }
        public DateTime? ParsedTimestamp { get; set; } = DateTime.UtcNow;

        public LogLevel Level { get; set; } = LogLevel.Unknown;

        [Required]
        public string RawMessage { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? TfReqId { get; set; }

        [MaxLength(200)]
        public string? TfResourceType { get; set; }

        [MaxLength(100)]
        public string? TfResourceName { get; set; }

        public TerraformPhase Phase { get; set; } = TerraformPhase.Unknown;

        [Column(TypeName = "jsonb")]
        public string? HttpReqBody { get; set; }

        [Column(TypeName = "jsonb")]
        public string? HttpResBody { get; set; }

        [MaxLength(20)]
        public string? HttpMethod { get; set; }

        [MaxLength(500)]
        public string? HttpUrl { get; set; }

        public int? HttpStatusCode { get; set; }

        public EntryStatus Status { get; set; } = EntryStatus.Unread;

        [MaxLength(50)]
        public string? SourceFile { get; set; }

        public int LineNumber { get; set; }

        // Навигационные свойства
        [ForeignKey("LogFileId")]
        public virtual LogFile LogFile { get; set; } = null!;
    }
}