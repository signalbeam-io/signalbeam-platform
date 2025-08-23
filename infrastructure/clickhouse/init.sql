CREATE DATABASE IF NOT EXISTS signalbeam;

USE signalbeam;

-- Logs table optimized for time-series data
CREATE TABLE IF NOT EXISTS logs (
    timestamp DateTime64(3),
    level Enum8('TRACE' = 1, 'DEBUG' = 2, 'INFO' = 3, 'WARN' = 4, 'ERROR' = 5, 'FATAL' = 6),
    message String,
    service LowCardinality(String),
    host LowCardinality(String),
    trace_id String,
    span_id String,
    labels Map(String, String),
    source_ip IPv4,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (service, level, timestamp)
TTL timestamp + INTERVAL 30 DAY;

-- Metrics table for time-series metrics
CREATE TABLE IF NOT EXISTS metrics (
    timestamp DateTime64(3),
    metric_name LowCardinality(String),
    value Float64,
    tags Map(String, String),
    service LowCardinality(String),
    host LowCardinality(String),
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (metric_name, service, timestamp)
TTL timestamp + INTERVAL 90 DAY;

-- Traces table for distributed tracing
CREATE TABLE IF NOT EXISTS traces (
    trace_id String,
    span_id String,
    parent_span_id String,
    operation_name String,
    start_time DateTime64(3),
    end_time DateTime64(3),
    duration_ms UInt32,
    service LowCardinality(String),
    tags Map(String, String),
    status Enum8('OK' = 1, 'ERROR' = 2, 'TIMEOUT' = 3),
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(start_time)
ORDER BY (service, trace_id, start_time)
TTL start_time + INTERVAL 30 DAY;

-- Events table for security and anomaly events
CREATE TABLE IF NOT EXISTS events (
    id UUID DEFAULT generateUUIDv4(),
    timestamp DateTime64(3),
    event_type LowCardinality(String),
    severity Enum8('LOW' = 1, 'MEDIUM' = 2, 'HIGH' = 3, 'CRITICAL' = 4),
    description String,
    source LowCardinality(String),
    metadata Map(String, String),
    resolved Bool DEFAULT false,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (event_type, severity, timestamp)
TTL timestamp + INTERVAL 180 DAY;