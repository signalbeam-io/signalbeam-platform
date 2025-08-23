using Microsoft.EntityFrameworkCore;
using ClickHouse.Client.ADO;
using System.Text.Json;
using HotChocolate.Subscriptions;
using HotChocolate.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Add Entity Framework for metadata
builder.Services.AddDbContext<ControlPlaneDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? 
                     "Host=localhost;Database=signalbeam;Username=postgres;Password=postgres"));

// Add ClickHouse connection for data queries
builder.Services.AddSingleton<ClickHouseConnection>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("clickhouse") ?? 
                          "Host=localhost;Port=8123;Username=signalbeam;Password=signalbeam_password;Database=signalbeam";
    return new ClickHouseConnection(connectionString);
});

// Add HTTP clients for other services
builder.Services.AddHttpClient("edge-collectors", client =>
{
    client.BaseAddress = new Uri("https://edge-collectors");
});

// Add business services
builder.Services.AddScoped<IDataQueryService, DataQueryService>();
builder.Services.AddScoped<ICollectorManagementService, CollectorManagementService>();
builder.Services.AddScoped<ISystemHealthService, SystemHealthService>();

// Add GraphQL
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddSubscriptionType<Subscription>()
    .AddFiltering()
    .AddSorting()
    .AddProjections()
    .AddInMemorySubscriptions();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
    context.Database.EnsureCreated();
}

app.UseRouting();

// Add CORS for frontend
app.UseCors(policy =>
{
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader();
});

// Map GraphQL endpoint
app.MapGraphQL("/graphql");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Platform info endpoint
app.MapGet("/api/info", () => Results.Ok(new
{
    service = "SignalBeam Control Plane",
    version = "1.0.0",
    endpoints = new
    {
        graphql = "/graphql",
        health = "/health"
    }
}));

app.Run();

// Database Context
public class ControlPlaneDbContext : DbContext
{
    public ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : base(options) { }

    public DbSet<Dashboard> Dashboards { get; set; } = null!;
    public DbSet<Alert> Alerts { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dashboard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Configuration).HasColumnType("jsonb");
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Query).IsRequired();
            entity.Property(e => e.Conditions).HasColumnType("jsonb");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
}

// Data Models
public class Dashboard
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public bool IsPublic { get; set; }
}

public class Alert
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public Dictionary<string, object> Conditions { get; set; } = new();
    public AlertStatus Status { get; set; }
    public AlertSeverity Severity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastTriggered { get; set; }
    public Guid CreatedBy { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLogin { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum AlertStatus
{
    Ok,
    Warning,
    Critical,
    Unknown
}

public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum UserRole
{
    Viewer,
    Editor,
    Admin
}

// GraphQL Types
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public Dictionary<string, string> Labels { get; set; } = new();
}

public class MetricEntry
{
    public DateTime Timestamp { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Service { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class TraceEntry
{
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public string Service { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class CollectorInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime LastHeartbeat { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class SystemStats
{
    public long TotalLogs { get; set; }
    public long TotalMetrics { get; set; }
    public long TotalTraces { get; set; }
    public int ActiveCollectors { get; set; }
    public int ActiveAlerts { get; set; }
    public Dictionary<string, long> ServiceCounts { get; set; } = new();
}

// GraphQL Queries
[ExtendObjectType<Query>]
public class Query
{
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Dashboard> GetDashboards([Service] ControlPlaneDbContext context) =>
        context.Dashboards;

    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Alert> GetAlerts([Service] ControlPlaneDbContext context) =>
        context.Alerts;

    public async Task<List<LogEntry>> GetLogs(
        [Service] IDataQueryService dataService,
        DateTime? from = null,
        DateTime? to = null,
        string? service = null,
        string? level = null,
        int limit = 100) =>
        await dataService.GetLogsAsync(from, to, service, level, limit);

    public async Task<List<MetricEntry>> GetMetrics(
        [Service] IDataQueryService dataService,
        string metricName,
        DateTime? from = null,
        DateTime? to = null,
        string? service = null,
        int limit = 1000) =>
        await dataService.GetMetricsAsync(metricName, from, to, service, limit);

    public async Task<List<TraceEntry>> GetTraces(
        [Service] IDataQueryService dataService,
        DateTime? from = null,
        DateTime? to = null,
        string? service = null,
        int limit = 100) =>
        await dataService.GetTracesAsync(from, to, service, limit);

    public async Task<List<CollectorInfo>> GetCollectors(
        [Service] ICollectorManagementService collectorService) =>
        await collectorService.GetAllCollectorsAsync();

    public async Task<SystemStats> GetSystemStats(
        [Service] ISystemHealthService healthService) =>
        await healthService.GetSystemStatsAsync();
}

// GraphQL Mutations
[ExtendObjectType<Mutation>]
public class Mutation
{
    public async Task<Dashboard> CreateDashboard(
        [Service] ControlPlaneDbContext context,
        string name,
        string description,
        Dictionary<string, object> configuration)
    {
        var dashboard = new Dashboard
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Configuration = configuration,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = Guid.Empty, // TODO: Get from auth context
            IsPublic = false
        };

        context.Dashboards.Add(dashboard);
        await context.SaveChangesAsync();
        return dashboard;
    }

    public async Task<Alert> CreateAlert(
        [Service] ControlPlaneDbContext context,
        string name,
        string description,
        string query,
        Dictionary<string, object> conditions,
        AlertSeverity severity)
    {
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Query = query,
            Conditions = conditions,
            Severity = severity,
            Status = AlertStatus.Unknown,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = Guid.Empty, // TODO: Get from auth context
            IsEnabled = true
        };

        context.Alerts.Add(alert);
        await context.SaveChangesAsync();
        return alert;
    }

    public async Task<bool> DeleteDashboard(
        [Service] ControlPlaneDbContext context,
        Guid id)
    {
        var dashboard = await context.Dashboards.FindAsync(id);
        if (dashboard == null) return false;

        context.Dashboards.Remove(dashboard);
        await context.SaveChangesAsync();
        return true;
    }
}

// GraphQL Subscriptions
[ExtendObjectType<Subscription>]
public class Subscription
{
    [Subscribe]
    public SystemStats OnSystemStatsUpdated([EventMessage] SystemStats stats) => stats;

    [Subscribe]
    public Alert OnAlertTriggered([EventMessage] Alert alert) => alert;
}

// Business Services
public interface IDataQueryService
{
    Task<List<LogEntry>> GetLogsAsync(DateTime? from, DateTime? to, string? service, string? level, int limit);
    Task<List<MetricEntry>> GetMetricsAsync(string metricName, DateTime? from, DateTime? to, string? service, int limit);
    Task<List<TraceEntry>> GetTracesAsync(DateTime? from, DateTime? to, string? service, int limit);
}

public interface ICollectorManagementService
{
    Task<List<CollectorInfo>> GetAllCollectorsAsync();
}

public interface ISystemHealthService
{
    Task<SystemStats> GetSystemStatsAsync();
}

public class DataQueryService : IDataQueryService
{
    private readonly ClickHouseConnection _clickHouseConnection;
    private readonly ILogger<DataQueryService> _logger;

    public DataQueryService(ClickHouseConnection clickHouseConnection, ILogger<DataQueryService> logger)
    {
        _clickHouseConnection = clickHouseConnection;
        _logger = logger;
    }

    public async Task<List<LogEntry>> GetLogsAsync(DateTime? from, DateTime? to, string? service, string? level, int limit)
    {
        var logs = new List<LogEntry>();
        
        try
        {
            var whereConditions = new List<string>();
            
            if (from.HasValue) whereConditions.Add($"timestamp >= '{from:yyyy-MM-dd HH:mm:ss}'");
            if (to.HasValue) whereConditions.Add($"timestamp <= '{to:yyyy-MM-dd HH:mm:ss}'");
            if (!string.IsNullOrEmpty(service)) whereConditions.Add($"service = '{service}'");
            if (!string.IsNullOrEmpty(level)) whereConditions.Add($"level = '{level}'");

            var whereClause = whereConditions.Any() ? "WHERE " + string.Join(" AND ", whereConditions) : "";
            
            var sql = $@"
                SELECT timestamp, level, message, service, host, trace_id, labels
                FROM logs 
                {whereClause}
                ORDER BY timestamp DESC 
                LIMIT {Math.Min(limit, 10000)}";

            using var command = _clickHouseConnection.CreateCommand(sql);
            await _clickHouseConnection.OpenAsync();
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var labels = new Dictionary<string, string>();
                try
                {
                    var labelsJson = reader.GetString("labels");
                    if (!string.IsNullOrEmpty(labelsJson))
                    {
                        labels = JsonSerializer.Deserialize<Dictionary<string, string>>(labelsJson) ?? new();
                    }
                }
                catch { /* ignore parsing errors */ }

                logs.Add(new LogEntry
                {
                    Timestamp = reader.GetDateTime("timestamp"),
                    Level = reader.GetString("level"),
                    Message = reader.GetString("message"),
                    Service = reader.GetString("service"),
                    Host = reader.GetString("host"),
                    TraceId = reader.IsDBNull("trace_id") ? null : reader.GetString("trace_id"),
                    Labels = labels
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query logs");
        }
        finally
        {
            if (_clickHouseConnection.State == System.Data.ConnectionState.Open)
            {
                await _clickHouseConnection.CloseAsync();
            }
        }

        return logs;
    }

    public async Task<List<MetricEntry>> GetMetricsAsync(string metricName, DateTime? from, DateTime? to, string? service, int limit)
    {
        var metrics = new List<MetricEntry>();
        
        try
        {
            var whereConditions = new List<string> { $"metric_name = '{metricName}'" };
            
            if (from.HasValue) whereConditions.Add($"timestamp >= '{from:yyyy-MM-dd HH:mm:ss}'");
            if (to.HasValue) whereConditions.Add($"timestamp <= '{to:yyyy-MM-dd HH:mm:ss}'");
            if (!string.IsNullOrEmpty(service)) whereConditions.Add($"service = '{service}'");

            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);
            
            var sql = $@"
                SELECT timestamp, metric_name, value, service, host, tags
                FROM metrics 
                {whereClause}
                ORDER BY timestamp DESC 
                LIMIT {Math.Min(limit, 10000)}";

            using var command = _clickHouseConnection.CreateCommand(sql);
            await _clickHouseConnection.OpenAsync();
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tags = new Dictionary<string, string>();
                try
                {
                    var tagsJson = reader.GetString("tags");
                    if (!string.IsNullOrEmpty(tagsJson))
                    {
                        tags = JsonSerializer.Deserialize<Dictionary<string, string>>(tagsJson) ?? new();
                    }
                }
                catch { /* ignore parsing errors */ }

                metrics.Add(new MetricEntry
                {
                    Timestamp = reader.GetDateTime("timestamp"),
                    MetricName = reader.GetString("metric_name"),
                    Value = reader.GetDouble("value"),
                    Service = reader.GetString("service"),
                    Host = reader.GetString("host"),
                    Tags = tags
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query metrics");
        }
        finally
        {
            if (_clickHouseConnection.State == System.Data.ConnectionState.Open)
            {
                await _clickHouseConnection.CloseAsync();
            }
        }

        return metrics;
    }

    public async Task<List<TraceEntry>> GetTracesAsync(DateTime? from, DateTime? to, string? service, int limit)
    {
        var traces = new List<TraceEntry>();
        
        try
        {
            var whereConditions = new List<string>();
            
            if (from.HasValue) whereConditions.Add($"start_time >= '{from:yyyy-MM-dd HH:mm:ss}'");
            if (to.HasValue) whereConditions.Add($"start_time <= '{to:yyyy-MM-dd HH:mm:ss}'");
            if (!string.IsNullOrEmpty(service)) whereConditions.Add($"service = '{service}'");

            var whereClause = whereConditions.Any() ? "WHERE " + string.Join(" AND ", whereConditions) : "";
            
            var sql = $@"
                SELECT trace_id, span_id, operation_name, start_time, end_time, service, status, tags
                FROM traces 
                {whereClause}
                ORDER BY start_time DESC 
                LIMIT {Math.Min(limit, 10000)}";

            using var command = _clickHouseConnection.CreateCommand(sql);
            await _clickHouseConnection.OpenAsync();
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tags = new Dictionary<string, string>();
                try
                {
                    var tagsJson = reader.GetString("tags");
                    if (!string.IsNullOrEmpty(tagsJson))
                    {
                        tags = JsonSerializer.Deserialize<Dictionary<string, string>>(tagsJson) ?? new();
                    }
                }
                catch { /* ignore parsing errors */ }

                traces.Add(new TraceEntry
                {
                    TraceId = reader.GetString("trace_id"),
                    SpanId = reader.GetString("span_id"),
                    OperationName = reader.GetString("operation_name"),
                    StartTime = reader.GetDateTime("start_time"),
                    EndTime = reader.GetDateTime("end_time"),
                    Service = reader.GetString("service"),
                    Status = reader.GetString("status"),
                    Tags = tags
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query traces");
        }
        finally
        {
            if (_clickHouseConnection.State == System.Data.ConnectionState.Open)
            {
                await _clickHouseConnection.CloseAsync();
            }
        }

        return traces;
    }
}

public class CollectorManagementService : ICollectorManagementService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CollectorManagementService> _logger;

    public CollectorManagementService(IHttpClientFactory httpClientFactory, ILogger<CollectorManagementService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("edge-collectors");
        _logger = logger;
    }

    public async Task<List<CollectorInfo>> GetAllCollectorsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/collectors");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var collectors = JsonSerializer.Deserialize<List<CollectorInfo>>(json) ?? new List<CollectorInfo>();
                return collectors;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get collectors from edge collectors service");
        }

        return new List<CollectorInfo>();
    }
}

public class SystemHealthService : ISystemHealthService
{
    private readonly ClickHouseConnection _clickHouseConnection;
    private readonly ICollectorManagementService _collectorService;
    private readonly ILogger<SystemHealthService> _logger;

    public SystemHealthService(
        ClickHouseConnection clickHouseConnection,
        ICollectorManagementService collectorService,
        ILogger<SystemHealthService> logger)
    {
        _clickHouseConnection = clickHouseConnection;
        _collectorService = collectorService;
        _logger = logger;
    }

    public async Task<SystemStats> GetSystemStatsAsync()
    {
        var stats = new SystemStats();

        try
        {
            await _clickHouseConnection.OpenAsync();
            
            // Get total counts
            using var logsCommand = _clickHouseConnection.CreateCommand("SELECT COUNT(*) FROM logs WHERE timestamp >= now() - INTERVAL 24 HOUR");
            stats.TotalLogs = (long)(await logsCommand.ExecuteScalarAsync() ?? 0L);

            using var metricsCommand = _clickHouseConnection.CreateCommand("SELECT COUNT(*) FROM metrics WHERE timestamp >= now() - INTERVAL 24 HOUR");
            stats.TotalMetrics = (long)(await metricsCommand.ExecuteScalarAsync() ?? 0L);

            using var tracesCommand = _clickHouseConnection.CreateCommand("SELECT COUNT(*) FROM traces WHERE start_time >= now() - INTERVAL 24 HOUR");
            stats.TotalTraces = (long)(await tracesCommand.ExecuteScalarAsync() ?? 0L);

            // Get service counts
            using var servicesCommand = _clickHouseConnection.CreateCommand("SELECT service, COUNT(*) as count FROM logs WHERE timestamp >= now() - INTERVAL 24 HOUR GROUP BY service");
            using var reader = await servicesCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stats.ServiceCounts[reader.GetString("service")] = reader.GetInt64("count");
            }

            // Get collector count
            var collectors = await _collectorService.GetAllCollectorsAsync();
            stats.ActiveCollectors = collectors.Count(c => c.Status == "Active");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system stats");
        }
        finally
        {
            if (_clickHouseConnection.State == System.Data.ConnectionState.Open)
            {
                await _clickHouseConnection.CloseAsync();
            }
        }

        return stats;
    }
}
