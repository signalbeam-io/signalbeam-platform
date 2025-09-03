var builder = DistributedApplication.CreateBuilder(args);

// Database and Infrastructure Services
var postgres = builder.AddPostgres("postgres", port: 5432, password: "signalbeam_password")
    .WithDataVolume()
    .AddDatabase("signalbeam");

var redis = builder.AddRedis("redis", port: 6379)
    .WithDataVolume();

var clickhouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "23.8")
    .WithBindMount("../../../infrastructure/clickhouse", "/docker-entrypoint-initdb.d")
    .WithEndpoint(8123, 8123, "http")
    .WithEndpoint(9000, 9000, "tcp")
    .WithEnvironment("CLICKHOUSE_DB", "signalbeam")
    .WithEnvironment("CLICKHOUSE_USER", "signalbeam")
    .WithEnvironment("CLICKHOUSE_PASSWORD", "signalbeam_password");

// Message Brokers
var rabbitmq = builder.AddRabbitMQ("rabbitmq", port: 5672)
    .WithManagementPlugin();

var nats = builder.AddContainer("nats", "nats", "2.10-alpine")
    .WithArgs("--jetstream", "--store_dir=/data", "--http_port=8222")
    .WithEndpoint(4222, 4222, "nats")
    .WithEndpoint(8222, 8222, "http")
    .WithDataVolume("/data");

var mosquitto = builder.AddContainer("mosquitto", "eclipse-mosquitto", "2.0")
    .WithBindMount("../../../infrastructure/mosquitto/config", "/mosquitto/config")
    .WithBindMount("../../../infrastructure/mosquitto/certs", "/mosquitto/certs")
    .WithEndpoint(1883, 1883, "mqtt")
    .WithEndpoint(8883, 8883, "mqtts")
    .WithEndpoint(9001, 9001, "ws");

// Core Platform Services
var authService = builder.AddProject<Projects.SignalBeam_Platform_Auth>("auth")
    .WithReference(postgres)
    .WithReference(redis);

var controlPlane = builder.AddProject<Projects.SignalBeam_Platform_ControlPlane>("control-plane")
    .WithReference(postgres)
    .WithReference(clickhouse)
    .WithReference(redis);

var ingestionService = builder.AddProject<Projects.SignalBeam_Platform_Ingestion>("ingestion")
    .WithReference(clickhouse)
    .WithReference(nats);

var edgeCollectors = builder.AddProject<Projects.SignalBeam_Platform_EdgeCollectors>("edge-collectors")
    .WithReference(postgres)
    .WithReference(mosquitto)
    .WithReference(nats);

var otelCloud = builder.AddProject<Projects.SignalBeam_Platform_OtelCloud>("otel-cloud")
    .WithReference(nats);

// Data Processing Services
var metricsProcessor = builder.AddProject<Projects.SignalBeam_Platform_MetricsProcessor>("metrics-processor")
    .WithReference(clickhouse)
    .WithReference(nats);

var rulesEngine = builder.AddProject<Projects.SignalBeam_Platform_RulesEngine>("rules-engine")
    .WithReference(postgres)
    .WithReference(clickhouse);

var alertingService = builder.AddProject<Projects.SignalBeam_Platform_Alerting>("alerting");

// API Gateway (Main Entry Point)
var gateway = builder.AddProject<Projects.SignalBeam_Platform_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithReference(authService)
    .WithReference(controlPlane)
    .WithReference(ingestionService)
    .WithReference(edgeCollectors);

// Legacy API Service (if still needed)
var apiService = builder.AddProject<Projects.SignalBeam_Platform_ApiService>("apiservice")
    .WithReference(postgres);

// Observability Stack
var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one", "1.49")
    .WithEndpoint(16686, 16686, "http")
    .WithEndpoint(14268, 14268, "http")
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true");

var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v2.47.0")
    .WithBindMount("../../../infrastructure/prometheus", "/etc/prometheus")
    .WithEndpoint(9090, 9090, "http")
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus", "--web.console.libraries=/etc/prometheus/console_libraries", "--web.console.templates=/etc/prometheus/consoles");

var grafana = builder.AddContainer("grafana", "grafana/grafana", "10.1.0")
    .WithBindMount("../../../infrastructure/grafana/provisioning", "/etc/grafana/provisioning")
    .WithEndpoint(3000, 3000, "http")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin");

builder.Build().Run();
