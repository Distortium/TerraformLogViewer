using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using TerraformLogViewer.Models;
using LogLevel = TerraformLogViewer.Models.LogLevel;

namespace TerraformLogViewer.Services
{
    public class IntegrationService
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _context;
        private readonly ILogger<IntegrationService> _logger;

        public IntegrationService(
            HttpClient httpClient,
            AppDbContext context,
            ILogger<IntegrationService> logger)
        {
            _httpClient = httpClient;
            _context = context;
            _logger = logger;
        }

        public async Task<bool> SendToSlack(Guid logFileId, string webhookUrl, string message)
        {
            try
            {
                var statistics = await GetStatistics(logFileId);

                var slackMessage = new
                {
                    text = message,
                    blocks = new object[]
                    {
                        new { type = "header", text = new { type = "plain_text", text = "Terraform Log Alert" } },
                        new { type = "section", text = new { type = "mrkdwn", text = message } },
                        new { type = "section", fields = new object[]
                            {
                                new { type = "mrkdwn", text = $"*Total Entries:* {statistics.TotalEntries}" },
                                new { type = "mrkdwn", text = $"*Errors:* {statistics.ErrorCount}" },
                                new { type = "mrkdwn", text = $"*Warnings:* {statistics.WarningCount}" },
                                new { type = "mrkdwn", text = $"*Unread:* {statistics.UnreadCount}" }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(slackMessage);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(webhookUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to Slack");
                return false;
            }
        }

        public async Task<bool> SendToPagerDuty(Guid logFileId, string routingKey, string summary)
        {
            try
            {
                var statistics = await GetStatistics(logFileId);

                var pagerDutyEvent = new
                {
                    routing_key = routingKey,
                    event_action = "trigger",
                    dedup_key = logFileId.ToString(),
                    payload = new
                    {
                        summary = summary,
                        source = "terraform-log-analyzer",
                        severity = statistics.ErrorCount > 0 ? "error" : "warning",
                        custom_details = new
                        {
                            log_file_id = logFileId,
                            total_entries = statistics.TotalEntries,
                            errors = statistics.ErrorCount,
                            warnings = statistics.WarningCount,
                            phase_distribution = statistics.PhaseDistribution
                        }
                    }
                };

                var json = JsonSerializer.Serialize(pagerDutyEvent);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://events.pagerduty.com/v2/enqueue", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending event to PagerDuty");
                return false;
            }
        }

        public async Task<bool> SendToWebhook(Guid logFileId, string webhookUrl, object customData = null)
        {
            try
            {
                var statistics = await GetStatistics(logFileId);
                var logFile = await _context.LogFiles.FindAsync(logFileId);

                var webhookData = new
                {
                    event_type = "terraform_log_analysis",
                    log_file = new
                    {
                        id = logFileId,
                        name = logFile?.FileName,
                        uploaded_at = logFile?.UploadedAt
                    },
                    statistics = statistics,
                    custom_data = customData,
                    timestamp = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(webhookData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(webhookUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending data to webhook");
                return false;
            }
        }

        private async Task<StatisticsDto> GetStatistics(Guid logFileId)
        {
            var entries = await _context.LogEntries
                .Where(e => e.LogFileId == logFileId)
                .ToListAsync();

            return new StatisticsDto
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
        }
    }
}