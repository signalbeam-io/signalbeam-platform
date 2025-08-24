# SignalBeam  

> **Illuminate your systems with unified observability**  
SignalBeam is an open-source observability platform designed for modern applications, IoT, and edge environments. It enables **log monitoring, distributed tracing, metrics collection, and edge analytics** under a single unified "beam" metaphor.  

SignalBeam is lightweight, cloud-native, and designed to scale seamlessly from a **single edge device** to **enterprise-wide monitoring**.  

---

## 🌟 Capabilities  

- **Log Monitoring (SignalBeam Core)** – Collect, aggregate, and search logs from apps, containers, and on-prem systems.  
- **Edge Monitoring (SignalBeam Edge)** – Stream logs and metrics from IoT devices and gateways.  
- **Distributed Tracing (SignalBeam Trace)** – Trace requests across microservices and hybrid environments.  
- **Metrics (SignalBeam Metrics)** – Collect time-series metrics for performance and capacity planning.  
- **Security Analytics** – Detect anomalies and correlate events at edge or central nodes.  
- **GDPR & Compliance Ready** – Designed with data locality and privacy in mind.  

---

## 🛠️ Technologies  

- **Backend:** .NET 9, ASP.NET Core, gRPC, HotChocolate (GraphQL)  
- **Frontend:** React (TypeScript, Apollo Client, TailwindCSS)  
- **Data Layer:** PostgreSQL, Redis (caching), TimescaleDB (metrics)  
- **Messaging:** RabbitMQ / Azure Service Bus  
- **Containerization & Orchestration:** Docker, Kubernetes, Azure Container Apps  
- **Provisioning & Deployment:** Terraform, GitHub Actions, ArgoCD  
- **Edge Runtime:** Lightweight collectors (Rust/Go agents)  
- **Observability:** OpenTelemetry (OTel)  

## 📐 Architecture  

SignalBeam follows a **modular architecture** with clear separation between edge agents, central backend, and visualization layer.  

### C1 Model – System Context  

![C1 System Context](https://www.plantuml.com/plantuml/proxy?cache=no&src=https://raw.githubusercontent.com/signalbeam-io/signalbeam-platform/main/docs/c1-system-context.puml)

### C2 Model – Container Diagram  

![C2 Container Diagram](https://www.plantuml.com/plantuml/proxy?cache=no&src=https://raw.githubusercontent.com/signalbeam-io/signalbeam-platform/main/docs/c2-container-diagram.puml)



## 🚀 Getting Started  

### Prerequisites  
- Docker & Docker Compose  
- Kubernetes cluster (local: Kind/K3s, or cloud: AKS, GKE, EKS)  
- Terraform & kubectl  
- .NET 8 SDK & Node.js (for local development)  

### Installation  

```bash
# Clone the repository
git clone https://github.com/<org>/signalbeam.git
cd signalbeam

# Start with Docker Compose
docker compose up -d
```
