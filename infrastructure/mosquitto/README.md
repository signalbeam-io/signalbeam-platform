# SignalBeam MQTT Broker

Eclipse Mosquitto 2.0 configured with mTLS authentication for secure edge device communication.

## Architecture

```
Edge Device (mTLS) → Port 8883 → Mosquitto → [Future: Edge Gateway Service]
```

## Security Features

- **mTLS Authentication**: Client certificates required for connection
- **Topic-based Authorization**: ACL restricts device access to own topics  
- **TLS 1.2+**: Strong cipher suites, no legacy protocols
- **Certificate-based Identity**: No shared passwords

## Ports

- **1883**: Standard MQTT (development only, allow_anonymous=false)
- **8883**: MQTT over TLS (production, requires client certificates)
- **9001**: WebSockets over TLS (web client support)

## Topic Structure

Edge devices publish to structured topics:

```
signalbeam/{device_id}/metrics/metrics     - System metrics
signalbeam/{device_id}/logs/logs           - Log entries (future)
signalbeam/{device_id}/events/events       - System events (future)
signalbeam/{device_id}/heartbeat/heartbeat - Device heartbeat
```

## Certificate Management

### Generate Certificates

```bash
cd infrastructure/mosquitto/certs
./generate-certs.sh
```

Creates:
- `ca.crt` - Certificate Authority (distribute to all devices)
- `server.crt/key` - MQTT broker certificates
- `client.crt/key` - Example device certificates

### Device Certificate Creation

For each new edge device:

```bash
# Generate device private key
openssl genrsa -out device-{id}.key 4096

# Create certificate signing request
openssl req -new -key device-{id}.key -out device-{id}.csr \
  -subj "/C=US/ST=CA/L=San Francisco/O=SignalBeam/OU=Edge/CN={device-id}"

# Sign with CA
openssl x509 -req -in device-{id}.csr -CA ca.crt -CAkey ca.key \
  -CAcreateserial -out device-{id}.crt -days 365
```

## Edge Collector Configuration

Update edge collector `config.yaml`:

```yaml
mqtt:
  broker: "tls://signalbeam-mosquitto:8883"
  ca_cert: "/path/to/ca.crt"
  cert_file: "/path/to/client.crt"
  key_file: "/path/to/client.key"
  qos: 1
  timeout: 30s
```

## Connection Testing

### Quick Test Setup

1. **Start MQTT Broker**:
```bash
docker-compose up -d mosquitto

# Check if running
docker-compose ps mosquitto
docker-compose logs mosquitto
```

2. **Test mTLS Connection** (using Docker client):
```bash
cd infrastructure/mosquitto/certs

# Test secure publish (should succeed)
docker run --rm --network signalbeam-network \
  -v $(pwd):/certs eclipse-mosquitto:2.0 \
  mosquitto_pub -h signalbeam-mosquitto -p 8883 \
  --cafile /certs/ca.crt \
  --cert /certs/client.crt \
  --key /certs/client.key \
  -t "signalbeam/edge-device-001/heartbeat/heartbeat" \
  -m '{"device_id":"edge-device-001","status":"online","timestamp":1693478400}' -d
```

**Expected Output**:
```
Client null sending CONNECT
Client null received CONNACK (0)
Client null sending PUBLISH (d0, q0, r0, m1, 'signalbeam/edge-device-001/heartbeat/heartbeat', ... (72 bytes))
Client null sending DISCONNECT
```

3. **Test Security** (should fail):
```bash
# Test insecure connection (should be rejected)
docker run --rm --network signalbeam-network \
  eclipse-mosquitto:2.0 \
  mosquitto_pub -h signalbeam-mosquitto -p 1883 \
  -t "test/topic" \
  -m "test message" -d
```

**Expected Output**:
```
Connection error: Connection Refused: not authorised.
Error: The connection was refused.
```

4. **Test Subscription**:
```bash
# In one terminal - subscribe to heartbeat messages
docker run --rm --network signalbeam-network \
  -v $(pwd):/certs eclipse-mosquitto:2.0 \
  mosquitto_sub -h signalbeam-mosquitto -p 8883 \
  --cafile /certs/ca.crt \
  --cert /certs/client.crt \
  --key /certs/client.key \
  -t "signalbeam/+/heartbeat/heartbeat" -v

# In another terminal - publish messages
docker run --rm --network signalbeam-network \
  -v $(pwd):/certs eclipse-mosquitto:2.0 \
  mosquitto_pub -h signalbeam-mosquitto -p 8883 \
  --cafile /certs/ca.crt \
  --cert /certs/client.crt \
  --key /certs/client.key \
  -t "signalbeam/edge-device-001/metrics/metrics" \
  -m '{"device_id":"edge-device-001","cpu":{"usage_percent":45.2},"timestamp":"2024-01-20T10:30:00Z"}'
```

### Testing with Local MQTT Client Tools

If you have `mosquitto-clients` installed locally:

```bash
# Subscribe to device metrics (requires valid client certificate)
mosquitto_sub -h localhost -p 8883 \
  --cafile certs/ca.crt \
  --cert certs/client.crt \
  --key certs/client.key \
  -t "signalbeam/+/metrics/metrics"

# Publish test message
mosquitto_pub -h localhost -p 8883 \
  --cafile certs/ca.crt \
  --cert certs/client.crt \
  --key certs/client.key \
  -t "signalbeam/edge-device-001/heartbeat/heartbeat" \
  -m '{"device_id":"edge-device-001","status":"online"}'
```

### Test Results Verification

✅ **mTLS connection succeeds** (CONNACK 0)  
✅ **Anonymous connection fails** (CONNACK 5 - not authorised)  
✅ **Messages publish/subscribe correctly**  
✅ **ACL enforces topic restrictions**  

### Troubleshooting

- **Certificate errors**: Verify certificates exist and have correct permissions
- **Connection refused**: Check if Mosquitto is running and ports are accessible
- **Docker network issues**: Ensure using `--network signalbeam-network`
- **File permissions**: ACL file should have restricted permissions (`chmod 600`)

## Access Control

ACL rules ensure:
- Devices can only access their own topics (`signalbeam/{device_id}/*`)
- Platform services have full access (`signalbeam-platform` user)
- Read access to configuration topics
- System monitoring access

## Production Deployment

1. **Certificate Distribution**: Securely distribute CA and client certificates
2. **Certificate Rotation**: Plan for certificate expiration (365 days default)
3. **Monitoring**: Monitor broker logs and connection metrics
4. **Firewall**: Restrict port 8883 to known device networks
5. **Backup**: Backup CA private key securely

## Integration Points

- **Edge Collector**: Publishes telemetry via mTLS
- **Edge Gateway Service**: Subscribes to all device topics, bridges to NATS
- **Platform Services**: Full topic access via `signalbeam-platform` certificate