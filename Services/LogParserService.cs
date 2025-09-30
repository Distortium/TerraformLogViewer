namespace TerraformLogViewer.Services
{
    using System.Text.RegularExpressions;
    using TerraformLogViewer.Models;

    public interface ILogParserService
    {
        Task<TerraformLog> ParseLogAsync(string logContent);
        TerraformLog ParseLog(string logContent);
        List<TerraformResource> ExtractResources(List<LogEntry> entries);
        List<LogError> ExtractErrors(List<LogEntry> entries);
        List<LogWarning> ExtractWarnings(List<LogEntry> entries);
        ExecutionSummary CalculateSummary(TerraformLog log);
    }

    public class LogParserService : ILogParserService
    {
        private readonly ILogger<LogParserService> _logger;

        // Регулярные выражения для парсинга
        private static readonly Regex TimestampRegex = new Regex(@"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)", RegexOptions.Compiled);
        private static readonly Regex ResourceRegex = new Regex(@"(aws|azurerm|google|kubernetes)_[\w]+\.?[\w]*", RegexOptions.Compiled);
        private static readonly Regex ResourceActionRegex = new Regex(@"#\s*([\w\._\-]+)\s+will be\s+(created|updated|destroyed|read)", RegexOptions.Compiled);
        private static readonly Regex ResourceResultRegex = new Regex(@"#\s*([\w\._\-]+)\s+(created|updated|destroyed|read)\s+.*?(\d+[smh]*)", RegexOptions.Compiled);
        private static readonly Regex ErrorRegex = new Regex(@"(Error:|ERROR|Failed|failed)\s*:\s*(.+)", RegexOptions.Compiled);
        private static readonly Regex WarningRegex = new Regex(@"(Warning:|WARN)\s*(.+)", RegexOptions.Compiled);
        private static readonly Regex PlanSummaryRegex = new Regex(@"Plan:\s*(\d+)\s+to\s+add,\s*(\d+)\s+to\s+change,\s*(\d+)\s+to\s+destroy", RegexOptions.Compiled);
        private static readonly Regex ApplySummaryRegex = new Regex(@"Apply\s+complete!\s+Resources:\s*(\d+)\s+added,\s*(\d+)\s+changed,\s*(\d+)\s+destroyed", RegexOptions.Compiled);
        private static readonly Regex DurationRegex = new Regex(@"(\d+[smh]+\d*[smh]*)", RegexOptions.Compiled);
        private static readonly Regex DependencyRegex = new Regex(@"depends_on\s*=\s*\[([^\]]+)\]", RegexOptions.Compiled);

        public LogParserService(ILogger<LogParserService> logger)
        {
            _logger = logger;
        }

        public async Task<TerraformLog> ParseLogAsync(string logContent)
        {
            return await Task.Run(() => ParseLog(logContent));
        }

        public TerraformLog ParseLog(string logContent)
        {
            var terraformLog = new TerraformLog
            {
                OriginalLog = logContent
            };

            try
            {
                var lines = logContent.Split('\n');
                terraformLog.Entries = ParseLogEntries(lines).ToList();
                terraformLog.Errors = ExtractErrors(terraformLog.Entries);
                terraformLog.Warnings = ExtractWarnings(terraformLog.Entries);
                terraformLog.Resources = ExtractResources(terraformLog.Entries);
                terraformLog.Summary = CalculateSummary(terraformLog);
                terraformLog.Phases = ExtractPhases(terraformLog.Entries);

                // Связываем ресурсы с соответствующими записями логов
                LinkResourcesWithEntries(terraformLog);

                _logger.LogInformation("Successfully parsed Terraform log with {ResourceCount} resources, {ErrorCount} errors",
                    terraformLog.Resources.Count, terraformLog.Errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Terraform log");
                throw;
            }

            return terraformLog;
        }

        private IEnumerable<LogEntry> ParseLogEntries(string[] lines)
        {
            var currentPhase = "unknown";
            var lineNumber = 0;

            foreach (var line in lines)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var entry = new LogEntry
                {
                    RawLine = line.Trim(),
                    LineNumber = lineNumber,
                    Phase = currentPhase
                };

                // Определяем уровень логирования
                entry.Level = DetermineLogLevel(line);

                // Определяем фазу выполнения
                if (line.Contains("Terraform will perform the following actions") || line.Contains("Plan:"))
                    currentPhase = "plan";
                else if (line.Contains("Apply complete!") || line.Contains("Applying..."))
                    currentPhase = "apply";
                else if (line.Contains("Initializing") || line.Contains("Initialization"))
                    currentPhase = "init";

                entry.Phase = currentPhase;

                // Извлекаем timestamp если есть
                var timestampMatch = TimestampRegex.Match(line);
                if (timestampMatch.Success && DateTime.TryParse(timestampMatch.Value, out var timestamp))
                {
                    entry.Timestamp = timestamp;
                }
                else
                {
                    entry.Timestamp = DateTime.UtcNow; // fallback
                }

                // Извлекаем сообщение
                entry.Message = ExtractMessage(line);

                // Извлекаем адрес ресурса если есть
                var resourceMatch = ResourceRegex.Match(line);
                if (resourceMatch.Success)
                {
                    entry.ResourceAddress = resourceMatch.Value;
                }

                yield return entry;
            }
        }

        public List<TerraformResource> ExtractResources(List<LogEntry> entries)
        {
            var resources = new Dictionary<string, TerraformResource>();

            foreach (var entry in entries)
            {
                // Поиск объявлений ресурсов в плане
                var actionMatch = ResourceActionRegex.Match(entry.RawLine);
                if (actionMatch.Success)
                {
                    var address = actionMatch.Groups[1].Value;
                    var action = actionMatch.Groups[2].Value;

                    if (!resources.ContainsKey(address))
                    {
                        resources[address] = new TerraformResource
                        {
                            Address = address,
                            Type = ExtractResourceType(address),
                            Provider = ExtractProvider(address),
                            Action = ParseResourceAction(action),
                            Status = ResourceStatus.Pending,
                            StartLine = entry.LineNumber
                        };
                    }
                }

                // Поиск результатов выполнения ресурсов
                var resultMatch = ResourceResultRegex.Match(entry.RawLine);
                if (resultMatch.Success)
                {
                    var address = resultMatch.Groups[1].Value;
                    var result = resultMatch.Groups[2].Value;
                    var duration = ParseDuration(resultMatch.Groups[3].Value);

                    if (resources.ContainsKey(address))
                    {
                        resources[address].Status = result.ToLower() == "failed" ?
                            ResourceStatus.Failed : ResourceStatus.Success;
                        resources[address].Duration = duration;
                        resources[address].EndLine = entry.LineNumber;
                    }
                    else
                    {
                        // Создаем ресурс если его еще нет
                        resources[address] = new TerraformResource
                        {
                            Address = address,
                            Type = ExtractResourceType(address),
                            Provider = ExtractProvider(address),
                            Action = ParseResourceAction(result),
                            Status = result.ToLower() == "failed" ?
                                ResourceStatus.Failed : ResourceStatus.Success,
                            Duration = duration,
                            StartLine = entry.LineNumber,
                            EndLine = entry.LineNumber
                        };
                    }
                }

                // Обработка ошибок для ресурсов
                if (entry.Level == LogLevel.Error && !string.IsNullOrEmpty(entry.ResourceAddress))
                {
                    var address = entry.ResourceAddress;
                    if (!resources.ContainsKey(address))
                    {
                        resources[address] = new TerraformResource
                        {
                            Address = address,
                            Type = ExtractResourceType(address),
                            Provider = ExtractProvider(address),
                            Action = ResourceAction.Create, // предположение по умолчанию
                            Status = ResourceStatus.Failed,
                            StartLine = entry.LineNumber
                        };
                    }
                    resources[address].Status = ResourceStatus.Failed;
                }
            }

            // Извлекаем зависимости
            ExtractDependencies(resources, entries);

            return resources.Values.ToList();
        }

        public List<LogError> ExtractErrors(List<LogEntry> entries)
        {
            var errors = new List<LogError>();

            foreach (var entry in entries.Where(e => e.Level == LogLevel.Error))
            {
                var errorMatch = ErrorRegex.Match(entry.RawLine);
                if (errorMatch.Success || entry.Level == LogLevel.Error)
                {
                    var error = new LogError
                    {
                        Message = errorMatch.Success ? errorMatch.Groups[2].Value : entry.Message,
                        ResourceAddress = entry.ResourceAddress,
                        Phase = entry.Phase,
                        LineNumber = entry.LineNumber,
                        Suggestion = GenerateSuggestion(entry.RawLine)
                    };

                    // Извлекаем код ошибки если есть
                    var codeMatch = Regex.Match(entry.RawLine, @"\(([A-Za-z_]+)\)");
                    if (codeMatch.Success)
                    {
                        error.ErrorCode = codeMatch.Groups[1].Value;
                    }

                    errors.Add(error);
                }
            }

            return errors;
        }

        public List<LogWarning> ExtractWarnings(List<LogEntry> entries)
        {
            var warnings = new List<LogWarning>();

            foreach (var entry in entries.Where(e => e.Level == LogLevel.Warn))
            {
                var warningMatch = WarningRegex.Match(entry.RawLine);
                if (warningMatch.Success || entry.Level == LogLevel.Warn)
                {
                    warnings.Add(new LogWarning
                    {
                        Message = warningMatch.Success ? warningMatch.Groups[2].Value : entry.Message,
                        ResourceAddress = entry.ResourceAddress,
                        LineNumber = entry.LineNumber
                    });
                }
            }

            return warnings;
        }

        public ExecutionSummary CalculateSummary(TerraformLog log)
        {
            var summary = new ExecutionSummary
            {
                HasErrors = log.Errors.Any(),
                HasWarnings = log.Warnings.Any()
            };

            // Анализ суммарной статистики из лога
            foreach (var entry in log.Entries)
            {
                // Поиск summary plan
                var planMatch = PlanSummaryRegex.Match(entry.RawLine);
                if (planMatch.Success)
                {
                    summary.ResourcesToAdd = int.Parse(planMatch.Groups[1].Value);
                    summary.ResourcesToChange = int.Parse(planMatch.Groups[2].Value);
                    summary.ResourcesToDestroy = int.Parse(planMatch.Groups[3].Value);
                }

                // Поиск summary apply
                var applyMatch = ApplySummaryRegex.Match(entry.RawLine);
                if (applyMatch.Success)
                {
                    summary.ResourcesToAdd = int.Parse(applyMatch.Groups[1].Value);
                    summary.ResourcesToChange = int.Parse(applyMatch.Groups[2].Value);
                    summary.ResourcesToDestroy = int.Parse(applyMatch.Groups[3].Value);
                }
            }

            // Расчет длительности
            if (log.Phases.Any())
            {
                var start = log.Phases.Min(p => p.StartTime);
                var end = log.Phases.Max(p => p.EndTime);
                summary.TotalDuration = end - start;
            }

            return summary;
        }

        #region Вспомогательные методы

        private LogLevel DetermineLogLevel(string line)
        {
            if (line.Contains("ERROR") || line.Contains("Error:") || line.Contains("failed"))
                return LogLevel.Error;
            if (line.Contains("WARN") || line.Contains("Warning:"))
                return LogLevel.Warn;
            if (line.Contains("DEBUG") || line.Contains("TRACE"))
                return LogLevel.Debug;
            if (line.Contains("INFO"))
                return LogLevel.Info;

            return LogLevel.Info; // по умолчанию
        }

        private string ExtractMessage(string line)
        {
            // Убираем timestamp если есть
            var cleanedLine = TimestampRegex.Replace(line, "").Trim();

            // Убираем префиксы уровней логирования
            cleanedLine = Regex.Replace(cleanedLine, @"^(ERROR|WARN|INFO|DEBUG|TRACE)\s*:", "").Trim();

            return cleanedLine;
        }

        private string ExtractResourceType(string address)
        {
            var parts = address.Split('.');
            return parts.Length > 0 ? parts[0] : address;
        }

        private string ExtractProvider(string address)
        {
            if (address.StartsWith("aws_")) return "aws";
            if (address.StartsWith("azurerm_")) return "azurerm";
            if (address.StartsWith("google_")) return "google";
            if (address.StartsWith("kubernetes_")) return "kubernetes";
            return "unknown";
        }

        private ResourceAction ParseResourceAction(string action)
        {
            return action.ToLower() switch
            {
                "created" or "create" => ResourceAction.Create,
                "updated" or "update" => ResourceAction.Update,
                "destroyed" or "destroy" => ResourceAction.Delete,
                "read" => ResourceAction.Read,
                "refreshed" => ResourceAction.Refresh,
                _ => ResourceAction.NoOp
            };
        }

        private TimeSpan ParseDuration(string durationStr)
        {
            try
            {
                if (durationStr.EndsWith("s"))
                    return TimeSpan.FromSeconds(int.Parse(durationStr.TrimEnd('s')));
                if (durationStr.EndsWith("m"))
                    return TimeSpan.FromMinutes(int.Parse(durationStr.TrimEnd('m')));
                if (durationStr.EndsWith("h"))
                    return TimeSpan.FromHours(int.Parse(durationStr.TrimEnd('h')));

                return TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private void ExtractDependencies(Dictionary<string, TerraformResource> resources, List<LogEntry> entries)
        {
            // Простой анализ зависимостей на основе порядка выполнения
            var resourceOrder = resources.Values
                .Where(r => r.StartLine > 0)
                .OrderBy(r => r.StartLine)
                .ToList();

            for (int i = 1; i < resourceOrder.Count; i++)
            {
                var current = resourceOrder[i];
                var previous = resourceOrder[i - 1];

                // Если ресурсы выполнялись близко по времени, предполагаем зависимость
                if (current.StartLine - previous.EndLine < 10)
                {
                    current.Dependencies.Add(previous.Address);
                }
            }
        }

        private List<ExecutionPhase> ExtractPhases(List<LogEntry> entries)
        {
            var phases = new List<ExecutionPhase>();
            var currentPhase = new ExecutionPhase();

            foreach (var entry in entries)
            {
                if (entry.Message.Contains("Initializing") && currentPhase.Name != "init")
                {
                    if (!string.IsNullOrEmpty(currentPhase.Name))
                        phases.Add(currentPhase);

                    currentPhase = new ExecutionPhase { Name = "init", StartTime = entry.Timestamp };
                }
                else if (entry.Message.Contains("Terraform will perform") && currentPhase.Name != "plan")
                {
                    if (!string.IsNullOrEmpty(currentPhase.Name))
                    {
                        currentPhase.EndTime = entry.Timestamp;
                        phases.Add(currentPhase);
                    }
                    currentPhase = new ExecutionPhase { Name = "plan", StartTime = entry.Timestamp };
                }
                else if (entry.Message.Contains("Applying") && currentPhase.Name != "apply")
                {
                    if (!string.IsNullOrEmpty(currentPhase.Name))
                    {
                        currentPhase.EndTime = entry.Timestamp;
                        phases.Add(currentPhase);
                    }
                    currentPhase = new ExecutionPhase { Name = "apply", StartTime = entry.Timestamp };
                }
            }

            // Завершаем последнюю фазу
            if (!string.IsNullOrEmpty(currentPhase.Name) && entries.Any())
            {
                currentPhase.EndTime = entries.Last().Timestamp;
                currentPhase.Completed = true;
                phases.Add(currentPhase);
            }

            return phases;
        }

        private void LinkResourcesWithEntries(TerraformLog log)
        {
            foreach (var resource in log.Resources)
            {
                resource.RelatedEntries = log.Entries
                    .Where(e => e.ResourceAddress == resource.Address ||
                               e.LineNumber >= resource.StartLine &&
                               e.LineNumber <= resource.EndLine)
                    .ToList();

                resource.Errors = log.Errors
                    .Where(e => e.ResourceAddress == resource.Address)
                    .ToList();
            }
        }

        private string GenerateSuggestion(string errorMessage)
        {
            if (errorMessage.Contains("limit exceeded", StringComparison.OrdinalIgnoreCase))
                return "Check your resource limits in the cloud provider console. Consider requesting limit increase.";

            if (errorMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                return "Resource might already exist. Check if you need to import it or use a different name.";

            if (errorMessage.Contains("permission", StringComparison.OrdinalIgnoreCase))
                return "Verify IAM permissions and ensure your credentials have sufficient privileges.";

            if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                return "Operation timed out. Consider increasing timeout values or checking network connectivity.";

            return "Review the error details and check Terraform documentation for this resource type.";
        }

        #endregion
    }
}
