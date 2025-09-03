# SignalBeam Platform Development Guide

## .NET Aspire Development Environment

### Prerequisites

- .NET 9 SDK
- Docker Desktop
- Visual Studio 2022 17.9+ or VS Code with C# Dev Kit

### Quick Start

1. **Clone and Setup**:
```bash
git clone https://github.com/signalbeam-io/signalbeam-platform.git
cd signalbeam-platform
```

2. **Start the Complete Platform**:
```bash
cd src/backend/SignalBeam.Platform.AppHost
dotnet run
```

3. **Access Services**:
- **Aspire Dashboard**: https://localhost:15888
- **API Gateway**: https://localhost:7070
- **Individual Services**: Check Aspire dashboard for dynamic ports

### What Aspire Provides

✅ **Service Orchestration**: All microservices + infrastructure  
✅ **Service Discovery**: Automatic inter-service communication  
✅ **Observability**: Built-in telemetry, logs, metrics  
✅ **Configuration**: Centralized settings management  
✅ **Health Checks**: Real-time service health monitoring  
✅ **Resource Management**: Databases, message queues, caches  

### Architecture in Aspire

```
┌─────────────────┐
│  Aspire AppHost │ ← Single entry point
└─────────┬───────┘
          │
    ┌─────▼──────────────────────────┐
    │         Infrastructure          │
    │  • PostgreSQL                  │
    │  • Redis                       │
    │  • ClickHouse                  │
    │  • NATS JetStream              │
    │  • MQTT (Mosquitto)            │
    │  • Jaeger, Prometheus, Grafana │
    └─────┬──────────────────────────┘
          │
    ┌─────▼──────────────────────────┐
    │      SignalBeam Services        │
    │  • API Gateway (YARP)          │
    │  • Control Plane (GraphQL)     │
    │  • Edge Collectors             │
    │  • Ingestion Service           │
    │  • Metrics Processor           │
    │  • Rules Engine                │
    │  • Alerting Service            │
    │  • Auth Service                │
    └────────────────────────────────┘
```

### Development Workflow

#### 1. **Start Development Environment**
```bash
# Start everything
cd src/backend/SignalBeam.Platform.AppHost
dotnet run

# Watch for changes (auto-restart)
dotnet watch run
```

#### 2. **View Service Health**
Open Aspire Dashboard: https://localhost:15888
- Service status and endpoints
- Real-time logs and metrics  
- Resource utilization
- Distributed tracing

#### 3. **Test MQTT Edge Flow**
```bash
# Edge collector will connect to MQTT broker automatically
# Test with Docker MQTT client:
docker run --rm --network signalbeam-network \
  eclipse-mosquitto:2.0 \
  mosquitto_pub -h mosquitto -p 1883 \
  -t "signalbeam/test-device/heartbeat/heartbeat" \
  -m '{"device_id":"test-device","status":"online"}'
```

#### 4. **Access Individual Services**
- **API Gateway**: Entry point for all HTTP requests
- **Control Plane**: GraphQL API at `/graphql`
- **Edge Collectors**: SignalR hubs for real-time communication
- **Aspire Dashboard**: Service mesh visualization

### Debugging

#### Service-Level Debugging
1. Set breakpoints in specific service projects
2. Start Aspire AppHost in debug mode
3. Attach debugger to individual service processes

#### Infrastructure Debugging
- **Logs**: Real-time in Aspire Dashboard
- **Metrics**: OpenTelemetry integration
- **Tracing**: Jaeger integration
- **Health**: Built-in health check endpoints

### Configuration

#### appsettings.json Override
Each service can override settings in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=signalbeam;Username=postgres;Password=..."
  },
  "MessageBroker": {
    "NATS": "nats://localhost:4222",
    "MQTT": "mqtt://localhost:1883"
  }
}
```

#### Environment Variables
Aspire automatically configures:
- Database connection strings
- Message broker endpoints
- Service discovery URLs
- OpenTelemetry settings

### Production vs Development

| Aspect | Development (Aspire) | Production (Kubernetes) |
|--------|---------------------|------------------------|
| **Orchestration** | .NET Aspire | Kubernetes |
| **Service Discovery** | Built-in | DNS/Service Mesh |
| **Configuration** | appsettings.json | ConfigMaps/Secrets |
| **Observability** | Aspire Dashboard | Grafana/Prometheus |
| **Scaling** | Single machine | Multi-node cluster |

### Best Practices

1. **Use Aspire for**:
   - Local development
   - Integration testing
   - Service prototyping
   - End-to-end debugging

2. **Use Docker Compose for**:
   - Infrastructure only
   - Production-like testing
   - CI/CD pipelines

3. **Use Kubernetes for**:
   - Production deployments
   - Multi-environment staging
   - Auto-scaling scenarios

### Troubleshooting

#### Common Issues

**Service not starting**: Check Aspire Dashboard logs  
**Connection errors**: Verify service references in AppHost  
**Port conflicts**: Aspire assigns dynamic ports automatically  
**Missing dependencies**: Ensure all NuGet packages restored  

#### Reset Environment
```bash
# Stop all containers
docker stop $(docker ps -aq)
docker system prune -f

# Clear Aspire state
rm -rf ~/.aspire/
```

### Next Steps

After local development with Aspire:
1. Test with Docker Compose production setup
2. Deploy to Kubernetes using generated manifests
3. Set up CI/CD pipeline for automated deployments
4. Configure monitoring and alerting for production