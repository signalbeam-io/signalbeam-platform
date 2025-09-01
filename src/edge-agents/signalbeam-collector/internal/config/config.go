package config

import (
	"fmt"
	"os"
	"time"

	"gopkg.in/yaml.v3"
)

// Config represents the edge collector configuration
type Config struct {
	Device     DeviceConfig     `yaml:"device"`
	MQTT       MQTTConfig       `yaml:"mqtt"`
	Collection CollectionConfig `yaml:"collection"`
	Logging    LoggingConfig    `yaml:"logging"`
}

// DeviceConfig contains device-specific settings
type DeviceConfig struct {
	ID       string            `yaml:"id"`
	Name     string            `yaml:"name"`
	Location string            `yaml:"location"`
	Tags     map[string]string `yaml:"tags"`
}

// MQTTConfig contains MQTT broker connection settings
type MQTTConfig struct {
	Broker   string        `yaml:"broker"`
	ClientID string        `yaml:"client_id"`
	Username string        `yaml:"username"`
	Password string        `yaml:"password"`
	QoS      byte          `yaml:"qos"`
	Retained bool          `yaml:"retained"`
	Timeout  time.Duration `yaml:"timeout"`
	Topics   TopicsConfig  `yaml:"topics"`
}

// TopicsConfig defines MQTT topic structure
type TopicsConfig struct {
	Prefix    string `yaml:"prefix"`
	Metrics   string `yaml:"metrics"`
	Logs      string `yaml:"logs"`
	Events    string `yaml:"events"`
	Heartbeat string `yaml:"heartbeat"`
}

// CollectionConfig defines what data to collect and how often
type CollectionConfig struct {
	Interval time.Duration     `yaml:"interval"`
	Metrics  MetricsConfig     `yaml:"metrics"`
	Logs     LogsConfig        `yaml:"logs"`
	Events   EventsConfig      `yaml:"events"`
}

// MetricsConfig defines system metrics collection
type MetricsConfig struct {
	Enabled bool `yaml:"enabled"`
	CPU     bool `yaml:"cpu"`
	Memory  bool `yaml:"memory"`
	Disk    bool `yaml:"disk"`
	Network bool `yaml:"network"`
	Load    bool `yaml:"load"`
}

// LogsConfig defines log collection settings
type LogsConfig struct {
	Enabled bool     `yaml:"enabled"`
	Paths   []string `yaml:"paths"`
	Exclude []string `yaml:"exclude"`
}

// EventsConfig defines system event collection
type EventsConfig struct {
	Enabled bool `yaml:"enabled"`
	Types   []string `yaml:"types"`
}

// LoggingConfig defines collector logging settings
type LoggingConfig struct {
	Level  string `yaml:"level"`
	Format string `yaml:"format"`
}

// Load reads and parses the configuration file
func Load(path string) (*Config, error) {
	// Set defaults
	cfg := &Config{
		Device: DeviceConfig{
			ID:   generateDeviceID(),
			Name: "SignalBeam Edge Device",
		},
		MQTT: MQTTConfig{
			Broker:   "tcp://localhost:1883",
			ClientID: "",
			QoS:      1,
			Retained: false,
			Timeout:  30 * time.Second,
			Topics: TopicsConfig{
				Prefix:    "signalbeam",
				Metrics:   "metrics",
				Logs:      "logs", 
				Events:    "events",
				Heartbeat: "heartbeat",
			},
		},
		Collection: CollectionConfig{
			Interval: 30 * time.Second,
			Metrics: MetricsConfig{
				Enabled: true,
				CPU:     true,
				Memory:  true,
				Disk:    true,
				Network: true,
				Load:    true,
			},
			Logs: LogsConfig{
				Enabled: false,
				Paths:   []string{},
			},
			Events: EventsConfig{
				Enabled: false,
				Types:   []string{},
			},
		},
		Logging: LoggingConfig{
			Level:  "info",
			Format: "text",
		},
	}

	// Read config file if it exists
	if _, err := os.Stat(path); err == nil {
		data, err := os.ReadFile(path)
		if err != nil {
			return nil, fmt.Errorf("failed to read config file: %w", err)
		}

		if err := yaml.Unmarshal(data, cfg); err != nil {
			return nil, fmt.Errorf("failed to parse config file: %w", err)
		}
	}

	// Set client ID if empty
	if cfg.MQTT.ClientID == "" {
		cfg.MQTT.ClientID = fmt.Sprintf("signalbeam-%s", cfg.Device.ID)
	}

	// Validate configuration
	if err := cfg.validate(); err != nil {
		return nil, fmt.Errorf("invalid configuration: %w", err)
	}

	return cfg, nil
}

// validate checks if the configuration is valid
func (c *Config) validate() error {
	if c.Device.ID == "" {
		return fmt.Errorf("device.id is required")
	}
	if c.MQTT.Broker == "" {
		return fmt.Errorf("mqtt.broker is required")
	}
	if c.Collection.Interval <= 0 {
		return fmt.Errorf("collection.interval must be positive")
	}
	return nil
}

// generateDeviceID creates a unique device identifier
func generateDeviceID() string {
	hostname, err := os.Hostname()
	if err != nil {
		return fmt.Sprintf("device-%d", time.Now().Unix())
	}
	return hostname
}