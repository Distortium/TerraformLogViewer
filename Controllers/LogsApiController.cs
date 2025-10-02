using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TerraformLogViewer.Models;
using TerraformLogViewer.Services;
using LogLevel = TerraformLogViewer.Models.LogLevel;

namespace TerraformLogViewer.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class LogsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly LogParserService _logParserService;
        private readonly VisualizationService _visualizationService;
        private readonly ILogger<LogsController> _logger;

        public LogsController(
            AppDbContext context,
            LogParserService logParserService,
            VisualizationService visualizationService,
            ILogger<LogsController> logger)
        {
            _context = context;
            _logParserService = logParserService;
            _visualizationService = visualizationService;
            _logger = logger;
        }

        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<UploadLogResponse>> UploadLogFile(
            IFormFile file,
            [FromForm] string fileName,
            [FromForm] string? fileType = "Text",
            [FromForm] string? userId = null)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new UploadLogResponse
                    {
                        Status = "Error",
                        Message = "No file provided"
                    });
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = file.FileName;
                }

                var user = await GetOrCreateUser(userId);

                using var stream = file.OpenReadStream();
                var logFile = await _logParserService.ParseAndStoreLogsAsync(
                    stream, fileName, user.Id, fileType ?? "Text");

                var response = new UploadLogResponse
                {
                    LogFileId = logFile.Id,
                    FileName = logFile.FileName,
                    FileSize = logFile.FileSize,
                    TotalEntries = logFile.TotalEntries,
                    ErrorCount = logFile.ErrorCount,
                    WarningCount = logFile.WarningCount,
                    UploadedAt = logFile.UploadedAt,
                    Status = "Success",
                    Message = $"Successfully parsed {logFile.TotalEntries} log entries"
                };

                // Отправка webhook уведомления
                await SendWebhookNotification("log_file_uploaded", logFile.Id, response);

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading log file");
                return StatusCode(500, new UploadLogResponse
                {
                    Status = "Error",
                    Message = $"Internal server error: {ex.Message}"
                });
            }
        }

        [HttpGet("{logFileId:guid}")]
        public async Task<ActionResult<LogFile>> GetLogFile(Guid logFileId)
        {
            var logFile = await _context.LogFiles
                .Include(lf => lf.User)
                .FirstOrDefaultAsync(lf => lf.Id == logFileId);

            if (logFile == null)
            {
                return NotFound();
            }

            return Ok(logFile);
        }

        [HttpPost("{logFileId:guid}/search")]
        public async Task<ActionResult<SearchLogsResponse>> SearchLogs(
            Guid logFileId,
            [FromBody] SearchLogsRequest request)
        {
            try
            {
                var searchCriteria = new VisualizationService.SearchCriteria
                {
                    FreeText = request.FreeText,
                    TfResourceType = request.TfResourceType,
                    MinLogLevel = request.MinLogLevel,
                    Phase = request.Phase,
                    StartTimestamp = request.StartTimestamp,
                    EndTimestamp = request.EndTimestamp,
                    UnreadOnly = request.UnreadOnly
                };

                var allResults = await _visualizationService.SearchLogsAsync(logFileId, searchCriteria);

                // Применяем пагинацию
                var pagedResults = allResults
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                var statistics = new StatisticsDto
                {
                    TotalEntries = allResults.Count,
                    ErrorCount = allResults.Count(e => e.Level == LogLevel.Error),
                    WarningCount = allResults.Count(e => e.Level == LogLevel.Warn),
                    UnreadCount = allResults.Count(e => e.Status == EntryStatus.Unread),
                    PhaseDistribution = allResults
                        .GroupBy(e => e.Phase)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    LevelDistribution = allResults
                        .GroupBy(e => e.Level)
                        .ToDictionary(g => g.Key, g => g.Count())
                };

                var response = new SearchLogsResponse
                {
                    Entries = pagedResults.Select(e => MapToDto(e)).ToList(),
                    TotalCount = allResults.Count,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling((double)allResults.Count / request.PageSize),
                    Statistics = statistics
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching logs for file {LogFileId}", logFileId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{logFileId:guid}/entries/{entryId:guid}")]
        public async Task<ActionResult<LogEntryDto>> GetLogEntry(Guid logFileId, Guid entryId)
        {
            var entry = await _context.LogEntries
                .FirstOrDefaultAsync(e => e.Id == entryId && e.LogFileId == logFileId);

            if (entry == null)
            {
                return NotFound();
            }

            return Ok(MapToDto(entry));
        }

        [HttpPost("{logFileId:guid}/entries/{entryId:guid}/mark-read")]
        public async Task<ActionResult> MarkAsRead(Guid logFileId, Guid entryId)
        {
            try
            {
                await _visualizationService.MarkAsReadAsync(entryId);
                return Ok(new { status = "success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking entry as read");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{logFileId:guid}/entries/{entryId:guid}/mark-unread")]
        public async Task<ActionResult> MarkAsUnread(Guid logFileId, Guid entryId)
        {
            try
            {
                await _visualizationService.MarkAsUnreadAsync(entryId);
                return Ok(new { status = "success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking entry as unread");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{logFileId:guid}/statistics")]
        public async Task<ActionResult<StatisticsDto>> GetStatistics(Guid logFileId)
        {
            try
            {
                var entries = await _context.LogEntries
                    .Where(e => e.LogFileId == logFileId)
                    .ToListAsync();

                var statistics = new StatisticsDto
                {
                    TotalEntries = entries.Count,
                    ErrorCount = entries.Count(e => e.Level == LogLevel.Error),
                    WarningCount = entries.Count(e => e.Level == LogLevel.Warn),
                    UnreadCount = entries.Count(e => e.Status == EntryStatus.Unread),
                    PhaseDistribution = entries
                        .GroupBy(e => e.Phase)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    LevelDistribution = entries
                        .GroupBy(e => e.Level)
                        .ToDictionary(g => g.Key, g => g.Count())
                };

                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting statistics for file {LogFileId}", logFileId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{logFileId:guid}/alerts")]
        public async Task<ActionResult<AlertResponse>> CreateAlert(
            Guid logFileId,
            [FromBody] AlertRequest request)
        {
            try
            {
                // Здесь можно интегрировать внешние системы

                var alertResponse = new AlertResponse
                {
                    AlertId = request.AlertId,
                    Status = "created",
                    CreatedAt = DateTime.UtcNow,
                    Message = "Alert created successfully"
                };

                // Отправка уведомления через webhook
                await SendWebhookNotification("alert_created", logFileId, new
                {
                    Alert = request,
                    Response = alertResponse
                });

                return Ok(alertResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating alert for file {LogFileId}", logFileId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{logFileId:guid}/timeline")]
        public async Task<ActionResult> GetTimeline(Guid logFileId)
        {
            try
            {
                var timelineData = await _visualizationService.GenerateTimelineAsync(logFileId);
                return Ok(timelineData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating timeline for file {LogFileId}", logFileId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<Models.User> GetOrCreateUser(string? userId)
        {
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
            {
                var existingUser = await _context.Users.FindAsync(userGuid);
                if (existingUser != null)
                {
                    return existingUser;
                }
            }

            var user = new Models.User
            {
                Id = Guid.NewGuid(),
                Email = "api@example.com",
                CreatedAt = DateTime.UtcNow
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            return user;
        }

        private LogEntryDto MapToDto(LogEntry entry)
        {
            object? httpReqBody = null;
            object? httpResBody = null;

            if (!string.IsNullOrEmpty(entry.HttpReqBody))
            {
                try
                {
                    httpReqBody = JsonSerializer.Deserialize<object>(entry.HttpReqBody);
                }
                catch (JsonException)
                {
                    httpReqBody = entry.HttpReqBody;
                }
            }

            if (!string.IsNullOrEmpty(entry.HttpResBody))
            {
                try
                {
                    httpResBody = JsonSerializer.Deserialize<object>(entry.HttpResBody);
                }
                catch (JsonException)
                {
                    httpResBody = entry.HttpResBody;
                }
            }

            return new LogEntryDto
            {
                Id = entry.Id,
                LogFileId = entry.LogFileId,
                Timestamp = entry.Timestamp,
                Level = entry.Level,
                RawMessage = entry.RawMessage,
                TfReqId = entry.TfReqId,
                TfResourceType = entry.TfResourceType,
                TfResourceName = entry.TfResourceName,
                Phase = entry.Phase,
                HttpMethod = entry.HttpMethod,
                HttpUrl = entry.HttpUrl,
                HttpStatusCode = entry.HttpStatusCode,
                Status = entry.Status,
                SourceFile = entry.SourceFile,
                LineNumber = entry.LineNumber,
                HttpReqBody = httpReqBody,
                HttpResBody = httpResBody
            };
        }

        private async Task SendWebhookNotification(string eventType, Guid logFileId, object data)
        {
            try
            {
                var notification = new WebhookNotification
                {
                    EventType = eventType,
                    LogFileId = logFileId,
                    Timestamp = DateTime.UtcNow,
                    Data = data
                };

                _logger.LogInformation("Webhook notification: {EventType} for log file {LogFileId}",
                    eventType, logFileId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send webhook notification");
            }
        }
    }
}