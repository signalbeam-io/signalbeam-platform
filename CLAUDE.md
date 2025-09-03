# SignalBeam Platform - Development Reference

This document serves as a comprehensive reference for Claude Code when working on the SignalBeam observability platform.

## Architecture Overview

SignalBeam is a cloud-native observability platform following a microservices architecture with clear separation between edge agents, central backend, and visualization layer.

### System Context (C1)
The platform serves three main user types:
- **Platform Administrators**: Configure platform, manage collectors, create dashboards
- **Developers**: View logs, metrics, traces for debugging
- **Operations Teams**: Monitor alerts, system health, respond to incidents

### Container Architecture (C2)

## Services to Build

### Frontend Layer
- **SignalBeam UI** (React, TypeScript)
  - Status: ✅ Basic structure exists
  - Needs: Dashboards, log/metric/trace visualization, platform configuration
  - Tech: React, TypeScript, Apollo Client, TailwindCSS

### API Gateway & Control Plane
- **API Gateway** (YARP, .NET 9)
  - Status: ✅ Skeleton exists
  - Needs: Request routing, load balancing, authentication
  - Routes to: Control Plane, Edge Collectors, Ingestion services

- **Control Plane API** (GraphQL, HotChocolate, .NET 9)
  - Status: ✅ Skeleton exists
  - Needs: Unified GraphQL API, dashboard management, alert configuration
  - Integrates with: PostgreSQL, ClickHouse, Edge Collectors

### Data Ingestion Services
- **Ingestion Service** (.NET 9, ClickHouse)
  - Status: ✅ Skeleton exists
  - Needs: Telemetry data processing, bulk inserts to time-series DB
  - Integrates with: ClickHouse, NATS JetStream

- **OTel Cloud Service** (.NET 9)
  - Status: ✅ Skeleton exists
  - Needs: OpenTelemetry HTTP/JSON endpoints, data processing
  - Integrates with: NATS JetStream for event publishing

- **Edge Collectors Service** (.NET 9, SignalR)
  - Status: ✅ Skeleton exists
  - Needs: Edge collector management, real-time communication
  - Integrates with: PostgreSQL, NATS JetStream, SignalR WebSockets

### Data Processing Services
- **Data Processor** (.NET 9)
  - Status: ❌ Needs implementation
  - Needs: Real-time metrics/traces processing, aggregations, transformations
  - Integrates with: NATS JetStream, ClickHouse, Rules Engine

- **Metrics Processor** (.NET 9)
  - Status: ⚠️ Basic template only
  - Needs: Time-series metrics processing, aggregations
  - Integrates with: NATS JetStream, ClickHouse

### Edge/IoT Layer
- **Edge Collector Agent** (Go)
  - Status: ✅ Complete - cross-platform Go agent
  - Features: MQTT communication, system metrics, configurable, lightweight
  - Supports: Raspberry Pi, Linux, Windows, macOS, ARM/x86
  - Integrates with: MQTT broker → Edge Gateway Service

### Alert & Rules Services
- **Rules Engine** (.NET 9)
  - Status: ✅ Skeleton exists
  - Needs: Alert rule evaluation, anomaly detection, trigger logic
  - Integrates with: PostgreSQL, ClickHouse, Alerting Service

- **Alerting Service** (.NET 9)
  - Status: ✅ Skeleton exists
  - Needs: Multi-channel alert delivery (email, Slack, PagerDuty)
  - Integrates with: External notification systems

### Data Storage Layer
- **ClickHouse** (Time-series Database)
  - Status: ⚠️ Basic config exists
  - Needs: Schema design, optimization for observability data
  - Stores: Logs, metrics, traces

- **PostgreSQL** (Relational Database)
  - Status: ❌ Needs setup
  - Needs: Schema for dashboards, alerts, users, collector configs
  - Stores: Metadata and configuration

- **Redis** (Cache & Session Store)
  - Status: ❌ Needs setup
  - Needs: Caching layer, session management
  - Used by: Control Plane, Gateway

### Message Queue
- **NATS JetStream** (Cloud-Native Messaging)
  - Status: ❌ Needs implementation
  - Needs: Event streaming, message persistence, replay capability
  - Replaces: RabbitMQ (cloud-native alternative)

## Development Commands

### Build Commands
```bash
# Build entire solution
dotnet build src/backend/SignalBeam.Platform.sln

# Run specific service
dotnet run --project src/backend/SignalBeam.Platform.MetricsProcessor

# Frontend development
cd src/frontend/signalbeam-ui && npm run dev
```

### Test Commands
```bash
# Run backend tests
dotnet test

# Run frontend tests
cd src/frontend/signalbeam-ui && npm test
```

### Infrastructure Commands

#### .NET Aspire (Recommended for Development)
```bash
# Start entire platform with Aspire (includes all services + infrastructure)
cd src/backend/SignalBeam.Platform.AppHost
dotnet run

# Access Aspire Dashboard: https://localhost:15888
# Access API Gateway: https://localhost:7070
# Access Individual Services: See Aspire dashboard for endpoints
```

#### Docker Compose (Alternative)
```bash
# Start infrastructure only
docker-compose up -d

# Start with application services
docker-compose -f docker-compose.yml -f docker-compose.dev.yml up -d

# Apply Terraform infrastructure
cd infrastructure/terraform && terraform apply
```

## Implementation Priority

### Phase 1: IoT/Edge-First Data Flow (CURRENT FOCUS)
1. ✅ **Edge Collector Agent** - Cross-platform Go agent (COMPLETED)
2. ✅ **MQTT Broker Setup** - Eclipse Mosquitto with mTLS authentication (COMPLETED)
3. **Edge Gateway Service** - MQTT to NATS bridge
4. **NATS JetStream Integration** - Cloud-native message streaming
5. **ClickHouse Schema & Integration** - Time-series storage for edge data
6. **Device Registration System** - Edge device management

### Phase 2: Core Processing Pipeline
7. **Data Processor Service** - Real-time processing
8. **Metrics Processor Implementation** - Time-series processing
9. **Enhanced Ingestion Service** - Bulk data handling
10. **PostgreSQL Schema & Setup** - Metadata storage
11. **Redis Caching Layer** - Performance optimization

### Phase 3: User Interface & Monitoring
12. **Edge Monitoring Dashboard** - Device health and metrics visualization
13. **Control Plane GraphQL API** - Unified API layer
14. **SignalBeam UI Enhancement** - Full observability dashboards
15. **API Gateway Enhancement** - Request routing and auth

### Phase 4: Enterprise Features
16. **Rules Engine Logic** - Alert rule processing
17. **Alerting Service Integration** - Multi-channel notifications
18. **OTel Cloud Enhancement** - OpenTelemetry processing
19. **Authentication Integration** - External auth provider
20. **Monitoring & Observability** - Self-monitoring

## Key Integrations Needed

### External Systems
- **Authentication Provider**: Auth0, Azure AD, or similar
- **Notification Systems**: Email SMTP, Slack API, PagerDuty API
- **Monitored Applications**: OpenTelemetry instrumentation
- **Infrastructure Components**: Prometheus exporters, log shippers
- **Edge/IoT Devices**: Lightweight collectors (Rust/Go agents)

### Message Flow Patterns
1. **Ingestion Flow**: Apps/Infrastructure → OTel/Ingestion → NATS JetStream → ClickHouse
2. **Processing Flow**: NATS JetStream → Data Processor → ClickHouse → Rules Engine
3. **Alert Flow**: Rules Engine → Alerting Service → External Notifications
4. **Query Flow**: UI → Gateway → Control Plane → ClickHouse/PostgreSQL
5. **Edge Flow**: Edge Devices → MQTT Broker (mTLS) → Edge Gateway Service → NATS JetStream → Data Processing

## Technology Stack Reference

### Backend (.NET 9)
- **API Framework**: ASP.NET Core, Minimal APIs
- **GraphQL**: HotChocolate
- **Real-time**: SignalR
- **Message Queue**: NATS JetStream
- **ORM**: Entity Framework Core (PostgreSQL)
- **Time-series**: ClickHouse.Client
- **Caching**: StackExchange.Redis
- **Observability**: OpenTelemetry

### Frontend (React)
- **Framework**: React 18+ with TypeScript
- **GraphQL Client**: Apollo Client
- **Styling**: TailwindCSS
- **Build Tool**: Vite
- **State Management**: React Query + Context

### Infrastructure
- **Containerization**: Docker, Docker Compose
- **Orchestration**: Kubernetes
- **Cloud**: Azure Container Apps
- **IaC**: Terraform
- **CI/CD**: GitHub Actions
- **Service Mesh**: Optional (Istio/Linkerd)

## Notes for Claude Code

- Always reference the ServiceDefaults project for common configurations
- Use the established .NET 9 patterns and minimal API approach
- Prioritize cloud-native solutions and Kubernetes deployment
- Follow the microservices communication patterns defined in C2
- Ensure all services include health checks and OpenTelemetry instrumentation
- NATS JetStream is the preferred message queue over RabbitMQ for cloud-native deployment