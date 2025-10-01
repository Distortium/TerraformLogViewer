
namespace TerraformLogViewer.Models
{
    public class UploadLogRequest
    {
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = "Text";
        public string? UserId { get; set; }
    }

    public class UploadLogResponse
    {
        public Guid LogFileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int TotalEntries { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public DateTime UploadedAt { get; set; }
        public string Status { get; set; } = "Success";
        public string? Message { get; set; }
    }

    public class SearchLogsRequest
    {
        public string? FreeText { get; set; }
        public string? TfResourceType { get; set; }
        public LogLevel? MinLogLevel { get; set; }
        public TerraformPhase? Phase { get; set; }
        public DateTime? StartTimestamp { get; set; }
        public DateTime? EndTimestamp { get; set; }
        public bool UnreadOnly { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class SearchLogsResponse
    {
        public List<LogEntryDto> Entries { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public StatisticsDto Statistics { get; set; } = new();
    }

    public class LogEntryDto
    {
        public Guid Id { get; set; }
        public Guid LogFileId { get; set; }
        public DateTime? Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string RawMessage { get; set; } = string.Empty;
        public string? TfReqId { get; set; }
        public string? TfResourceType { get; set; }
        public string? TfResourceName { get; set; }
        public TerraformPhase Phase { get; set; }
        public string? HttpMethod { get; set; }
        public string? HttpUrl { get; set; }
        public int? HttpStatusCode { get; set; }
        public EntryStatus Status { get; set; }
        public string? SourceFile { get; set; }
        public int LineNumber { get; set; }
        public object? HttpReqBody { get; set; }
        public object? HttpResBody { get; set; }
    }

    public class StatisticsDto
    {
        public int TotalEntries { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int UnreadCount { get; set; }
        public Dictionary<TerraformPhase, int> PhaseDistribution { get; set; } = new();
        public Dictionary<LogLevel, int> LevelDistribution { get; set; } = new();
    }

    public class AlertRequest
    {
        public string AlertId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public LogLevel Severity { get; set; }
        public List<Guid> LogEntryIds { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class AlertResponse
    {
        public string AlertId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? Message { get; set; }
    }

    public class WebhookNotification
    {
        public string EventType { get; set; } = string.Empty;
        public Guid LogFileId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public object Data { get; set; } = new();
    }
}