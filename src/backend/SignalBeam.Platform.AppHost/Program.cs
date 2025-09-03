var builder = DistributedApplication.CreateBuilder(args);

// Database Services
var postgres = builder.AddPostgres("postgres")
    .AddDatabase("signalbeam");

var redis = builder.AddRedis("redis");

// Message Brokers
var rabbitmq = builder.AddRabbitMQ("rabbitmq");

// Core Platform Services
var authService = builder.AddProject<Projects.SignalBeam_Platform_Auth>("auth")
    .WithReference(postgres)
    .WithReference(redis);

var controlPlane = builder.AddProject<Projects.SignalBeam_Platform_ControlPlane>("control-plane")
    .WithReference(postgres)
    .WithReference(redis);

var ingestionService = builder.AddProject<Projects.SignalBeam_Platform_Ingestion>("ingestion")
    .WithReference(postgres);

var edgeCollectors = builder.AddProject<Projects.SignalBeam_Platform_EdgeCollectors>("edge-collectors")
    .WithReference(postgres);

var otelCloud = builder.AddProject<Projects.SignalBeam_Platform_OtelCloud>("otel-cloud");

// Data Processing Services
var metricsProcessor = builder.AddProject<Projects.SignalBeam_Platform_MetricsProcessor>("metrics-processor");

var rulesEngine = builder.AddProject<Projects.SignalBeam_Platform_RulesEngine>("rules-engine")
    .WithReference(postgres);

var alertingService = builder.AddProject<Projects.SignalBeam_Platform_Alerting>("alerting");

// API Gateway (Main Entry Point)
var gateway = builder.AddProject<Projects.SignalBeam_Platform_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithReference(authService)
    .WithReference(controlPlane)
    .WithReference(ingestionService)
    .WithReference(edgeCollectors);


builder.Build().Run();
