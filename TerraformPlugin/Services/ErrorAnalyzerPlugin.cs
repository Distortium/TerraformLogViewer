using Grpc.Core;
using TerraformPlugin.Grpc;
using System.Text.RegularExpressions;
using LogLevel = TerraformPlugin.Grpc.LogLevel;

namespace TerraformPlugin.Services
{
    public class ErrorAnalyzerPlugin : LogAnalyzerPlugin.LogAnalyzerPluginBase
    {
        private readonly ILogger<ErrorAnalyzerPlugin> _logger;

        public ErrorAnalyzerPlugin(ILogger<ErrorAnalyzerPlugin> logger)
        {
            _logger = logger;
        }

        public override Task<HealthCheckResponse> HealthCheck(HealthCheckRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HealthCheckResponse
            {
                Status = "healthy",
                Message = "Error Pattern Analyzer is running"
            });
        }

        public override Task<FilterResponse> FilterLogs(FilterRequest request, ServerCallContext context)
        {
            var errorEntries = request.Entries
                .Where(e => e.Level == LogLevel.Error)
                .ToList();

            _logger.LogInformation("Filtered {Count} error entries from {Total} total entries",
                errorEntries.Count, request.Entries.Count);

            return Task.FromResult(new FilterResponse
            {
                FilteredEntries = { errorEntries }
            });
        }

        public override Task<AggregateResponse> AggregateErrors(AggregateRequest request, ServerCallContext context)
        {
            var errorEntries = request.Entries
                .Where(e => e.Level == LogLevel.Error)
                .ToList();

            var errorGroups = GroupErrorsByPattern(errorEntries);
            var timeWindowHours = ParseTimeWindow(request.TimeWindow);

            foreach (var group in errorGroups)
            {
                group.FrequencyPerHour = CalculateFrequency(group, timeWindowHours);
            }

            _logger.LogInformation("Aggregated {Count} error groups from {ErrorCount} errors",
                errorGroups.Count, errorEntries.Count);

            return Task.FromResult(new AggregateResponse
            {
                ErrorGroups = { errorGroups }
            });
        }

        private List<ErrorGroup> GroupErrorsByPattern(List<LogEntry> errorEntries)
        {
            var patterns = new Dictionary<string, ErrorGroup>();
            var commonPatterns = new[]
            {
                @"timeout.*\d+ms",
                @"connection.*refused",
                @"permission denied",
                @"resource.*not found",
                @"authentication failed",
                @"quota.*exceeded",
                @"rate limit",
                @"internal server error"
            };

            foreach (var entry in errorEntries)
            {
                var pattern = FindMatchingPattern(entry.RawMessage, commonPatterns) ??
                             ExtractDynamicPattern(entry.RawMessage);

                if (!patterns.ContainsKey(pattern))
                {
                    patterns[pattern] = new ErrorGroup
                    {
                        Pattern = pattern,
                        Count = 0,
                        FirstOccurrence = entry.Timestamp,
                        LastOccurrence = entry.Timestamp,
                        ExampleMessages = { entry.RawMessage },
                        AffectedResources = { GetResourceInfo(entry) }
                    };
                }

                var group = patterns[pattern];
                group.Count++;

                if (DateTime.Parse(entry.Timestamp) < DateTime.Parse(group.FirstOccurrence))
                    group.FirstOccurrence = entry.Timestamp;

                if (DateTime.Parse(entry.Timestamp) > DateTime.Parse(group.LastOccurrence))
                    group.LastOccurrence = entry.Timestamp;

                if (group.ExampleMessages.Count < 3)
                    group.ExampleMessages.Add(entry.RawMessage);

                var resourceInfo = GetResourceInfo(entry);
                if (!string.IsNullOrEmpty(resourceInfo) && !group.AffectedResources.Contains(resourceInfo))
                    group.AffectedResources.Add(resourceInfo);
            }

            return patterns.Values.ToList();
        }

        private string FindMatchingPattern(string message, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(message, pattern, RegexOptions.IgnoreCase))
                {
                    return pattern;
                }
            }
            return null;
        }

        private string ExtractDynamicPattern(string message)
        {
            // Извлекаем динамические части сообщения
            var cleanedMessage = Regex.Replace(message, @"\d+", "#");
            cleanedMessage = Regex.Replace(cleanedMessage, @"0x[0-9a-fA-F]+", "0x#");
            cleanedMessage = Regex.Replace(cleanedMessage, @"['""].*?['""]", "'#'");

            return cleanedMessage.Length > 100 ? cleanedMessage.Substring(0, 100) + "..." : cleanedMessage;
        }

        private string GetResourceInfo(LogEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.TfResourceType) && !string.IsNullOrEmpty(entry.TfResourceName))
            {
                return $"{entry.TfResourceType}.{entry.TfResourceName}";
            }
            return entry.TfResourceType ?? "unknown";
        }

        private double ParseTimeWindow(string timeWindow)
        {
            if (string.IsNullOrEmpty(timeWindow)) return 1.0;

            var match = Regex.Match(timeWindow, @"(\d+)([hmd])");
            if (match.Success)
            {
                var value = double.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;

                return unit switch
                {
                    "h" => value,
                    "m" => value / 60,
                    "d" => value * 24,
                    _ => 1.0
                };
            }

            return 1.0;
        }

        private double CalculateFrequency(ErrorGroup group, double timeWindowHours)
        {
            var first = DateTime.Parse(group.FirstOccurrence);
            var last = DateTime.Parse(group.LastOccurrence);
            var totalHours = (last - first).TotalHours;

            if (totalHours <= 0) return group.Count;

            return group.Count / Math.Max(totalHours, timeWindowHours);
        }

        public override Task<ProcessResponse> ProcessLogs(ProcessRequest request, ServerCallContext context)
        {
            var processedEntries = request.Entries.Select(entry =>
            {
                // Добавляем метаданные об ошибках
                if (entry.Level == LogLevel.Error)
                {
                    var processedEntry = entry.Clone();
                    processedEntry.RawMessage = $"[PATTERN_ANALYZED] {entry.RawMessage}";
                    return processedEntry;
                }
                return entry;
            }).ToList();

            return Task.FromResult(new ProcessResponse
            {
                ProcessedEntries = { processedEntries }
            });
        }
    }
}