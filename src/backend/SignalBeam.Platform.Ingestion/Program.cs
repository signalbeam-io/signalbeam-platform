using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Data;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IIngestionService, IngestionService>();
builder.Services.AddHostedService<MessageConsumerService>();

// Add ClickHouse connection
builder.Services.AddSingleton<ClickHouseConnection>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("clickhouse") ?? 
                          "Host=localhost;Port=8123;Username=signalbeam;Password=signalbeam_password;Database=signalbeam";
    return new ClickHouseConnection(connectionString);
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ingestion endpoints
app.MapPost("/logs", async (LogEntry[] logs, IIngestionService ingestionService) =>
{
    await ingestionService.IngestLogsAsync(logs);
    return Results.Ok(new { message = "Logs ingested successfully", count = logs.Length });
});

app.MapPost("/metrics", async (MetricEntry[] metrics, IIngestionService ingestionService) =>
{
    await ingestionService.IngestMetricsAsync(metrics);
    return Results.Ok(new { message = "Metrics ingested successfully", count = metrics.Length });
});

app.MapPost("/traces", async (TraceEntry[] traces, IIngestionService ingestionService) =>
{
    await ingestionService.IngestTracesAsync(traces);
    return Results.Ok(new { message = "Traces ingested successfully", count = traces.Length });
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

public interface IIngestionService
{
    Task IngestLogsAsync(LogEntry[] logs);
    Task IngestMetricsAsync(MetricEntry[] metrics);
    Task IngestTracesAsync(TraceEntry[] traces);
}

public class IngestionService : IIngestionService
{
    private readonly ClickHouseConnection _clickHouseConnection;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(ClickHouseConnection clickHouseConnection, ILogger<IngestionService> logger)
    {
        _clickHouseConnection = clickHouseConnection;
        _logger = logger;
    }

    public async Task IngestLogsAsync(LogEntry[] logs)
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
            _logger.LogInformation("Ingested {Count} log entries", logs.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest logs");
            throw;
        }
    }

    public async Task IngestMetricsAsync(MetricEntry[] metrics)
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
            _logger.LogInformation("Ingested {Count} metric entries", metrics.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest metrics");
            throw;
        }
    }

    public async Task IngestTracesAsync(TraceEntry[] traces)
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
            _logger.LogInformation("Ingested {Count} trace entries", traces.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest traces");
            throw;
        }
    }
}

public class MessageConsumerService : BackgroundService
{
    private readonly ILogger<MessageConsumerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IConnection? _connection;
    private IModel? _channel;

    public MessageConsumerService(ILogger<MessageConsumerService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = "localhost", // This will be configured via Aspire
                UserName = "signalbeam",
                Password = "signalbeam_password"
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.QueueDeclare(queue: "logs_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueDeclare(queue: "metrics_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueDeclare(queue: "traces_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    
                    using var scope = _serviceProvider.CreateScope();
                    var ingestionService = scope.ServiceProvider.GetRequiredService<IIngestionService>();

                    switch (ea.RoutingKey)
                    {
                        case "logs_queue":
                            var logs = JsonSerializer.Deserialize<LogEntry[]>(message);
                            if (logs != null) await ingestionService.IngestLogsAsync(logs);
                            break;
                        case "metrics_queue":
                            var metrics = JsonSerializer.Deserialize<MetricEntry[]>(message);
                            if (metrics != null) await ingestionService.IngestMetricsAsync(metrics);
                            break;
                        case "traces_queue":
                            var traces = JsonSerializer.Deserialize<TraceEntry[]>(message);
                            if (traces != null) await ingestionService.IngestTracesAsync(traces);
                            break;
                    }

                    _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message from queue {RoutingKey}", ea.RoutingKey);
                    _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                }
            };

            _channel.BasicConsume(queue: "logs_queue", autoAck: false, consumer: consumer);
            _channel.BasicConsume(queue: "metrics_queue", autoAck: false, consumer: consumer);
            _channel.BasicConsume(queue: "traces_queue", autoAck: false, consumer: consumer);

            _logger.LogInformation("Message consumer service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in message consumer service");
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

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

public record MetricEntry(
    DateTime Timestamp,
    string MetricName,
    double Value,
    string Service,
    string Host,
    Dictionary<string, string>? Tags = null
);

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
