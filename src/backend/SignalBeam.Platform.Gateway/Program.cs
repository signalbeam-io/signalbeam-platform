var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
app.UseRouting();

// Map YARP reverse proxy
app.MapReverseProxy();

app.Run();
