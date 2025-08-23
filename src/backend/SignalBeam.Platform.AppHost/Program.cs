var builder = DistributedApplication.CreateBuilder(args);

// Database services
var postgres = builder.AddPostgres("postgres")
    .AddDatabase("signalbeam");

var redis = builder.AddRedis("redis");

var rabbitmq = builder.AddRabbitMQ("rabbitmq");

// Core microservices
var authService = builder.AddProject<Projects.SignalBeam_Platform_Auth>("auth")
    .WithReference(postgres)
    .WithReference(redis);

var ingestionService = builder.AddProject<Projects.SignalBeam_Platform_Ingestion>("ingestion")
    .WithReference(postgres)
    .WithReference(rabbitmq);

var gateway = builder.AddProject<Projects.SignalBeam_Platform_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithReference(authService)
    .WithReference(ingestionService);

var apiService = builder.AddProject<Projects.SignalBeam_Platform_ApiService>("apiservice")
    .WithReference(postgres);


builder.Build().Run();
