package collector

import (
	"context"
	"encoding/json"
	"fmt"
	"sync"
	"time"

	mqtt "github.com/eclipse/paho.mqtt.golang"
	"github.com/signalbeam-io/signalbeam-platform/edge-agents/signalbeam-collector/internal/config"
	"github.com/signalbeam-io/signalbeam-platform/edge-agents/signalbeam-collector/internal/metrics"
	"github.com/sirupsen/logrus"
)

// Collector represents the main edge data collector
type Collector struct {
	config     *config.Config
	logger     *logrus.Entry
	mqttClient mqtt.Client
	metrics    *metrics.Collector
	stopCh     chan struct{}
	wg         sync.WaitGroup
}

// TelemetryData represents data sent from edge to cloud
type TelemetryData struct {
	DeviceID  string                 `json:"device_id"`
	Timestamp time.Time              `json:"timestamp"`
	Type      string                 `json:"type"` // "metrics", "logs", "events"
	Data      map[string]interface{} `json:"data"`
	Tags      map[string]string      `json:"tags"`
}

// New creates a new edge collector instance
func New(cfg *config.Config, logger *logrus.Entry) (*Collector, error) {
	// Create MQTT client
	opts := mqtt.NewClientOptions()
	opts.AddBroker(cfg.MQTT.Broker)
	opts.SetClientID(cfg.MQTT.ClientID)
	opts.SetUsername(cfg.MQTT.Username)
	opts.SetPassword(cfg.MQTT.Password)
	opts.SetConnectTimeout(cfg.MQTT.Timeout)
	opts.SetKeepAlive(60 * time.Second)
	opts.SetDefaultPublishHandler(func(client mqtt.Client, msg mqtt.Message) {
		logger.WithFields(logrus.Fields{
			"topic":   msg.Topic(),
			"payload": string(msg.Payload()),
		}).Debug("Received MQTT message")
	})
	opts.SetConnectionLostHandler(func(client mqtt.Client, err error) {
		logger.WithError(err).Error("MQTT connection lost")
	})

	mqttClient := mqtt.NewClient(opts)

	// Create metrics collector
	metricsCollector, err := metrics.New(logger)
	if err != nil {
		return nil, fmt.Errorf("failed to create metrics collector: %w", err)
	}

	return &Collector{
		config:     cfg,
		logger:     logger,
		mqttClient: mqttClient,
		metrics:    metricsCollector,
		stopCh:     make(chan struct{}),
	}, nil
}

// Start begins the collection and transmission of telemetry data
func (c *Collector) Start(ctx context.Context) error {
	c.logger.Info("Starting edge collector")

	// Connect to MQTT broker
	if token := c.mqttClient.Connect(); token.Wait() && token.Error() != nil {
		return fmt.Errorf("failed to connect to MQTT broker: %w", token.Error())
	}
	c.logger.Info("Connected to MQTT broker")

	// Send initial heartbeat
	c.sendHeartbeat()

	// Start collection goroutines
	if c.config.Collection.Metrics.Enabled {
		c.wg.Add(1)
		go c.collectMetrics(ctx)
	}

	// Start heartbeat goroutine
	c.wg.Add(1)
	go c.heartbeatLoop(ctx)

	// Wait for context cancellation
	<-ctx.Done()
	return nil
}

// Stop gracefully stops the collector
func (c *Collector) Stop(ctx context.Context) error {
	c.logger.Info("Stopping edge collector")

	// Signal all goroutines to stop
	close(c.stopCh)

	// Wait for goroutines to finish with timeout
	done := make(chan struct{})
	go func() {
		c.wg.Wait()
		close(done)
	}()

	select {
	case <-done:
		c.logger.Info("All goroutines stopped")
	case <-ctx.Done():
		c.logger.Warn("Shutdown timeout reached")
	}

	// Disconnect from MQTT
	if c.mqttClient.IsConnected() {
		c.mqttClient.Disconnect(1000)
		c.logger.Info("Disconnected from MQTT broker")
	}

	return nil
}

// collectMetrics periodically collects and sends system metrics
func (c *Collector) collectMetrics(ctx context.Context) {
	defer c.wg.Done()

	ticker := time.NewTicker(c.config.Collection.Interval)
	defer ticker.Stop()

	for {
		select {
		case <-ticker.C:
			c.gatherAndSendMetrics()
		case <-c.stopCh:
			return
		case <-ctx.Done():
			return
		}
	}
}

// gatherAndSendMetrics collects system metrics and sends them via MQTT
func (c *Collector) gatherAndSendMetrics() {
	metricsData, err := c.metrics.Collect(c.config.Collection.Metrics)
	if err != nil {
		c.logger.WithError(err).Error("Failed to collect metrics")
		return
	}

	telemetry := TelemetryData{
		DeviceID:  c.config.Device.ID,
		Timestamp: time.Now().UTC(),
		Type:      "metrics",
		Data:      metricsData,
		Tags:      c.config.Device.Tags,
	}

	if err := c.sendTelemetry("metrics", telemetry); err != nil {
		c.logger.WithError(err).Error("Failed to send metrics")
	}
}

// heartbeatLoop sends periodic heartbeats
func (c *Collector) heartbeatLoop(ctx context.Context) {
	defer c.wg.Done()

	ticker := time.NewTicker(60 * time.Second) // Heartbeat every minute
	defer ticker.Stop()

	for {
		select {
		case <-ticker.C:
			c.sendHeartbeat()
		case <-c.stopCh:
			return
		case <-ctx.Done():
			return
		}
	}
}

// sendHeartbeat sends a heartbeat message
func (c *Collector) sendHeartbeat() {
	heartbeat := map[string]interface{}{
		"device_id":   c.config.Device.ID,
		"device_name": c.config.Device.Name,
		"location":    c.config.Device.Location,
		"timestamp":   time.Now().UTC().Unix(),
		"status":      "online",
		"version":     "0.1.0",
	}

	data, err := json.Marshal(heartbeat)
	if err != nil {
		c.logger.WithError(err).Error("Failed to marshal heartbeat")
		return
	}

	topic := c.getTopicName("heartbeat")
	token := c.mqttClient.Publish(topic, c.config.MQTT.QoS, c.config.MQTT.Retained, data)
	if token.Wait() && token.Error() != nil {
		c.logger.WithError(token.Error()).Error("Failed to send heartbeat")
	}
}

// sendTelemetry sends telemetry data via MQTT
func (c *Collector) sendTelemetry(dataType string, telemetry TelemetryData) error {
	data, err := json.Marshal(telemetry)
	if err != nil {
		return fmt.Errorf("failed to marshal telemetry: %w", err)
	}

	topic := c.getTopicName(dataType)
	token := c.mqttClient.Publish(topic, c.config.MQTT.QoS, c.config.MQTT.Retained, data)
	if token.Wait() && token.Error() != nil {
		return fmt.Errorf("failed to publish to MQTT: %w", token.Error())
	}

	c.logger.WithFields(logrus.Fields{
		"topic": topic,
		"size":  len(data),
		"type":  dataType,
	}).Debug("Sent telemetry data")

	return nil
}

// getTopicName constructs MQTT topic name
func (c *Collector) getTopicName(dataType string) string {
	var topicSuffix string
	switch dataType {
	case "metrics":
		topicSuffix = c.config.MQTT.Topics.Metrics
	case "logs":
		topicSuffix = c.config.MQTT.Topics.Logs
	case "events":
		topicSuffix = c.config.MQTT.Topics.Events
	case "heartbeat":
		topicSuffix = c.config.MQTT.Topics.Heartbeat
	default:
		topicSuffix = dataType
	}

	return fmt.Sprintf("%s/%s/%s/%s",
		c.config.MQTT.Topics.Prefix,
		c.config.Device.ID,
		topicSuffix,
		dataType,
	)
}