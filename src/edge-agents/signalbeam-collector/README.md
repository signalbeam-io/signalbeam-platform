# SignalBeam Edge Collector

A lightweight, cross-platform telemetry collector for IoT and edge devices.

## Features

- **Cross-platform**: Works on Linux, macOS, Windows, ARM devices (Raspberry Pi, etc.)
- **MQTT Communication**: Uses industry-standard MQTT for reliable edge-to-cloud messaging
- **System Metrics**: Collects CPU, memory, disk, network, and load metrics
- **Configurable**: YAML-based configuration with sensible defaults
- **Lightweight**: Minimal resource footprint for constrained devices
- **Resilient**: Auto-reconnection and graceful error handling

## Quick Start

### 1. Build the Collector

```bash
# For current platform
go build -o signalbeam-collector ./cmd

# For Raspberry Pi (ARM64)
GOOS=linux GOARCH=arm64 go build -o signalbeam-collector-arm64 ./cmd

# For Raspberry Pi (ARM32)
GOOS=linux GOARCH=arm go build -o signalbeam-collector-arm ./cmd
```

### 2. Configuration

Copy and modify the configuration file:

```bash
cp config.yaml my-config.yaml
```

Edit `my-config.yaml`:

```yaml
device:
  name: "Raspberry Pi 5 - Living Room"
  location: "home/living-room"
  tags:
    environment: "production"
    zone: "home-iot"

mqtt:
  broker: "tcp://your-mqtt-broker:1883"
  username: "edge-device"
  password: "your-password"
```

### 3. Run the Collector

```bash
# With default config
./signalbeam-collector

# With custom config
./signalbeam-collector -config my-config.yaml
```

## Configuration Options

### Device Configuration

```yaml
device:
  id: ""  # Auto-generated from hostname if empty
  name: "SignalBeam Edge Device"
  location: "default"
  tags:
    environment: "development"
    zone: "edge"
```

### MQTT Configuration

```yaml
mqtt:
  broker: "tcp://localhost:1883"
  username: ""
  password: ""
  qos: 1  # 0, 1, or 2
  retained: false
  timeout: 30s
```

### Collection Configuration

```yaml
collection:
  interval: 30s  # How often to collect metrics
  metrics:
    enabled: true
    cpu: true      # CPU usage and info
    memory: true   # RAM and swap usage
    disk: true     # Disk usage and I/O
    network: true  # Network interface stats
    load: true     # System load averages
```

### Logging Configuration

```yaml
logging:
  level: "info"    # trace, debug, info, warn, error
  format: "text"   # text or json
```

## MQTT Topics

The collector publishes data to structured MQTT topics:

```
signalbeam/{device_id}/metrics/metrics     - System metrics
signalbeam/{device_id}/logs/logs           - Log entries (future)
signalbeam/{device_id}/events/events       - System events (future)
signalbeam/{device_id}/heartbeat/heartbeat - Device heartbeat
```

## Data Format

### Metrics Message

```json
{
  "device_id": "raspberrypi5",
  "timestamp": "2024-01-20T10:30:00Z",
  "type": "metrics",
  "data": {
    "system": {
      "hostname": "raspberrypi5",
      "uptime": 86400,
      "os": "linux",
      "platform": "debian"
    },
    "cpu": {
      "usage_percent": 25.5,
      "count": 4
    },
    "memory": {
      "virtual": {
        "total": 8589934592,
        "used": 4294967296,
        "used_percent": 50.0
      }
    }
  },
  "tags": {
    "environment": "production",
    "zone": "home-iot"
  }
}
```

### Heartbeat Message

```json
{
  "device_id": "raspberrypi5",
  "device_name": "Raspberry Pi 5 - Living Room",
  "location": "home/living-room",
  "timestamp": 1705747800,
  "status": "online",
  "version": "0.1.0"
}
```

## Deployment

### Raspberry Pi Service

Create a systemd service file `/etc/systemd/system/signalbeam-collector.service`:

```ini
[Unit]
Description=SignalBeam Edge Collector
After=network.target

[Service]
Type=simple
User=pi
WorkingDirectory=/home/pi/signalbeam
ExecStart=/home/pi/signalbeam/signalbeam-collector -config /home/pi/signalbeam/config.yaml
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable signalbeam-collector
sudo systemctl start signalbeam-collector
```

### Docker

```dockerfile
FROM golang:1.23-alpine AS builder
WORKDIR /app
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN go build -o signalbeam-collector ./cmd

FROM alpine:latest
RUN apk --no-cache add ca-certificates
WORKDIR /root/
COPY --from=builder /app/signalbeam-collector .
COPY --from=builder /app/config.yaml .
CMD ["./signalbeam-collector"]
```

## Development

### Dependencies

```bash
go mod tidy
```

### Testing

```bash
go test ./...
```

### Cross-compilation

```bash
# Linux ARM64 (Raspberry Pi 4/5)
GOOS=linux GOARCH=arm64 go build -o dist/signalbeam-collector-linux-arm64 ./cmd

# Linux ARM32 (Raspberry Pi 2/3)
GOOS=linux GOARCH=arm GOARM=7 go build -o dist/signalbeam-collector-linux-arm ./cmd

# Linux x86_64
GOOS=linux GOARCH=amd64 go build -o dist/signalbeam-collector-linux-amd64 ./cmd

# Windows x86_64
GOOS=windows GOARCH=amd64 go build -o dist/signalbeam-collector-windows-amd64.exe ./cmd

# macOS ARM64
GOOS=darwin GOARCH=arm64 go build -o dist/signalbeam-collector-darwin-arm64 ./cmd
```