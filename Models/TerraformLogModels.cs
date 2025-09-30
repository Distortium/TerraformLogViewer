namespace TerraformLogViewer.Models
{
    public class TerraformLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime UploadTime { get; set; } = DateTime.UtcNow;
        public string OriginalLog { get; set; } = string.Empty;
        public List<LogEntry> Entries { get; set; } = new();
        public List<TerraformResource> Resources { get; set; } = new();
        public List<LogError> Errors { get; set; } = new();
        public List<LogWarning> Warnings { get; set; } = new();
        public ExecutionSummary Summary { get; set; } = new();
        public List<ExecutionPhase> Phases { get; set; } = new();
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string ResourceAddress { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string RawLine { get; set; } = string.Empty;
    }

    public class TerraformResource
    {
        public string Address { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public ResourceAction Action { get; set; }
        public ResourceStatus Status { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public List<LogEntry> RelatedEntries { get; set; } = new();
        public List<LogError> Errors { get; set; } = new();
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    public class LogError
    {
        public string Message { get; set; } = string.Empty;
        public string ResourceAddress { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Suggestion { get; set; } = string.Empty;
    }

    public class LogWarning
    {
        public string Message { get; set; } = string.Empty;
        public string ResourceAddress { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }

    public class ExecutionSummary
    {
        public int ResourcesToAdd { get; set; }
        public int ResourcesToChange { get; set; }
        public int ResourcesToDestroy { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public bool HasErrors { get; set; }
        public bool HasWarnings { get; set; }
    }

    public class ExecutionPhase
    {
        public string Name { get; set; } = string.Empty; // init, plan, apply
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public bool Completed { get; set; }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error,
        Fatal
    }

    public enum ResourceAction
    {
        Create,
        Read,
        Update,
        Delete,
        Refresh,
        NoOp
    }

    public enum ResourceStatus
    {
        Pending,
        InProgress,
        Success,
        Failed,
        Skipped
    }
}
