using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TerraformLogViewer.Grpc;
using TerraformLogViewer.Models;
using TerraformLogViewer.Services;

namespace TerraformLogViewer.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class PluginsController : ControllerBase
    {
        private readonly PluginHostService _pluginHostService;
        private readonly ILogger<PluginsController> _logger;
        private readonly AppDbContext _context;

        public PluginsController(PluginHostService pluginHostService, ILogger<PluginsController> logger, AppDbContext context)
        {
            _pluginHostService = pluginHostService;
            _logger = logger;
            _context = context;
        }

        [HttpPost("register")]
        public IActionResult RegisterPlugin([FromBody] RegisterPluginRequest request)
        {
            try
            {
                _pluginHostService.RegisterPlugin(request.Name, request.Endpoint, request.Type);
                return Ok(new { message = $"Plugin {request.Name} registered successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering plugin");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<ActionResult<List<PluginEndpoint>>> GetPlugins()
        {
            var plugins = await _pluginHostService.GetPluginsAsync();
            return Ok(plugins);
        }

        [HttpPost("{logFileId:guid}/filter")]
        public async Task<ActionResult<List<LogEntryDto>>> ApplyFilters(
            Guid logFileId,
            [FromBody] ApplyPluginRequest request)
        {
            try
            {
                // Получаем записи логов
                var entries = await _context.LogEntries
                    .Where(e => e.LogFileId == logFileId)
                    .ToListAsync();

                // Применяем фильтры через плагины
                var filteredEntries = await _pluginHostService.ApplyFiltersAsync(
                    entries, request.PluginName);

                return Ok(filteredEntries.Select(MapToDto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying filters for log file {LogFileId}", logFileId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{logFileId:guid}/process")]
        public async Task<ActionResult<List<LogEntryDto>>> ProcessLogs(
            Guid logFileId,
            [FromBody] ApplyPluginRequest request)
        {
            try
            {
                var entries = await _context.LogEntries
                    .Where(e => e.LogFileId == logFileId)
                    .ToListAsync();

                var processedEntries = await _pluginHostService.ProcessLogsAsync(
                    entries, request.PluginName);

                return Ok(processedEntries.Select(MapToDto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing logs for file {LogFileId}", logFileId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{logFileId:guid}/aggregate-errors")]
        public async Task<ActionResult<List<ErrorGroupDto>>> AggregateErrors(
            Guid logFileId,
            [FromQuery] string timeWindow = "1h")
        {
            try
            {
                var entries = await _context.LogEntries
                    .Where(e => e.LogFileId == logFileId)
                    .ToListAsync();

                var errorGroups = await _pluginHostService.AggregateErrorsAsync(entries, timeWindow);

                return Ok(errorGroups.Select(MapToDto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error aggregating errors for file {LogFileId}", logFileId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private LogEntryDto MapToDto(Models.LogEntry entry)
        {
            // Реализация маппинга (как в предыдущем примере)
            return new LogEntryDto();
        }

        private ErrorGroupDto MapToDto(ErrorGroup group)
        {
            return new ErrorGroupDto
            {
                Pattern = group.Pattern,
                Count = group.Count,
                FirstOccurrence = group.FirstOccurrence,
                LastOccurrence = group.LastOccurrence,
                ExampleMessages = group.ExampleMessages.ToList(),
                AffectedResources = group.AffectedResources.ToList(),
                FrequencyPerHour = group.FrequencyPerHour
            };
        }
    }

    public class RegisterPluginRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public PluginType Type { get; set; }
    }

    public class ApplyPluginRequest
    {
        public string? PluginName { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
    }

    public class ErrorGroupDto
    {
        public string Pattern { get; set; } = string.Empty;
        public int Count { get; set; }
        public string FirstOccurrence { get; set; } = string.Empty;
        public string LastOccurrence { get; set; } = string.Empty;
        public List<string> ExampleMessages { get; set; } = new();
        public List<string> AffectedResources { get; set; } = new();
        public double FrequencyPerHour { get; set; }
    }
}