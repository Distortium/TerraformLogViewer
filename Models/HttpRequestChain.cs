using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TerraformLogViewer.Models
{
    public class HttpRequestChain
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid LogFileId { get; set; }

        [Required]
        [MaxLength(100)]
        public string TfReqId { get; set; } = string.Empty;

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;

        [MaxLength(200)]
        public string? ResourceType { get; set; }

        [MaxLength(100)]
        public string? HttpMethod { get; set; }

        public int RequestCount { get; set; }
        public int ErrorCount { get; set; }

        // Навигационные свойства
        [ForeignKey("LogFileId")]
        public virtual LogFile LogFile { get; set; } = null!;

        public virtual ICollection<LogEntry> LogEntries { get; set; } = new List<LogEntry>();
    }
}
