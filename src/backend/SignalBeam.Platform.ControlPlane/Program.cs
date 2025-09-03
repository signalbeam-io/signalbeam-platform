using Microsoft.EntityFrameworkCore;
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

// Add HTTP clients for other services
builder.Services.AddHttpClient("edge-collectors", client =>
{
    client.BaseAddress = new Uri("https://edge-collectors");
});

// Add business services
builder.Services.AddScoped<ICollectorManagementService, CollectorManagementService>();

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

    public async Task<List<CollectorInfo>> GetCollectors(
        [Service] ICollectorManagementService collectorService) =>
        await collectorService.GetAllCollectorsAsync();

    public SystemStats GetSystemStats() => new SystemStats
    {
        TotalLogs = 0,
        TotalMetrics = 0,
        TotalTraces = 0,
        ActiveCollectors = 0,
        ActiveAlerts = 0,
        ServiceCounts = new Dictionary<string, long>()
    };
}

// GraphQL Mutations
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
public class Subscription
{
    [Subscribe]
    public SystemStats OnSystemStatsUpdated([EventMessage] SystemStats stats) => stats;

    [Subscribe]
    public Alert OnAlertTriggered([EventMessage] Alert alert) => alert;
}

// Business Services
public interface ICollectorManagementService
{
    Task<List<CollectorInfo>> GetAllCollectorsAsync();
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
            // TODO: Implement actual HTTP call to edge collectors service
            // For now, return mock data
            return new List<CollectorInfo>
            {
                new CollectorInfo
                {
                    Id = Guid.NewGuid(),
                    Name = "Sample Edge Device",
                    Type = "IoT Device",
                    Status = "Active",
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-1),
                    IpAddress = "192.168.1.100",
                    Tags = new Dictionary<string, string> { ["location"] = "factory-floor-1" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get collectors from edge collectors service");
        }

        return new List<CollectorInfo>();
    }
}