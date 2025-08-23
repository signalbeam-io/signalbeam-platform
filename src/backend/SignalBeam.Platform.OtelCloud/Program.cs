using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Add OpenTelemetry self-instrumentation
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation();
    });

// Add ClickHouse connection
builder.Services.AddSingleton<ClickHouseConnection>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("clickhouse") ?? 
                          "Host=localhost;Port=8123;Username=signalbeam;Password=signalbeam_password;Database=signalbeam";
    return new ClickHouseConnection(connectionString);
});

// Add business services
builder.Services.AddSingleton<IOtelCollectorService, OtelCollectorService>();
builder.Services.AddSingleton<IMessagePublisher, RabbitMQPublisher>();
builder.Services.AddHostedService<OtelMetricsProcessingService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Simple HTTP endpoints for OTLP-like data (JSON format)
app.MapPost("/v1/traces", async (HttpContext context, IOtelCollectorService service) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        await service.ProcessTracesAsync(body);
        return Results.Ok(new { status = "success" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/v1/metrics", async (HttpContext context, IOtelCollectorService service) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        await service.ProcessMetricsAsync(body);
        return Results.Ok(new { status = "success" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/v1/logs", async (HttpContext context, IOtelCollectorService service) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        await service.ProcessLogsAsync(body);
        return Results.Ok(new { status = "success" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Batch endpoint for mixed telemetry data
app.MapPost("/v1/batch", async (HttpContext context, IOtelCollectorService service) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var batchData = JsonSerializer.Deserialize<BatchTelemetryData>(body);
        
        if (batchData?.Traces?.Any() == true)
            await service.ProcessTracesAsync(JsonSerializer.Serialize(batchData.Traces));
        
        if (batchData?.Metrics?.Any() == true)
            await service.ProcessMetricsAsync(JsonSerializer.Serialize(batchData.Metrics));
        
        if (batchData?.Logs?.Any() == true)
            await service.ProcessLogsAsync(JsonSerializer.Serialize(batchData.Logs));
        
        return Results.Ok(new { status = "success", processed = new {
            traces = batchData?.Traces?.Count ?? 0,
            metrics = batchData?.Metrics?.Count ?? 0,
            logs = batchData?.Logs?.Count ?? 0
        }});
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Configuration endpoints
app.MapGet("/api/otel/config", () =>
{
    return Results.Ok(new
    {
        ServiceName = "SignalBeam OTEL Cloud",
        Version = "1.0.0",
        Endpoints = new
        {
            Http = new
            {
                Traces = "/v1/traces",
                Metrics = "/v1/metrics",
                Logs = "/v1/logs",
                Batch = "/v1/batch"
            }
        },
        SupportedFormats = new[] { "json" },
        SamplingRules = new
        {
            TracesSamplingRate = 1.0,
            MetricsSamplingRate = 1.0
        }
    });
});

// Health and statistics
app.MapGet("/api/otel/stats", async (IOtelCollectorService service) =>
{
    var stats = await service.GetStatisticsAsync();
    return Results.Ok(stats);
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

// Data Models for batch processing
public record BatchTelemetryData(
    List<SimpleTraceData>? Traces = null,
    List<SimpleMetricData>? Metrics = null,
    List<SimpleLogData>? Logs = null
);

public record SimpleTraceData(
    string TraceId,
    string SpanId,
    string OperationName,
    DateTime StartTime,
    DateTime EndTime,
    string Service,
    string? ParentSpanId = null,
    Dictionary<string, string>? Tags = null
);

public record SimpleMetricData(
    string Name,
    double Value,
    DateTime Timestamp,
    string Service,
    Dictionary<string, string>? Tags = null
);

public record SimpleLogData(
    string Message,
    string Level,
    DateTime Timestamp,
    string Service,
    string? TraceId = null,
    Dictionary<string, string>? Attributes = null
);

// Business Services
public interface IOtelCollectorService
{
    Task ProcessTracesAsync(string tracesData);
    Task ProcessMetricsAsync(string metricsData);
    Task ProcessLogsAsync(string logsData);
    Task<object> GetStatisticsAsync();
}

public class OtelCollectorService : IOtelCollectorService
{
    private readonly ClickHouseConnection _clickHouseConnection;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<OtelCollectorService> _logger;
    private long _tracesProcessed = 0;
    private long _metricsProcessed = 0;
    private long _logsProcessed = 0;

    public OtelCollectorService(
        ClickHouseConnection clickHouseConnection, 
        IMessagePublisher messagePublisher,
        ILogger<OtelCollectorService> logger)
    {
        _clickHouseConnection = clickHouseConnection;
        _messagePublisher = messagePublisher;
        _logger = logger;
    }

    public async Task ProcessTracesAsync(string tracesData)
    {
        try
        {
            var traces = ParseTraces(tracesData);
            
            if (traces.Any())
            {
                await StoreTracesInClickHouseAsync(traces);
                await _messagePublisher.PublishTracesAsync(traces);
                
                Interlocked.Add(ref _tracesProcessed, traces.Count);
                _logger.LogInformation("Processed {Count} traces", traces.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process traces");
            throw;
        }
    }

    public async Task ProcessMetricsAsync(string metricsData)
    {
        try
        {
            var metrics = ParseMetrics(metricsData);
            
            if (metrics.Any())
            {
                await StoreMetricsInClickHouseAsync(metrics);
                await _messagePublisher.PublishMetricsAsync(metrics);
                
                Interlocked.Add(ref _metricsProcessed, metrics.Count);
                _logger.LogInformation("Processed {Count} metrics", metrics.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process metrics");
            throw;
        }
    }

    public async Task ProcessLogsAsync(string logsData)
    {
        try
        {
            var logs = ParseLogs(logsData);
            
            if (logs.Any())
            {
                await StoreLogsInClickHouseAsync(logs);
                await _messagePublisher.PublishLogsAsync(logs);
                
                Interlocked.Add(ref _logsProcessed, logs.Count);
                _logger.LogInformation("Processed {Count} logs", logs.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process logs");
            throw;
        }
    }

    public async Task<object> GetStatisticsAsync()
    {
        return await Task.FromResult(new
        {
            ProcessedCounts = new
            {
                Traces = _tracesProcessed,
                Metrics = _metricsProcessed,
                Logs = _logsProcessed,
                Total = _tracesProcessed + _metricsProcessed + _logsProcessed
            },
            StartTime = DateTimeOffset.UtcNow,
            Status = "Active"
        });
    }

    private List<TraceEntry> ParseTraces(string tracesData)
    {
        var traces = new List<TraceEntry>();
        
        try
        {
            if (string.IsNullOrEmpty(tracesData)) return traces;
            
            // Try to parse as simple trace data array first
            var simpleTraces = JsonSerializer.Deserialize<List<SimpleTraceData>>(tracesData);
            if (simpleTraces != null)
            {
                traces.AddRange(simpleTraces.Select(t => new TraceEntry(
                    TraceId: t.TraceId,
                    SpanId: t.SpanId,
                    OperationName: t.OperationName,
                    StartTime: t.StartTime,
                    EndTime: t.EndTime,
                    Service: t.Service,
                    Status: "OK",
                    ParentSpanId: t.ParentSpanId,
                    Tags: t.Tags
                )));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse traces: {Error}", ex.Message);
        }
        
        return traces;
    }

    private List<MetricEntry> ParseMetrics(string metricsData)
    {
        var metrics = new List<MetricEntry>();
        
        try
        {
            if (string.IsNullOrEmpty(metricsData)) return metrics;
            
            var simpleMetrics = JsonSerializer.Deserialize<List<SimpleMetricData>>(metricsData);
            if (simpleMetrics != null)
            {
                metrics.AddRange(simpleMetrics.Select(m => new MetricEntry(
                    Timestamp: m.Timestamp,
                    MetricName: m.Name,
                    Value: m.Value,
                    Service: m.Service,
                    Host: "otel-cloud",
                    Tags: m.Tags
                )));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse metrics: {Error}", ex.Message);
        }
        
        return metrics;
    }

    private List<LogEntry> ParseLogs(string logsData)
    {
        var logs = new List<LogEntry>();
        
        try
        {
            if (string.IsNullOrEmpty(logsData)) return logs;
            
            var simpleLogs = JsonSerializer.Deserialize<List<SimpleLogData>>(logsData);
            if (simpleLogs != null)
            {
                logs.AddRange(simpleLogs.Select(l => new LogEntry(
                    Timestamp: l.Timestamp,
                    Level: l.Level,
                    Message: l.Message,
                    Service: l.Service,
                    Host: "otel-cloud",
                    TraceId: l.TraceId,
                    SpanId: null,
                    Labels: l.Attributes,
                    SourceIp: "127.0.0.1"
                )));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse logs: {Error}", ex.Message);
        }
        
        return logs;
    }

    private async Task StoreTracesInClickHouseAsync(List<TraceEntry> traces)
    {
        try
        {
            using var bulkCopy = new ClickHouseBulkCopy(_clickHouseConnection)
            {
                DestinationTableName = "traces"
            };

            var dataTable = new DataTable();
            dataTable.Columns.Add("trace_id", typeof(string));
            dataTable.Columns.Add("span_id", typeof(string));
            dataTable.Columns.Add("parent_span_id", typeof(string));
            dataTable.Columns.Add("operation_name", typeof(string));
            dataTable.Columns.Add("start_time", typeof(DateTime));
            dataTable.Columns.Add("end_time", typeof(DateTime));
            dataTable.Columns.Add("duration_ms", typeof(uint));
            dataTable.Columns.Add("service", typeof(string));
            dataTable.Columns.Add("tags", typeof(string));
            dataTable.Columns.Add("status", typeof(string));

            foreach (var trace in traces)
            {
                var row = dataTable.NewRow();
                row["trace_id"] = trace.TraceId;
                row["span_id"] = trace.SpanId;
                row["parent_span_id"] = trace.ParentSpanId ?? "";
                row["operation_name"] = trace.OperationName;
                row["start_time"] = trace.StartTime;
                row["end_time"] = trace.EndTime;
                row["duration_ms"] = (uint)(trace.EndTime - trace.StartTime).TotalMilliseconds;
                row["service"] = trace.Service;
                row["tags"] = JsonSerializer.Serialize(trace.Tags ?? new Dictionary<string, string>());
                row["status"] = trace.Status;
                dataTable.Rows.Add(row);
            }

            using var reader = dataTable.CreateDataReader();
            await bulkCopy.WriteToServerAsync(reader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store traces in ClickHouse");
            throw;
        }
    }

    private async Task StoreMetricsInClickHouseAsync(List<MetricEntry> metrics)
    {
        try
        {
            using var bulkCopy = new ClickHouseBulkCopy(_clickHouseConnection)
            {
                DestinationTableName = "metrics"
            };

            var dataTable = new DataTable();
            dataTable.Columns.Add("timestamp", typeof(DateTime));
            dataTable.Columns.Add("metric_name", typeof(string));
            dataTable.Columns.Add("value", typeof(double));
            dataTable.Columns.Add("tags", typeof(string));
            dataTable.Columns.Add("service", typeof(string));
            dataTable.Columns.Add("host", typeof(string));

            foreach (var metric in metrics)
            {
                var row = dataTable.NewRow();
                row["timestamp"] = metric.Timestamp;
                row["metric_name"] = metric.MetricName;
                row["value"] = metric.Value;
                row["tags"] = JsonSerializer.Serialize(metric.Tags ?? new Dictionary<string, string>());
                row["service"] = metric.Service;
                row["host"] = metric.Host;
                dataTable.Rows.Add(row);
            }

            using var reader = dataTable.CreateDataReader();
            await bulkCopy.WriteToServerAsync(reader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store metrics in ClickHouse");
            throw;
        }
    }

    private async Task StoreLogsInClickHouseAsync(List<LogEntry> logs)
    {
        try
        {
            using var bulkCopy = new ClickHouseBulkCopy(_clickHouseConnection)
            {
                DestinationTableName = "logs"
            };

            var dataTable = new DataTable();
            dataTable.Columns.Add("timestamp", typeof(DateTime));
            dataTable.Columns.Add("level", typeof(string));
            dataTable.Columns.Add("message", typeof(string));
            dataTable.Columns.Add("service", typeof(string));
            dataTable.Columns.Add("host", typeof(string));
            dataTable.Columns.Add("trace_id", typeof(string));
            dataTable.Columns.Add("span_id", typeof(string));
            dataTable.Columns.Add("labels", typeof(string));
            dataTable.Columns.Add("source_ip", typeof(string));

            foreach (var log in logs)
            {
                var row = dataTable.NewRow();
                row["timestamp"] = log.Timestamp;
                row["level"] = log.Level;
                row["message"] = log.Message;
                row["service"] = log.Service;
                row["host"] = log.Host;
                row["trace_id"] = log.TraceId ?? "";
                row["span_id"] = log.SpanId ?? "";
                row["labels"] = JsonSerializer.Serialize(log.Labels ?? new Dictionary<string, string>());
                row["source_ip"] = log.SourceIp ?? "127.0.0.1";
                dataTable.Rows.Add(row);
            }

            using var reader = dataTable.CreateDataReader();
            await bulkCopy.WriteToServerAsync(reader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store logs in ClickHouse");
            throw;
        }
    }
}

// Message Publisher
public interface IMessagePublisher
{
    Task PublishTracesAsync(List<TraceEntry> traces);
    Task PublishMetricsAsync(List<MetricEntry> metrics);
    Task PublishLogsAsync(List<LogEntry> logs);
}

public class RabbitMQPublisher : IMessagePublisher
{
    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly IConnection? _connection;
    private readonly IModel? _channel;

    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger)
    {
        _logger = logger;
        
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = "localhost",
                UserName = "signalbeam",
                Password = "signalbeam_password"
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare queues
            _channel.QueueDeclare("traces_queue", durable: true, exclusive: false, autoDelete: false);
            _channel.QueueDeclare("metrics_queue", durable: true, exclusive: false, autoDelete: false);
            _channel.QueueDeclare("logs_queue", durable: true, exclusive: false, autoDelete: false);
            
            _logger.LogInformation("RabbitMQ connection established");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ");
        }
    }

    public async Task PublishTracesAsync(List<TraceEntry> traces)
    {
        if (_channel == null)
        {
            _logger.LogWarning("RabbitMQ channel not available, skipping trace publishing");
            return;
        }
        
        try
        {
            var message = JsonSerializer.Serialize(traces);
            var body = Encoding.UTF8.GetBytes(message);
            
            _channel.BasicPublish(
                exchange: "",
                routingKey: "traces_queue",
                basicProperties: null,
                body: body
            );

            _logger.LogDebug("Published {Count} traces to queue", traces.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish traces");
        }
        
        await Task.CompletedTask;
    }

    public async Task PublishMetricsAsync(List<MetricEntry> metrics)
    {
        if (_channel == null)
        {
            _logger.LogWarning("RabbitMQ channel not available, skipping metrics publishing");
            return;
        }
        
        try
        {
            var message = JsonSerializer.Serialize(metrics);
            var body = Encoding.UTF8.GetBytes(message);
            
            _channel.BasicPublish(
                exchange: "",
                routingKey: "metrics_queue",
                basicProperties: null,
                body: body
            );

            _logger.LogDebug("Published {Count} metrics to queue", metrics.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish metrics");
        }
        
        await Task.CompletedTask;
    }

    public async Task PublishLogsAsync(List<LogEntry> logs)
    {
        if (_channel == null)
        {
            _logger.LogWarning("RabbitMQ channel not available, skipping logs publishing");
            return;
        }
        
        try
        {
            var message = JsonSerializer.Serialize(logs);
            var body = Encoding.UTF8.GetBytes(message);
            
            _channel.BasicPublish(
                exchange: "",
                routingKey: "logs_queue",
                basicProperties: null,
                body: body
            );

            _logger.LogDebug("Published {Count} logs to queue", logs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish logs");
        }
        
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            _channel?.Close();
            _connection?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing RabbitMQ connection");
        }
    }
}

// Background service for processing metrics
public class OtelMetricsProcessingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OtelMetricsProcessingService> _logger;

    public OtelMetricsProcessingService(IServiceProvider serviceProvider, ILogger<OtelMetricsProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Process any pending metrics aggregation or cleanup
                _logger.LogDebug("Processing OTEL background tasks");
                
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OTEL metrics processing service");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}

// Data Models (reusing from ingestion service)
public record TraceEntry(
    string TraceId,
    string SpanId,
    string OperationName,
    DateTime StartTime,
    DateTime EndTime,
    string Service,
    string Status,
    string? ParentSpanId = null,
    Dictionary<string, string>? Tags = null
);

public record MetricEntry(
    DateTime Timestamp,
    string MetricName,
    double Value,
    string Service,
    string Host,
    Dictionary<string, string>? Tags = null
);

public record LogEntry(
    DateTime Timestamp,
    string Level,
    string Message,
    string Service,
    string Host,
    string? TraceId = null,
    string? SpanId = null,
    Dictionary<string, string>? Labels = null,
    string? SourceIp = null
);
