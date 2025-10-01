using Microsoft.EntityFrameworkCore;
using TerraformLogViewer.Models;
using LogLevel = TerraformLogViewer.Models.LogLevel;

namespace TerraformLogViewer.Services
{
    public class VisualizationService
    {
        private readonly AppDbContext _context;

        public VisualizationService(AppDbContext context)
        {
            _context = context;
        }

        public class SearchCriteria
        {
            public string? TfResourceType { get; set; }
            public DateTime? StartTimestamp { get; set; }
            public DateTime? EndTimestamp { get; set; }
            public LogLevel? MinLogLevel { get; set; }
            public string? TfReqId { get; set; }
            public TerraformPhase? Phase { get; set; }
            public bool UnreadOnly { get; set; } = false;
            public string? FreeText { get; set; }
        }

        public async Task<List<LogEntry>> SearchLogsAsync(Guid logFileId, SearchCriteria criteria)
        {
            var query = _context.LogEntries
                .Where(e => e.LogFileId == logFileId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(criteria.TfResourceType))
                query = query.Where(e => e.TfResourceType != null && e.TfResourceType.Contains(criteria.TfResourceType));

            if (criteria.StartTimestamp.HasValue)
                query = query.Where(e => e.Timestamp >= criteria.StartTimestamp);

            if (criteria.EndTimestamp.HasValue)
                query = query.Where(e => e.Timestamp <= criteria.EndTimestamp);

            if (criteria.MinLogLevel.HasValue)
                query = query.Where(e => e.Level >= criteria.MinLogLevel);

            if (!string.IsNullOrEmpty(criteria.TfReqId))
                query = query.Where(e => e.TfReqId == criteria.TfReqId);

            if (criteria.Phase.HasValue)
                query = query.Where(e => e.Phase == criteria.Phase);

            if (criteria.UnreadOnly)
                query = query.Where(e => e.Status == EntryStatus.Unread);

            if (!string.IsNullOrEmpty(criteria.FreeText))
                query = query.Where(e => e.RawMessage.Contains(criteria.FreeText));

            return await query
                .OrderBy(e => e.Timestamp)
                .ToListAsync();
        }

        public async Task MarkAsReadAsync(Guid entryId)
        {
            var entry = await _context.LogEntries.FindAsync(entryId);
            if (entry != null)
            {
                entry.Status = EntryStatus.Read;
                await _context.SaveChangesAsync();
            }
        }

        public async Task MarkAsUnreadAsync(Guid entryId)
        {
            var entry = await _context.LogEntries.FindAsync(entryId);
            if (entry != null)
            {
                entry.Status = EntryStatus.Unread;
                await _context.SaveChangesAsync();
            }
        }

        public class TimelineData
        {
            public List<RequestChain> Chains { get; set; } = new();
            public DateTime MinTime { get; set; }
            public DateTime MaxTime { get; set; }
        }

        public class RequestChain
        {
            public string TfReqId { get; set; } = string.Empty;
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public List<LogEntry> Entries { get; set; } = new();
        }

        public async Task<TimelineData> GenerateTimelineAsync(Guid logFileId)
        {
            var entries = await _context.LogEntries
                .Where(e => e.LogFileId == logFileId && e.TfReqId != null && e.Timestamp.HasValue)
                .OrderBy(e => e.Timestamp)
                .ToListAsync();

            var chains = entries
                .GroupBy(e => e.TfReqId)
                .Select(g => new RequestChain
                {
                    TfReqId = g.Key!,
                    StartTime = g.Min(e => e.Timestamp)!.Value,
                    EndTime = g.Max(e => e.Timestamp)!.Value,
                    Entries = g.ToList()
                })
                .OrderBy(c => c.StartTime)
                .ToList();

            return new TimelineData
            {
                Chains = chains,
                MinTime = chains.Any() ? chains.Min(c => c.StartTime) : DateTime.UtcNow,
                MaxTime = chains.Any() ? chains.Max(c => c.EndTime) : DateTime.UtcNow
            };
        }
    }
}