package metrics

import (
	"fmt"
	"runtime"

	"github.com/shirou/gopsutil/v3/cpu"
	"github.com/shirou/gopsutil/v3/disk"
	"github.com/shirou/gopsutil/v3/host"
	"github.com/shirou/gopsutil/v3/load"
	"github.com/shirou/gopsutil/v3/mem"
	"github.com/shirou/gopsutil/v3/net"
	"github.com/signalbeam-io/signalbeam-platform/edge-agents/signalbeam-collector/internal/config"
	"github.com/sirupsen/logrus"
)

// Collector handles system metrics collection
type Collector struct {
	logger *logrus.Entry
}

// New creates a new metrics collector
func New(logger *logrus.Entry) (*Collector, error) {
	return &Collector{
		logger: logger,
	}, nil
}

// Collect gathers system metrics based on configuration
func (c *Collector) Collect(cfg config.MetricsConfig) (map[string]interface{}, error) {
	metrics := make(map[string]interface{})

	// Add system info
	metrics["system"] = c.getSystemInfo()

	// Collect CPU metrics
	if cfg.CPU {
		cpuMetrics, err := c.getCPUMetrics()
		if err != nil {
			c.logger.WithError(err).Warn("Failed to collect CPU metrics")
		} else {
			metrics["cpu"] = cpuMetrics
		}
	}

	// Collect memory metrics
	if cfg.Memory {
		memMetrics, err := c.getMemoryMetrics()
		if err != nil {
			c.logger.WithError(err).Warn("Failed to collect memory metrics")
		} else {
			metrics["memory"] = memMetrics
		}
	}

	// Collect disk metrics
	if cfg.Disk {
		diskMetrics, err := c.getDiskMetrics()
		if err != nil {
			c.logger.WithError(err).Warn("Failed to collect disk metrics")
		} else {
			metrics["disk"] = diskMetrics
		}
	}

	// Collect network metrics
	if cfg.Network {
		netMetrics, err := c.getNetworkMetrics()
		if err != nil {
			c.logger.WithError(err).Warn("Failed to collect network metrics")
		} else {
			metrics["network"] = netMetrics
		}
	}

	// Collect load metrics
	if cfg.Load {
		loadMetrics, err := c.getLoadMetrics()
		if err != nil {
			c.logger.WithError(err).Warn("Failed to collect load metrics")
		} else {
			metrics["load"] = loadMetrics
		}
	}

	return metrics, nil
}

// getSystemInfo returns basic system information
func (c *Collector) getSystemInfo() map[string]interface{} {
	info, err := host.Info()
	if err != nil {
		c.logger.WithError(err).Warn("Failed to get host info")
		return map[string]interface{}{
			"os":       runtime.GOOS,
			"arch":     runtime.GOARCH,
			"cpus":     runtime.NumCPU(),
			"goroutines": runtime.NumGoroutine(),
		}
	}

	return map[string]interface{}{
		"hostname":          info.Hostname,
		"uptime":           info.Uptime,
		"boot_time":        info.BootTime,
		"procs":            info.Procs,
		"os":               info.OS,
		"platform":         info.Platform,
		"platform_family":  info.PlatformFamily,
		"platform_version": info.PlatformVersion,
		"kernel_version":   info.KernelVersion,
		"kernel_arch":      info.KernelArch,
		"virtualization_system": info.VirtualizationSystem,
		"virtualization_role":   info.VirtualizationRole,
		"host_id":          info.HostID,
	}
}

// getCPUMetrics returns CPU usage metrics
func (c *Collector) getCPUMetrics() (map[string]interface{}, error) {
	// Get CPU percentages
	percentages, err := cpu.Percent(0, false)
	if err != nil {
		return nil, fmt.Errorf("failed to get CPU percentages: %w", err)
	}

	// Get CPU times
	times, err := cpu.Times(false)
	if err != nil {
		return nil, fmt.Errorf("failed to get CPU times: %w", err)
	}

	// Get CPU info
	info, err := cpu.Info()
	if err != nil {
		return nil, fmt.Errorf("failed to get CPU info: %w", err)
	}

	metrics := map[string]interface{}{
		"usage_percent": 0.0,
		"count":         len(info),
	}

	if len(percentages) > 0 {
		metrics["usage_percent"] = percentages[0]
	}

	if len(times) > 0 {
		t := times[0]
		metrics["times"] = map[string]interface{}{
			"user":      t.User,
			"system":    t.System,
			"idle":      t.Idle,
			"nice":      t.Nice,
			"iowait":    t.Iowait,
			"irq":       t.Irq,
			"softirq":   t.Softirq,
			"steal":     t.Steal,
			"guest":     t.Guest,
			"guest_nice": t.GuestNice,
		}
	}

	if len(info) > 0 {
		i := info[0]
		metrics["info"] = map[string]interface{}{
			"vendor_id":   i.VendorID,
			"family":      i.Family,
			"model":       i.Model,
			"model_name":  i.ModelName,
			"stepping":    i.Stepping,
			"mhz":         i.Mhz,
			"cache_size":  i.CacheSize,
			"cores":       i.Cores,
			"flags":       i.Flags,
		}
	}

	return metrics, nil
}

// getMemoryMetrics returns memory usage metrics
func (c *Collector) getMemoryMetrics() (map[string]interface{}, error) {
	// Virtual memory
	vmem, err := mem.VirtualMemory()
	if err != nil {
		return nil, fmt.Errorf("failed to get virtual memory stats: %w", err)
	}

	// Swap memory
	swap, err := mem.SwapMemory()
	if err != nil {
		return nil, fmt.Errorf("failed to get swap memory stats: %w", err)
	}

	return map[string]interface{}{
		"virtual": map[string]interface{}{
			"total":        vmem.Total,
			"available":    vmem.Available,
			"used":         vmem.Used,
			"used_percent": vmem.UsedPercent,
			"free":         vmem.Free,
			"active":       vmem.Active,
			"inactive":     vmem.Inactive,
			"buffers":      vmem.Buffers,
			"cached":       vmem.Cached,
			"shared":       vmem.Shared,
		},
		"swap": map[string]interface{}{
			"total":        swap.Total,
			"used":         swap.Used,
			"used_percent": swap.UsedPercent,
			"free":         swap.Free,
		},
	}, nil
}

// getDiskMetrics returns disk usage metrics
func (c *Collector) getDiskMetrics() (map[string]interface{}, error) {
	// Get disk usage for root partition
	usage, err := disk.Usage("/")
	if err != nil {
		return nil, fmt.Errorf("failed to get disk usage: %w", err)
	}

	// Get disk IO stats
	ioStats, err := disk.IOCounters()
	if err != nil {
		return nil, fmt.Errorf("failed to get disk IO stats: %w", err)
	}

	metrics := map[string]interface{}{
		"usage": map[string]interface{}{
			"path":         usage.Path,
			"fstype":       usage.Fstype,
			"total":        usage.Total,
			"free":         usage.Free,
			"used":         usage.Used,
			"used_percent": usage.UsedPercent,
		},
		"io": make(map[string]interface{}),
	}

	// Add IO stats for each disk
	for name, stat := range ioStats {
		metrics["io"].(map[string]interface{})[name] = map[string]interface{}{
			"read_count":   stat.ReadCount,
			"read_bytes":   stat.ReadBytes,
			"read_time":    stat.ReadTime,
			"write_count":  stat.WriteCount,
			"write_bytes":  stat.WriteBytes,
			"write_time":   stat.WriteTime,
		}
	}

	return metrics, nil
}

// getNetworkMetrics returns network interface metrics
func (c *Collector) getNetworkMetrics() (map[string]interface{}, error) {
	// Get network IO stats
	ioStats, err := net.IOCounters(true)
	if err != nil {
		return nil, fmt.Errorf("failed to get network IO stats: %w", err)
	}

	interfaces := make(map[string]interface{})
	
	for _, stat := range ioStats {
		interfaces[stat.Name] = map[string]interface{}{
			"bytes_sent":   stat.BytesSent,
			"bytes_recv":   stat.BytesRecv,
			"packets_sent": stat.PacketsSent,
			"packets_recv": stat.PacketsRecv,
			"errin":        stat.Errin,
			"errout":       stat.Errout,
			"dropin":       stat.Dropin,
			"dropout":      stat.Dropout,
		}
	}

	return map[string]interface{}{
		"interfaces": interfaces,
	}, nil
}

// getLoadMetrics returns system load metrics
func (c *Collector) getLoadMetrics() (map[string]interface{}, error) {
	loadAvg, err := load.Avg()
	if err != nil {
		return nil, fmt.Errorf("failed to get load average: %w", err)
	}

	return map[string]interface{}{
		"load1":  loadAvg.Load1,
		"load5":  loadAvg.Load5,
		"load15": loadAvg.Load15,
	}, nil
}