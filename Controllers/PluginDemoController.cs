using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TerraformLogViewer.Services;

namespace TerraformLogViewer.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class PluginDemoController : ControllerBase
    {
        private readonly PluginHostService _pluginHostService;
        private readonly AppDbContext _context;
        private readonly ILogger<PluginDemoController> _logger;

        public PluginDemoController(
            PluginHostService pluginHostService,
            AppDbContext context,
            ILogger<PluginDemoController> logger)
        {
            _pluginHostService = pluginHostService;
            _context = context;
            _logger = logger;
        }

        [HttpPost("test-plugin/{logFileId:guid}")]
        public async Task<ActionResult> TestPlugin(Guid logFileId)
        {
            try
            {
                // Получаем записи логов
                var entries = await _context.LogEntries
                    .Where(e => e.LogFileId == logFileId)
                    .Take(100)
                    .ToListAsync();

                _logger.LogInformation("Testing plugin with {Count} entries", entries.Count);

                // 1. Тестируем фильтрацию
                var filteredEntries = await _pluginHostService.ApplyFiltersAsync(entries, "ErrorPatternAnalyzer");
                _logger.LogInformation("Filtered to {Count} error entries", filteredEntries.Count);

                // 2. Тестируем агрегацию ошибок
                var errorGroups = await _pluginHostService.AggregateErrorsAsync(entries, "1h");
                _logger.LogInformation("Found {Count} error groups", errorGroups.Count);

                // 3. Тестируем обработку
                var processedEntries = await _pluginHostService.ProcessLogsAsync(entries, "ErrorPatternAnalyzer");
                _logger.LogInformation("Processed {Count} entries", processedEntries.Count);

                var result = new
                {
                    OriginalEntries = entries.Count,
                    FilteredErrors = filteredEntries.Count,
                    ErrorGroups = errorGroups.Select(g => new
                    {
                        Pattern = g.Pattern,
                        Count = g.Count,
                        Frequency = g.FrequencyPerHour,
                        Examples = g.ExampleMessages.Take(2)
                    }),
                    ProcessedEntries = processedEntries.Count,
                    PluginStatus = "Working correctly"
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing plugin");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("plugin-status")]
        public async Task<ActionResult> GetPluginStatus()
        {
            var plugins = await _pluginHostService.GetPluginsAsync();

            var status = plugins.Select(p => new
            {
                Name = p.Name,
                Endpoint = p.Endpoint,
                Type = p.Type,
                IsHealthy = p.IsHealthy,
                LastChecked = DateTime.UtcNow
            });

            return Ok(status);
        }
    }
}