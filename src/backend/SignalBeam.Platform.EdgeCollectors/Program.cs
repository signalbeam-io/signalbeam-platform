using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();

// Add Entity Framework
builder.Services.AddDbContext<EdgeCollectorsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? 
                     "Host=localhost;Database=signalbeam;Username=postgres;Password=postgres"));

// Add business services
builder.Services.AddScoped<ICollectorService, CollectorService>();
builder.Services.AddScoped<ICollectorConfigService, CollectorConfigService>();
builder.Services.AddSingleton<IMessagePublisher, RabbitMQPublisher>();
builder.Services.AddHostedService<CollectorHealthMonitorService>();

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
    var context = scope.ServiceProvider.GetRequiredService<EdgeCollectorsDbContext>();
    context.Database.EnsureCreated();
}

app.UseRouting();

// Map SignalR hub for real-time collector communication
app.MapHub<CollectorHub>("/collector-hub");

// Collector Management APIs
app.MapPost("/api/collectors/register", async (CollectorRegistrationRequest request, ICollectorService service) =>
{
    var collector = await service.RegisterCollectorAsync(request);
    return Results.Ok(collector);
});

app.MapGet("/api/collectors", async (ICollectorService service) =>
{
    var collectors = await service.GetAllCollectorsAsync();
    return Results.Ok(collectors);
});

app.MapGet("/api/collectors/{id:guid}", async (Guid id, ICollectorService service) =>
{
    var collector = await service.GetCollectorAsync(id);
    return collector != null ? Results.Ok(collector) : Results.NotFound();
});

app.MapPut("/api/collectors/{id:guid}/status", async (Guid id, CollectorStatusUpdate status, ICollectorService service) =>
{
    await service.UpdateCollectorStatusAsync(id, status);
    return Results.Ok();
});

app.MapDelete("/api/collectors/{id:guid}", async (Guid id, ICollectorService service) =>
{
    await service.UnregisterCollectorAsync(id);
    return Results.Ok();
});

// Collector Configuration APIs
app.MapGet("/api/collectors/{id:guid}/config", async (Guid id, ICollectorConfigService service) =>
{
    var config = await service.GetCollectorConfigAsync(id);
    return config != null ? Results.Ok(config) : Results.NotFound();
});

app.MapPut("/api/collectors/{id:guid}/config", async (Guid id, CollectorConfig config, ICollectorConfigService service) =>
{
    await service.UpdateCollectorConfigAsync(id, config);
    return Results.Ok();
});

// Data Collection APIs
app.MapPost("/api/collectors/{id:guid}/data", async (Guid id, CollectorDataPayload payload, IMessagePublisher publisher) =>
{
    await publisher.PublishCollectorDataAsync(id, payload);
    return Results.Accepted();
});

// Collector Commands
app.MapPost("/api/collectors/{id:guid}/commands/{command}", async (Guid id, string command, object? parameters, ICollectorService service) =>
{
    await service.SendCommandToCollectorAsync(id, command, parameters);
    return Results.Ok();
});

// Health and metrics
app.MapGet("/api/collectors/health", async (ICollectorService service) =>
{
    var health = await service.GetCollectorsHealthAsync();
    return Results.Ok(health);
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

// Database Context
public class EdgeCollectorsDbContext : DbContext
{
    public EdgeCollectorsDbContext(DbContextOptions<EdgeCollectorsDbContext> options) : base(options) { }

    public DbSet<Collector> Collectors { get; set; } = null!;
    public DbSet<CollectorConfig> CollectorConfigs { get; set; } = null!;
    public DbSet<CollectorHealthStatus> CollectorHealthStatuses { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Collector>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Version).HasMaxLength(50);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.Tags).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CollectorConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<Collector>()
                .WithMany()
                .HasForeignKey(e => e.CollectorId);
            entity.Property(e => e.Configuration).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CollectorHealthStatus>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<Collector>()
                .WithMany()
                .HasForeignKey(e => e.CollectorId);
            entity.Property(e => e.Metrics).HasColumnType("jsonb");
        });
    }
}

// Data Models
public class Collector
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public CollectorStatus Status { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public DateTime RegisteredAt { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public string? Description { get; set; }
}

public class CollectorConfig
{
    public Guid Id { get; set; }
    public Guid CollectorId { get; set; }
    public string ConfigType { get; set; } = string.Empty;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
}

public class CollectorHealthStatus
{
    public Guid Id { get; set; }
    public Guid CollectorId { get; set; }
    public bool IsHealthy { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
    public DateTime CheckedAt { get; set; }
}

public enum CollectorStatus
{
    Registering,
    Active,
    Inactive,
    Error,
    Maintenance
}

// DTOs
public record CollectorRegistrationRequest(
    string Name,
    string Type,
    string Version,
    string IpAddress,
    string Hostname,
    Dictionary<string, string>? Tags = null,
    string? Description = null
);

public record CollectorStatusUpdate(
    CollectorStatus Status,
    string? Message = null,
    Dictionary<string, object>? Metrics = null
);

public record CollectorDataPayload(
    string DataType,
    object Data,
    DateTime Timestamp,
    Dictionary<string, string>? Metadata = null
);

// Business Services
public interface ICollectorService
{
    Task<Collector> RegisterCollectorAsync(CollectorRegistrationRequest request);
    Task<IEnumerable<Collector>> GetAllCollectorsAsync();
    Task<Collector?> GetCollectorAsync(Guid id);
    Task UpdateCollectorStatusAsync(Guid id, CollectorStatusUpdate status);
    Task UnregisterCollectorAsync(Guid id);
    Task SendCommandToCollectorAsync(Guid id, string command, object? parameters);
    Task<object> GetCollectorsHealthAsync();
}

public interface ICollectorConfigService
{
    Task<CollectorConfig?> GetCollectorConfigAsync(Guid collectorId);
    Task UpdateCollectorConfigAsync(Guid collectorId, CollectorConfig config);
}

public interface IMessagePublisher
{
    Task PublishCollectorDataAsync(Guid collectorId, CollectorDataPayload payload);
}

public class CollectorService : ICollectorService
{
    private readonly EdgeCollectorsDbContext _context;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<CollectorService> _logger;

    public CollectorService(EdgeCollectorsDbContext context, IMessagePublisher messagePublisher, ILogger<CollectorService> logger)
    {
        _context = context;
        _messagePublisher = messagePublisher;
        _logger = logger;
    }

    public async Task<Collector> RegisterCollectorAsync(CollectorRegistrationRequest request)
    {
        var collector = new Collector
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Type = request.Type,
            Version = request.Version,
            IpAddress = request.IpAddress,
            Hostname = request.Hostname,
            Status = CollectorStatus.Active,
            LastHeartbeat = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow,
            Tags = request.Tags ?? new Dictionary<string, string>(),
            Description = request.Description
        };

        _context.Collectors.Add(collector);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Collector {Name} ({Id}) registered successfully", collector.Name, collector.Id);
        
        return collector;
    }

    public async Task<IEnumerable<Collector>> GetAllCollectorsAsync()
    {
        return await _context.Collectors.ToListAsync();
    }

    public async Task<Collector?> GetCollectorAsync(Guid id)
    {
        return await _context.Collectors.FindAsync(id);
    }

    public async Task UpdateCollectorStatusAsync(Guid id, CollectorStatusUpdate status)
    {
        var collector = await _context.Collectors.FindAsync(id);
        if (collector != null)
        {
            collector.Status = status.Status;
            collector.LastHeartbeat = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Store health status
            var healthStatus = new CollectorHealthStatus
            {
                Id = Guid.NewGuid(),
                CollectorId = id,
                IsHealthy = status.Status == CollectorStatus.Active,
                LastError = status.Message,
                Metrics = status.Metrics ?? new Dictionary<string, object>(),
                CheckedAt = DateTime.UtcNow
            };

            _context.CollectorHealthStatuses.Add(healthStatus);
            await _context.SaveChangesAsync();
        }
    }

    public async Task UnregisterCollectorAsync(Guid id)
    {
        var collector = await _context.Collectors.FindAsync(id);
        if (collector != null)
        {
            _context.Collectors.Remove(collector);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Collector {Id} unregistered", id);
        }
    }

    public async Task SendCommandToCollectorAsync(Guid id, string command, object? parameters)
    {
        // This would send commands to collectors via SignalR or message queue
        _logger.LogInformation("Sending command {Command} to collector {Id}", command, id);
        await Task.CompletedTask; // Placeholder
    }

    public async Task<object> GetCollectorsHealthAsync()
    {
        var collectors = await _context.Collectors.ToListAsync();
        var healthStatuses = await _context.CollectorHealthStatuses
            .Where(h => collectors.Select(c => c.Id).Contains(h.CollectorId))
            .GroupBy(h => h.CollectorId)
            .Select(g => g.OrderByDescending(h => h.CheckedAt).First())
            .ToListAsync();

        return new
        {
            TotalCollectors = collectors.Count,
            HealthyCollectors = healthStatuses.Count(h => h.IsHealthy),
            UnhealthyCollectors = healthStatuses.Count(h => !h.IsHealthy),
            CollectorsByStatus = collectors.GroupBy(c => c.Status).ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
    }
}

public class CollectorConfigService : ICollectorConfigService
{
    private readonly EdgeCollectorsDbContext _context;
    private readonly ILogger<CollectorConfigService> _logger;

    public CollectorConfigService(EdgeCollectorsDbContext context, ILogger<CollectorConfigService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CollectorConfig?> GetCollectorConfigAsync(Guid collectorId)
    {
        return await _context.CollectorConfigs
            .Where(c => c.CollectorId == collectorId)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateCollectorConfigAsync(Guid collectorId, CollectorConfig config)
    {
        config.CollectorId = collectorId;
        config.UpdatedAt = DateTime.UtcNow;
        config.Version += 1;

        _context.CollectorConfigs.Add(config);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Configuration updated for collector {CollectorId}, version {Version}", collectorId, config.Version);
    }
}

public class RabbitMQPublisher : IMessagePublisher
{
    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger)
    {
        _logger = logger;
        
        var factory = new ConnectionFactory()
        {
            HostName = "localhost", // This will be configured via Aspire
            UserName = "signalbeam",
            Password = "signalbeam_password"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare exchanges and queues
        _channel.ExchangeDeclare("collector-data", ExchangeType.Topic, durable: true);
        _channel.QueueDeclare("logs_queue", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueDeclare("metrics_queue", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueDeclare("traces_queue", durable: true, exclusive: false, autoDelete: false);
    }

    public async Task PublishCollectorDataAsync(Guid collectorId, CollectorDataPayload payload)
    {
        try
        {
            var message = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(message);
            
            var routingKey = $"collector.{collectorId}.{payload.DataType.ToLower()}";
            
            _channel.BasicPublish(
                exchange: "collector-data",
                routingKey: routingKey,
                basicProperties: null,
                body: body
            );

            _logger.LogDebug("Published data from collector {CollectorId} with type {DataType}", collectorId, payload.DataType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish collector data for {CollectorId}", collectorId);
            throw;
        }

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}

// SignalR Hub for real-time collector communication
public class CollectorHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly ILogger<CollectorHub> _logger;

    public CollectorHub(ILogger<CollectorHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinCollectorGroup(string collectorId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"collector-{collectorId}");
        _logger.LogInformation("Collector {CollectorId} joined SignalR group", collectorId);
    }

    public async Task LeaveCollectorGroup(string collectorId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"collector-{collectorId}");
        _logger.LogInformation("Collector {CollectorId} left SignalR group", collectorId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Collector disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

// Background service to monitor collector health
public class CollectorHealthMonitorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CollectorHealthMonitorService> _logger;

    public CollectorHealthMonitorService(IServiceProvider serviceProvider, ILogger<CollectorHealthMonitorService> logger)
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
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<EdgeCollectorsDbContext>();
                
                var threshold = DateTime.UtcNow.AddMinutes(-5); // 5 minutes threshold
                var staleCollectors = await context.Collectors
                    .Where(c => c.LastHeartbeat < threshold && c.Status == CollectorStatus.Active)
                    .ToListAsync(stoppingToken);

                foreach (var collector in staleCollectors)
                {
                    collector.Status = CollectorStatus.Inactive;
                    _logger.LogWarning("Marking collector {Name} ({Id}) as inactive due to missing heartbeat", 
                        collector.Name, collector.Id);
                }

                if (staleCollectors.Any())
                {
                    await context.SaveChangesAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Check every minute
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in collector health monitoring");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
