using Grpc.Net.Client;
using TerraformLogViewer.Grpc;
using LogEntry = TerraformLogViewer.Models.LogEntry;

namespace TerraformLogViewer.Services
{
    public class PluginHostService
    {
        private readonly ILogger<PluginHostService> _logger;
        private readonly List<PluginEndpoint> _plugins = new();
        private readonly HttpClient _httpClient;

        public PluginHostService(ILogger<PluginHostService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public void RegisterPlugin(string name, string endpoint, PluginType type)
        {
            var plugin = new PluginEndpoint
            {
                Name = name,
                Endpoint = endpoint,
                Type = type,
                IsHealthy = false
            };

            _plugins.Add(plugin);
            _logger.LogInformation("Registered plugin: {Name} at {Endpoint}", name, endpoint);
        }

        public async Task<List<PluginEndpoint>> GetPluginsAsync()
        {
            // Проверяем здоровье всех плагинов
            foreach (var plugin in _plugins)
            {
                plugin.IsHealthy = await CheckPluginHealthAsync(plugin);
            }

            return _plugins;
        }

        public async Task<List<LogEntry>> ApplyFiltersAsync(List<LogEntry> entries, string pluginName = null)
        {
            var filteredEntries = entries;
            var pluginsToUse = _plugins.Where(p => p.IsHealthy && p.Type == PluginType.Filter);

            if (!string.IsNullOrEmpty(pluginName))
            {
                pluginsToUse = pluginsToUse.Where(p => p.Name == pluginName);
            }

            foreach (var plugin in pluginsToUse)
            {
                try
                {
                    using var channel = GrpcChannel.ForAddress(plugin.Endpoint);
                    var client = new LogAnalyzerPlugin.LogAnalyzerPluginClient(channel);

                    var request = new FilterRequest
                    {
                        Entries = { filteredEntries.Select(MapToGrpc) }
                    };

                    var response = await client.FilterLogsAsync(request);
                    filteredEntries = response.FilteredEntries.Select(MapFromGrpc).ToList();

                    _logger.LogDebug("Applied filter plugin {PluginName}, remaining entries: {Count}",
                        plugin.Name, filteredEntries.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply filter plugin {PluginName}", plugin.Name);
                }
            }

            return filteredEntries;
        }

        public async Task<List<LogEntry>> ProcessLogsAsync(List<LogEntry> entries, string pluginName = null)
        {
            var processedEntries = entries;
            var pluginsToUse = _plugins.Where(p => p.IsHealthy && p.Type == PluginType.Processor);

            if (!string.IsNullOrEmpty(pluginName))
            {
                pluginsToUse = pluginsToUse.Where(p => p.Name == pluginName);
            }

            foreach (var plugin in pluginsToUse)
            {
                try
                {
                    using var channel = GrpcChannel.ForAddress(plugin.Endpoint);
                    var client = new LogAnalyzerPlugin.LogAnalyzerPluginClient(channel);

                    var request = new ProcessRequest
                    {
                        Entries = { processedEntries.Select(MapToGrpc) }
                    };

                    var response = await client.ProcessLogsAsync(request);
                    processedEntries = response.ProcessedEntries.Select(MapFromGrpc).ToList();

                    _logger.LogDebug("Applied processor plugin {PluginName}", plugin.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply processor plugin {PluginName}", plugin.Name);
                }
            }

            return processedEntries;
        }

        public async Task<List<ErrorGroup>> AggregateErrorsAsync(List<LogEntry> entries, string timeWindow = "1h")
        {
            var allErrorGroups = new List<ErrorGroup>();
            var pluginsToUse = _plugins.Where(p => p.IsHealthy && p.Type == PluginType.Aggregator);

            foreach (var plugin in pluginsToUse)
            {
                try
                {
                    using var channel = GrpcChannel.ForAddress(plugin.Endpoint);
                    var client = new LogAnalyzerPlugin.LogAnalyzerPluginClient(channel);

                    var request = new AggregateRequest
                    {
                        Entries = { entries.Select(MapToGrpc) },
                        TimeWindow = timeWindow
                    };

                    var response = await client.AggregateErrorsAsync(request);
                    allErrorGroups.AddRange(response.ErrorGroups);

                    _logger.LogDebug("Applied aggregator plugin {PluginName}, found {Count} error groups",
                        plugin.Name, response.ErrorGroups.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply aggregator plugin {PluginName}", plugin.Name);
                }
            }

            return allErrorGroups;
        }

        private async Task<bool> CheckPluginHealthAsync(PluginEndpoint plugin)
        {
            try
            {
                using var channel = GrpcChannel.ForAddress(plugin.Endpoint);
                var client = new LogAnalyzerPlugin.LogAnalyzerPluginClient(channel);

                var response = await client.HealthCheckAsync(new HealthCheckRequest());
                return response.Status == "healthy";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health check failed for plugin {PluginName}", plugin.Name);
                return false;
            }
        }

        private Grpc.LogEntry MapToGrpc(Models.LogEntry entry)
        {
            return new Grpc.LogEntry
            {
                Id = entry.Id.ToString(),
                LogFileId = entry.LogFileId.ToString(),
                Timestamp = entry.Timestamp?.ToString("O") ?? "",
                Level = (Grpc.LogLevel)entry.Level,
                RawMessage = entry.RawMessage,
                TfReqId = entry.TfReqId ?? "",
                TfResourceType = entry.TfResourceType ?? "",
                TfResourceName = entry.TfResourceName ?? "",
                Phase = (Grpc.TerraformPhase)entry.Phase,
                HttpMethod = entry.HttpMethod ?? "",
                HttpUrl = entry.HttpUrl ?? "",
                HttpStatusCode = entry.HttpStatusCode ?? 0,
                Status = (Grpc.EntryStatus)entry.Status,
                SourceFile = entry.SourceFile ?? "",
                LineNumber = entry.LineNumber,
                HttpReqBody = entry.HttpReqBody ?? "",
                HttpResBody = entry.HttpResBody ?? ""
            };
        }

        private Models.LogEntry MapFromGrpc(Grpc.LogEntry grpcEntry)
        {
            return new Models.LogEntry
            {
                Id = Guid.Parse(grpcEntry.Id.ToString()),
                LogFileId = Guid.Parse(grpcEntry.LogFileId.ToString()),
                Timestamp = DateTime.TryParse(grpcEntry.Timestamp, out var timestamp) ? timestamp : null,
                Level = (Models.LogLevel)grpcEntry.Level,
                RawMessage = grpcEntry.RawMessage,
                TfReqId = string.IsNullOrEmpty(grpcEntry.TfReqId) ? null : grpcEntry.TfReqId,
                TfResourceType = string.IsNullOrEmpty(grpcEntry.TfResourceType) ? null : grpcEntry.TfResourceType,
                TfResourceName = string.IsNullOrEmpty(grpcEntry.TfResourceName) ? null : grpcEntry.TfResourceName,
                Phase = (Models.TerraformPhase)grpcEntry.Phase,
                HttpMethod = string.IsNullOrEmpty(grpcEntry.HttpMethod) ? null : grpcEntry.HttpMethod,
                HttpUrl = string.IsNullOrEmpty(grpcEntry.HttpUrl) ? null : grpcEntry.HttpUrl,
                HttpStatusCode = grpcEntry.HttpStatusCode == 0 ? null : grpcEntry.HttpStatusCode,
                Status = (Models.EntryStatus)grpcEntry.Status,
                SourceFile = grpcEntry.SourceFile,
                LineNumber = grpcEntry.LineNumber,
                HttpReqBody = string.IsNullOrEmpty(grpcEntry.HttpReqBody) ? null : grpcEntry.HttpReqBody,
                HttpResBody = string.IsNullOrEmpty(grpcEntry.HttpResBody) ? null : grpcEntry.HttpResBody
            };
        }
    }

    public class PluginEndpoint
    {
        public string Name { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public PluginType Type { get; set; }
        public bool IsHealthy { get; set; }
    }

    public enum PluginType
    {
        Filter,
        Processor,
        Aggregator
    }
}