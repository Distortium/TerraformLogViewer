using Microsoft.EntityFrameworkCore;
using Nest;
using Npgsql;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TerraformLogViewer.Models;
using LogLevel = TerraformLogViewer.Models.LogLevel;

namespace TerraformLogViewer.Services
{
    public class LogParserService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<LogParserService> _logger;

        public LogParserService(AppDbContext context, ILogger<LogParserService> logger)
        {
            _context = context;
            _logger = logger;
        }
        public async Task<LogFile> ParseAndStoreLogsAsync(Stream logStream, string fileName, Guid userId, string fileType = "Text")
        {
            //await _context.Users.AddAsync(new User() { Id = userId });
            //await _context.SaveChangesAsync();

            var lastUser = _context.Users
                .OrderByDescending(u => u.Id)
                .FirstOrDefault();

            // ИСПРАВЛЕНИЕ: Используем переданный userId вместо поиска в базе
            var logFile = new LogFile
            {
                Id = Guid.NewGuid(),
                UserId = lastUser.Id, // Используем переданный userId
                FileName = fileName,
                FileSize = logStream.Length,
                UploadedAt = DateTime.UtcNow,
                FileType = fileType
            };

            await _context.LogFiles.AddAsync(logFile);
            await _context.SaveChangesAsync();

            var (entries, stats) = await ParseLogsWithStatsAsync(logStream, logFile.Id, fileType);

            // Обновляем статистику
            logFile.TotalEntries = stats.TotalEntries;
            logFile.ErrorCount = stats.ErrorCount;
            logFile.WarningCount = stats.WarningCount;
            logFile.ProcessedAt = DateTime.UtcNow;

            // Сохраняем записи батчами
            await SaveEntriesInBatchesAsync(entries, logFile.Id);

            return logFile;
        }

        private async Task<(IAsyncEnumerable<LogEntry> entries, ParseStats stats)> ParseLogsWithStatsAsync(
            Stream logStream, Guid logFileId, string fileType)
        {
            var stats = new ParseStats();

            IAsyncEnumerable<LogEntry> entries = fileType == "JSON"
                ? ParseJsonLogsStreamingAsync(logStream, logFileId, stats)
                : ParseTextLogsStreamingAsync(logStream, logFileId, stats);

            return (entries, stats);
        }

        private async IAsyncEnumerable<LogEntry> ParseTextLogsStreamingAsync(Stream logStream, Guid logFileId, ParseStats stats)
        {
            using var reader = new StreamReader(logStream, Encoding.UTF8, leaveOpen: true);

            string? line;
            int lineNumber = 0;
            TerraformPhase currentPhase = TerraformPhase.Unknown;
            var buffer = ArrayPool<LogEntry>.Shared.Rent(1000);
            var bufferIndex = 0;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Определяем фазу из текстовой строки
                var detectedPhase = DetectPhaseFromText(line);
                if (detectedPhase != TerraformPhase.Unknown)
                {
                    currentPhase = detectedPhase;
                    _logger.LogDebug("Phase changed to {Phase} at line {LineNumber}", currentPhase, lineNumber);
                }

                var entry = ParseTextLogLine(line, lineNumber, currentPhase, logFileId);

                // Обновляем статистику
                UpdateStats(stats, entry);

                buffer[bufferIndex++] = entry;

                // Возвращаем батч когда буфер заполнен
                if (bufferIndex >= buffer.Length)
                {
                    for (int i = 0; i < bufferIndex; i++)
                    {
                        yield return buffer[i];
                    }
                    bufferIndex = 0;
                }
            }

            // Возвращаем оставшиеся записи
            for (int i = 0; i < bufferIndex; i++)
            {
                yield return buffer[i];
            }

            ArrayPool<LogEntry>.Shared.Return(buffer);
        }

        private async IAsyncEnumerable<LogEntry> ParseJsonLogsStreamingAsync(Stream logStream, Guid logFileId, ParseStats stats)
        {
            using var reader = new StreamReader(logStream, Encoding.UTF8, leaveOpen: true);

            string? line;
            int lineNumber = 0;

            // Текущая фаза для всего пласта логов
            TerraformPhase currentPhase = TerraformPhase.Unknown;
            var buffer = ArrayPool<LogEntry>.Shared.Rent(1000);
            var bufferIndex = 0;

            // Список для накопления записей до определения фазы
            var pendingEntries = new List<(string jsonLine, int lineNumber)>();

            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Определяем фазу из JSON строки
                var detectedPhase = DetectPhaseFromJson(line);
                if (detectedPhase != TerraformPhase.Unknown)
                {
                    // Если нашли новую фазу, обрабатываем все накопленные записи с предыдущей фазой
                    if (pendingEntries.Any())
                    {
                        foreach (var (pendingLine, pendingLineNumber) in pendingEntries)
                        {
                            var pendingEntry = ParseJsonLogLine(pendingLine, logFileId, pendingLineNumber, currentPhase);
                            if (pendingEntry != null)
                            {
                                UpdateStats(stats, pendingEntry);
                                buffer[bufferIndex++] = pendingEntry;

                                if (bufferIndex >= buffer.Length)
                                {
                                    for (int i = 0; i < bufferIndex; i++)
                                    {
                                        yield return buffer[i];
                                    }
                                    bufferIndex = 0;
                                }
                            }
                        }
                        pendingEntries.Clear();
                    }

                    currentPhase = detectedPhase;
                    _logger.LogDebug("Phase changed to {Phase} at line {LineNumber}", currentPhase, lineNumber);
                }

                // Если фаза еще не определена, накапливаем записи
                if (currentPhase == TerraformPhase.Unknown)
                {
                    pendingEntries.Add((line, lineNumber));
                }
                else
                {
                    // Фаза определена - обрабатываем запись с текущей фазой
                    var entry = ParseJsonLogLine(line, logFileId, lineNumber, currentPhase);
                    if (entry != null)
                    {
                        UpdateStats(stats, entry);
                        buffer[bufferIndex++] = entry;

                        if (bufferIndex >= buffer.Length)
                        {
                            for (int i = 0; i < bufferIndex; i++)
                            {
                                yield return buffer[i];
                            }
                            bufferIndex = 0;
                        }
                    }
                }
            }

            // Обрабатываем оставшиеся накопленные записи (если фаза так и не определилась)
            if (pendingEntries.Any())
            {
                foreach (var (pendingLine, pendingLineNumber) in pendingEntries)
                {
                    var pendingEntry = ParseJsonLogLine(pendingLine, logFileId, pendingLineNumber, currentPhase);
                    if (pendingEntry != null)
                    {
                        UpdateStats(stats, pendingEntry);
                        buffer[bufferIndex++] = pendingEntry;
                    }
                }
            }

            // Возвращаем оставшиеся записи
            for (int i = 0; i < bufferIndex; i++)
            {
                yield return buffer[i];
            }

            ArrayPool<LogEntry>.Shared.Return(buffer);
        }

        private TerraformPhase DetectPhaseFromJson(string jsonLine)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonLine);
                var root = document.RootElement;

                // 1. Проверяем поле "@message" на наличие CLI args
                if (root.TryGetProperty("@message", out var messageElement) &&
                    messageElement.ValueKind == JsonValueKind.String)
                {
                    var message = messageElement.GetString();
                    if (!string.IsNullOrEmpty(message))
                    {
                        var phaseFromMessage = DetectPhaseFromMessage(message);
                        if (phaseFromMessage != TerraformPhase.Unknown)
                        {
                            return phaseFromMessage;
                        }
                    }
                }

                // 2. Проверяем поле "message" (альтернативное имя)
                if (root.TryGetProperty("message", out var altMessageElement) &&
                    altMessageElement.ValueKind == JsonValueKind.String)
                {
                    var message = altMessageElement.GetString();
                    if (!string.IsNullOrEmpty(message))
                    {
                        var phaseFromMessage = DetectPhaseFromMessage(message);
                        if (phaseFromMessage != TerraformPhase.Unknown)
                        {
                            return phaseFromMessage;
                        }
                    }
                }

                // 3. Проверяем поле "type" для явного указания фазы
                if (root.TryGetProperty("type", out var typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String)
                {
                    var type = typeElement.GetString();
                    var phaseFromType = ParsePhaseFromType(type);
                    if (phaseFromType != TerraformPhase.Unknown)
                    {
                        return phaseFromType;
                    }
                }

                // 4. Проверяем начало операции в бекенде
                if (root.TryGetProperty("@message", out var backendMessageElement) &&
                    backendMessageElement.ValueKind == JsonValueKind.String)
                {
                    var message = backendMessageElement.GetString();
                    if (!string.IsNullOrEmpty(message) && message.Contains("backend/local: starting"))
                    {
                        if (message.Contains("Apply operation"))
                            return TerraformPhase.Apply;
                        else if (message.Contains("Plan operation"))
                            return TerraformPhase.Plan;
                        else if (message.Contains("Destroy operation"))
                            return TerraformPhase.Destroy;
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse JSON for phase detection");
            }

            return TerraformPhase.Unknown;
        }

        private TerraformPhase DetectPhaseFromMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return TerraformPhase.Unknown;

            // Ищем CLI args в сообщении
            if (message.Contains("CLI args:") || message.Contains("CLI command args:"))
            {
                if (message.Contains("\"terraform\", \"apply\"") ||
                    message.Contains("[]string{\"apply\"") ||
                    message.Contains("[\"apply\""))
                {
                    _logger.LogDebug("Detected Apply phase from CLI args: {Message}",
                        GetShortMessage(message));
                    return TerraformPhase.Apply;
                }
                else if (message.Contains("\"terraform\", \"plan\"") ||
                         message.Contains("[]string{\"plan\"") ||
                         message.Contains("[\"plan\""))
                {
                    _logger.LogDebug("Detected Plan phase from CLI args: {Message}",
                        GetShortMessage(message));
                    return TerraformPhase.Plan;
                }
                else if (message.Contains("\"terraform\", \"init\"") ||
                         message.Contains("[]string{\"init\"") ||
                         message.Contains("[\"init\""))
                {
                    _logger.LogDebug("Detected Init phase from CLI args: {Message}",
                        GetShortMessage(message));
                    return TerraformPhase.Init;
                }
                else if (message.Contains("\"terraform\", \"destroy\"") ||
                         message.Contains("[]string{\"destroy\"") ||
                         message.Contains("[\"destroy\""))
                {
                    _logger.LogDebug("Detected Destroy phase from CLI args: {Message}",
                        GetShortMessage(message));
                    return TerraformPhase.Destroy;
                }
            }

            // Ищем другие признаки фазы в сообщении
            if (message.Contains("backend/local: starting Apply operation") ||
                message.Contains("Applying...") ||
                message.Contains("Apply complete!") ||
                message.Contains("apply_start") ||
                message.Contains("apply_complete"))
            {
                return TerraformPhase.Apply;
            }
            else if (message.Contains("Running plan") ||
                     message.Contains("Plan:") ||
                     message.Contains("planned_change") ||
                     message.Contains("change_summary"))
            {
                return TerraformPhase.Plan;
            }
            else if (message.Contains("Initializing the backend") ||
                     message.Contains("Terraform has been successfully initialized") ||
                     message.Contains("Initializing provider plugins"))
            {
                return TerraformPhase.Init;
            }
            else if (message.Contains("Destroying...") ||
                     message.Contains("Destruction complete") ||
                     message.Contains("destroy_start"))
            {
                return TerraformPhase.Destroy;
            }
            else if (message.Contains("Refreshing state") ||
                     message.Contains("Refresh complete") ||
                     message.Contains("refresh_start"))
            {
                return TerraformPhase.Refresh;
            }

            return TerraformPhase.Unknown;
        }

        private TerraformPhase DetectPhaseFromText(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return TerraformPhase.Unknown;

            var trimmedLine = line.Trim();

            // Ищем прямые команды terraform в текстовых логах
            if (trimmedLine.Contains("terraform apply", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Contains("Running apply", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Contains("Applying...", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.Contains("Apply complete!", StringComparison.OrdinalIgnoreCase))
            {
                return TerraformPhase.Apply;
            }
            else if (trimmedLine.Contains("terraform plan", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains("Running plan", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains("Plan:", StringComparison.OrdinalIgnoreCase))
            {
                return TerraformPhase.Plan;
            }
            else if (trimmedLine.Contains("terraform init", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains("Initializing", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains("Terraform has been successfully initialized", StringComparison.OrdinalIgnoreCase))
            {
                return TerraformPhase.Init;
            }
            else if (trimmedLine.Contains("terraform destroy", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains("Destroying...", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains("Destruction complete", StringComparison.OrdinalIgnoreCase))
            {
                return TerraformPhase.Destroy;
            }
            else if (trimmedLine.Contains("Refreshing state", StringComparison.OrdinalIgnoreCase) ||
                     trimmedLine.Contains("Refresh complete", StringComparison.OrdinalIgnoreCase))
            {
                return TerraformPhase.Refresh;
            }

            return TerraformPhase.Unknown;
        }

        private string GetShortMessage(string message)
        {
            return message.Length > 100 ? message.Substring(0, 100) + "..." : message;
        }

        // Остальные методы остаются без изменений (SaveEntriesInBatchesAsync, SaveBatchAsync, ParseTextLogLine, ParseJsonLogLine и т.д.)
        private async Task SaveEntriesInBatchesAsync(IAsyncEnumerable<LogEntry> entries, Guid logFileId)
        {
            const int batchSize = 1000;
            var batch = new List<LogEntry>(batchSize);
            var totalSaved = 0;

            await foreach (var entry in entries)
            {
                if (!IsValidEntry(entry, logFileId))
                {
                    _logger.LogWarning("Skipping invalid entry: {EntryId}", entry.Id);
                    continue;
                }

                batch.Add(entry);

                if (batch.Count >= batchSize)
                {
                    await SaveBatchAsync(batch);
                    totalSaved += batch.Count;
                    _logger.LogInformation("Saved batch of {BatchSize} entries, total: {TotalSaved}", batch.Count, totalSaved);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await SaveBatchAsync(batch);
                totalSaved += batch.Count;
                _logger.LogInformation("Saved final batch of {BatchSize} entries, total: {TotalSaved}", batch.Count, totalSaved);
            }
        }

        private async Task SaveBatchAsync(List<LogEntry> batch)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                var sql = new StringBuilder();
                var parameters = new List<NpgsqlParameter>();
                var paramIndex = 0;

                sql.AppendLine("INSERT INTO \"LogEntries\" (\"Id\", \"LogFileId\", \"Timestamp\", \"ParsedTimestamp\", \"Level\", \"RawMessage\", \"TfReqId\", \"TfResourceType\", \"TfResourceName\", \"Phase\", \"HttpReqBody\", \"HttpResBody\", \"HttpMethod\", \"HttpUrl\", \"HttpStatusCode\", \"Status\", \"SourceFile\", \"LineNumber\") VALUES ");

                for (int i = 0; i < batch.Count; i++)
                {
                    var entry = batch[i];
                    if (i > 0) sql.AppendLine(",");

                    sql.Append($"(@p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++}, @p{paramIndex++})");

                    var httpReqBody = ValidateAndFormatJsonForDb(entry.HttpReqBody);
                    var httpResBody = ValidateAndFormatJsonForDb(entry.HttpResBody);

                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.Id));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.LogFileId));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.Timestamp ?? (object)DBNull.Value));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.ParsedTimestamp));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", (int)entry.Level));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.RawMessage ?? ""));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.TfReqId ?? (object)DBNull.Value));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.TfResourceType ?? (object)DBNull.Value));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.TfResourceName ?? (object)DBNull.Value));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", (int)entry.Phase));

                    var reqBodyParam = new NpgsqlParameter($"p{parameters.Count}", httpReqBody ?? (object)DBNull.Value);
                    reqBodyParam.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
                    parameters.Add(reqBodyParam);

                    var resBodyParam = new NpgsqlParameter($"p{parameters.Count}", httpResBody ?? (object)DBNull.Value);
                    resBodyParam.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
                    parameters.Add(resBodyParam);

                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.HttpMethod ?? (object)DBNull.Value));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.HttpUrl ?? (object)DBNull.Value));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.HttpStatusCode ?? (object)DBNull.Value));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", (int)entry.Status));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.SourceFile ?? ""));
                    parameters.Add(new NpgsqlParameter($"p{parameters.Count}", entry.LineNumber));
                }

                var paramArray = parameters.Cast<object>().ToArray();
                await _context.Database.ExecuteSqlRawAsync(sql.ToString(), paramArray);
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save batch of {BatchSize} entries", batch.Count);
                throw;
            }
        }

        private bool IsValidEntry(LogEntry entry, Guid logFileId)
        {
            if (entry.Id == Guid.Empty) return false;
            if (string.IsNullOrWhiteSpace(entry.RawMessage)) return false;
            if (entry.LogFileId != logFileId) return false;
            if (entry.LineNumber <= 0) return false;
            return true;
        }

        private void UpdateStats(ParseStats stats, LogEntry entry)
        {
            stats.TotalEntries++;
            if (entry.Level == LogLevel.Error)
                stats.ErrorCount++;
            else if (entry.Level == LogLevel.Warn)
                stats.WarningCount++;
        }

        private LogEntry ParseTextLogLine(string line, int lineNumber, TerraformPhase currentPhase, Guid logFileId)
        {
            var entry = new LogEntry
            {
                Id = Guid.NewGuid(),
                LogFileId = logFileId,
                RawMessage = line,
                LineNumber = lineNumber,
                SourceFile = "imported.log",
                Phase = currentPhase, // Применяем текущую фазу ко всем записям
                ParsedTimestamp = DateTime.UtcNow,
                Status = EntryStatus.Unread
            };

            ParseTimestamp(line, entry);
            ParseLogLevel(line, entry);
            ParseTerraformFields(line, entry);
            ParseHttpData(line, entry);

            return entry;
        }

        private LogEntry? ParseJsonLogLine(string jsonLine, Guid logFileId, int lineNumber, TerraformPhase currentPhase)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonLine);
                var root = document.RootElement;

                var entry = new LogEntry
                {
                    Id = Guid.NewGuid(),
                    LogFileId = logFileId,
                    LineNumber = lineNumber,
                    Phase = currentPhase, // Применяем текущую фазу ко всем записям
                    ParsedTimestamp = DateTime.UtcNow,
                    SourceFile = "terraform.json",
                    Status = EntryStatus.Unread
                };

                // Парсим основные поля
                ParseJsonField(root, "@timestamp", value => {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        var timestampStr = value.GetString();
                        if (DateTime.TryParse(timestampStr, out var timestamp))
                        {
                            entry.Timestamp = NormalizeDateTime(timestamp);
                        }
                    }
                });

                ParseJsonField(root, "@level", value => {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        entry.Level = ParseLogLevelFromString(value.GetString());
                    }
                });

                ParseJsonField(root, "@message", value => {
                    if (value.ValueKind == JsonValueKind.String)
                        entry.RawMessage = value.GetString() ?? string.Empty;
                });

                // Альтернативные имена полей
                if (entry.Level == LogLevel.Unknown)
                {
                    ParseJsonField(root, "level", value => {
                        if (value.ValueKind == JsonValueKind.String)
                        {
                            entry.Level = ParseLogLevelFromString(value.GetString());
                        }
                    });
                }

                if (string.IsNullOrEmpty(entry.RawMessage))
                {
                    ParseJsonField(root, "message", value => {
                        if (value.ValueKind == JsonValueKind.String)
                            entry.RawMessage = value.GetString() ?? string.Empty;
                    });
                }

                if (string.IsNullOrEmpty(entry.RawMessage))
                    entry.RawMessage = jsonLine;

                // Парсим остальные поля...
                ParseJsonField(root, "tf_req_id", value => {
                    if (value.ValueKind == JsonValueKind.String)
                        entry.TfReqId = value.GetString();
                });

                ParseJsonField(root, "tf_resource_type", value => {
                    if (value.ValueKind == JsonValueKind.String)
                        entry.TfResourceType = value.GetString();
                });

                ParseJsonField(root, "tf_resource_name", value => {
                    if (value.ValueKind == JsonValueKind.String)
                        entry.TfResourceName = value.GetString();
                });

                ParseJsonField(root, "@module", value => {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        var module = value.GetString();
                        if (!string.IsNullOrEmpty(module))
                        {
                            entry.RawMessage = $"[{module}] {entry.RawMessage}";
                        }
                    }
                });

                return entry;
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse JSON line {LineNumber}", lineNumber);
                return ParseTextLogLine(jsonLine, lineNumber, currentPhase, logFileId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error parsing line {LineNumber}", lineNumber);
                return CreateErrorEntry(jsonLine, logFileId, lineNumber, "Parse error");
            }
        }

        private LogEntry CreateErrorEntry(string line, Guid logFileId, int lineNumber, string errorMessage)
        {
            return new LogEntry
            {
                Id = Guid.NewGuid(),
                LogFileId = logFileId,
                RawMessage = $"{errorMessage}: {line}",
                LineNumber = lineNumber,
                Level = LogLevel.Error,
                Phase = TerraformPhase.Unknown,
                ParsedTimestamp = DateTime.UtcNow,
                SourceFile = "error",
                Status = EntryStatus.Unread
            };
        }

        // Остальные вспомогательные методы без изменений...
        private DateTime? NormalizeDateTime(DateTime timestamp)
        {
            if (timestamp.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            else if (timestamp.Kind == DateTimeKind.Local)
                return timestamp.ToUniversalTime();
            else
                return timestamp;
        }

        private LogLevel ParseLogLevelFromString(string? levelStr)
        {
            return levelStr?.ToUpperInvariant() switch
            {
                "ERROR" => LogLevel.Error,
                "WARN" or "WARNING" => LogLevel.Warn,
                "INFO" => LogLevel.Info,
                "DEBUG" => LogLevel.Debug,
                "TRACE" => LogLevel.Trace,
                _ => LogLevel.Unknown
            };
        }

        private TerraformPhase ParsePhaseFromType(string? typeStr)
        {
            if (string.IsNullOrEmpty(typeStr))
                return TerraformPhase.Unknown;

            return typeStr.ToUpperInvariant() switch
            {
                "PLAN" or "PLANNED_CHANGE" or "CHANGE_SUMMARY" => TerraformPhase.Plan,
                "APPLY" or "APPLY_START" or "APPLY_COMPLETE" or "APPLY_ERROR" or "APPLY_PROGRESS" => TerraformPhase.Apply,
                "PROVISION_PROGRESS" or "PROVISIONER_COMPLETED" or "PROVISIONER_STARTED" => TerraformPhase.Apply,
                "REFRESH_COMPLETE" or "REFRESH_START" => TerraformPhase.Refresh,
                "INIT" => TerraformPhase.Init,
                "DESTROY" => TerraformPhase.Destroy,
                _ => TerraformPhase.Unknown
            };
        }

        private void ParseTimestamp(string line, LogEntry entry)
        {
            var timestampPatterns = new[]
            {
                new Regex(@"(?<timestamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z?)"),
                new Regex(@"(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})"),
                new Regex(@"(?<timestamp>\d{2}:\d{2}:\d{2}\.\d{3})"),
                new Regex(@"(?<timestamp>\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})")
            };

            foreach (var pattern in timestampPatterns)
            {
                var match = pattern.Match(line);
                if (match.Success)
                {
                    var timestampString = match.Groups["timestamp"].Value;
                    if (DateTime.TryParse(timestampString, out var timestamp))
                    {
                        entry.Timestamp = NormalizeDateTime(timestamp);
                        break;
                    }
                }
            }
        }

        private void ParseLogLevel(string line, LogEntry entry)
        {
            var levelPatterns = new Dictionary<Regex, LogLevel>
            {
                { new Regex(@"(?i)\[ERROR\]|\bERROR\b|\berror\b"), LogLevel.Error },
                { new Regex(@"(?i)\[WARN\]|\bWARN\b|\bwarning\b|\bwarn\b"), LogLevel.Warn },
                { new Regex(@"(?i)\[INFO\]|\bINFO\b|\binfo\b"), LogLevel.Info },
                { new Regex(@"(?i)\[DEBUG\]|\bDEBUG\b|\bdebug\b"), LogLevel.Debug },
                { new Regex(@"(?i)\[TRACE\]|\bTRACE\b|\btrace\b"), LogLevel.Trace }
            };

            foreach (var (pattern, level) in levelPatterns)
            {
                if (pattern.IsMatch(line))
                {
                    entry.Level = level;
                    return;
                }
            }

            entry.Level = DetermineLevelHeuristic(line);
        }

        private LogLevel DetermineLevelHeuristic(string line)
        {
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Error;

            if (line.Contains("warn", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Warn;

            if (line.Contains("debug", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Debug;

            return LogLevel.Info;
        }

        private void ParseTerraformFields(string line, LogEntry entry)
        {
            var tfReqIdRegex = new Regex(@"tf_req_id[\s=:]+([\w-]+)");
            var tfResourceTypeRegex = new Regex(@"tf_resource_type[\s=:]+([\w\._]+)");
            var tfResourceNameRegex = new Regex(@"tf_resource_name[\s=:]+([\w\._-]+)");

            var reqIdMatch = tfReqIdRegex.Match(line);
            if (reqIdMatch.Success) entry.TfReqId = reqIdMatch.Groups[1].Value;

            var resourceTypeMatch = tfResourceTypeRegex.Match(line);
            if (resourceTypeMatch.Success) entry.TfResourceType = resourceTypeMatch.Groups[1].Value;

            var resourceNameMatch = tfResourceNameRegex.Match(line);
            if (resourceNameMatch.Success) entry.TfResourceName = resourceNameMatch.Groups[1].Value;
        }

        private void ParseHttpData(string line, LogEntry entry)
        {
            var httpReqBodyRegex = new Regex(@"tf_http_req_body[\s=:]+(?<json>\{.*\})", RegexOptions.Singleline);
            var httpResBodyRegex = new Regex(@"tf_http_res_body[\s=:]+(?<json>\{.*\})", RegexOptions.Singleline);
            var httpMethodRegex = new Regex(@"tf_http_method[\s=:]+(\w+)");
            var httpUrlRegex = new Regex(@"tf_http_url[\s=:]+(\S+)");
            var httpStatusCodeRegex = new Regex(@"tf_http_status_code[\s=:]+(\d+)");

            var reqBodyMatch = httpReqBodyRegex.Match(line);
            if (reqBodyMatch.Success && IsValidJson(reqBodyMatch.Groups["json"].Value))
                entry.HttpReqBody = FormatJson(reqBodyMatch.Groups["json"].Value);

            var resBodyMatch = httpResBodyRegex.Match(line);
            if (resBodyMatch.Success && IsValidJson(resBodyMatch.Groups["json"].Value))
                entry.HttpResBody = FormatJson(resBodyMatch.Groups["json"].Value);

            var methodMatch = httpMethodRegex.Match(line);
            if (methodMatch.Success) entry.HttpMethod = methodMatch.Groups[1].Value;

            var urlMatch = httpUrlRegex.Match(line);
            if (urlMatch.Success) entry.HttpUrl = urlMatch.Groups[1].Value;

            var statusMatch = httpStatusCodeRegex.Match(line);
            if (statusMatch.Success && int.TryParse(statusMatch.Groups[1].Value, out var statusCode))
                entry.HttpStatusCode = statusCode;
        }

        private void ParseJsonField(JsonElement root, string fieldName, Action<JsonElement> action)
        {
            if (root.TryGetProperty(fieldName, out var element))
            {
                try
                {
                    action(element);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error parsing JSON field {FieldName}", fieldName);
                }
            }
        }

        private bool IsValidJson(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString)) return false;
            try
            {
                JsonDocument.Parse(jsonString);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private string? ValidateAndFormatJsonForDb(string? jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return null;
            try
            {
                using var document = JsonDocument.Parse(jsonString);
                return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Invalid JSON found and will be skipped: {Error}", ex.Message);
                return null;
            }
        }

        private string? ValidateAndFormatJson(string? jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return null;
            try
            {
                using var document = JsonDocument.Parse(jsonString);
                return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
            }
            catch (JsonException)
            {
                _logger.LogDebug("Invalid JSON found: {JsonString}", GetShortMessage(jsonString));
                return null;
            }
        }

        private string FormatJson(string jsonString)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonString);
                return JsonSerializer.Serialize(document, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch (JsonException)
            {
                return jsonString;
            }
        }

        private class ParseStats
        {
            public int TotalEntries { get; set; }
            public int ErrorCount { get; set; }
            public int WarningCount { get; set; }
        }
    }
}