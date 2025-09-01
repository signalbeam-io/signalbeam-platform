package main

import (
	"context"
	"flag"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/signalbeam-io/signalbeam-platform/edge-agents/signalbeam-collector/internal/collector"
	"github.com/signalbeam-io/signalbeam-platform/edge-agents/signalbeam-collector/internal/config"
	"github.com/sirupsen/logrus"
)

func main() {
	var configPath = flag.String("config", "config.yaml", "Path to configuration file")
	flag.Parse()

	// Load configuration
	cfg, err := config.Load(*configPath)
	if err != nil {
		logrus.WithError(err).Fatal("Failed to load configuration")
	}

	// Setup logging
	level, err := logrus.ParseLevel(cfg.Logging.Level)
	if err != nil {
		logrus.WithError(err).Warn("Invalid log level, defaulting to info")
		level = logrus.InfoLevel
	}
	logrus.SetLevel(level)
	
	if cfg.Logging.Format == "json" {
		logrus.SetFormatter(&logrus.JSONFormatter{})
	}

	logger := logrus.WithFields(logrus.Fields{
		"component": "signalbeam-collector",
		"version":   "0.1.0",
		"device_id": cfg.Device.ID,
	})

	logger.Info("Starting SignalBeam Edge Collector")

	// Create collector instance
	c, err := collector.New(cfg, logger)
	if err != nil {
		logger.WithError(err).Fatal("Failed to create collector")
	}

	// Setup graceful shutdown
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM)

	// Start collector
	go func() {
		if err := c.Start(ctx); err != nil {
			logger.WithError(err).Error("Collector failed")
			cancel()
		}
	}()

	// Wait for shutdown signal
	select {
	case sig := <-sigCh:
		logger.WithField("signal", sig).Info("Received shutdown signal")
	case <-ctx.Done():
		logger.Info("Context cancelled")
	}

	// Graceful shutdown with timeout
	shutdownCtx, shutdownCancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer shutdownCancel()

	logger.Info("Shutting down collector...")
	if err := c.Stop(shutdownCtx); err != nil {
		logger.WithError(err).Error("Error during shutdown")
	}

	logger.Info("SignalBeam Edge Collector stopped")
}