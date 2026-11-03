namespace Nexus.Abstractions.Sensors;

public enum SensorType
{
    Temperature,
    Load,
    ClockSpeed,
    FanRpm,
    Voltage,
    Power,
    MemoryUsage,
    FlowRate,
    Current
}

public enum SensorStatus
{
    Normal,
    Warning,
    Critical,
    Unavailable
}

public interface ISensor
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    SensorType Type { get; }
    SensorStatus Status { get; }
    double? CurrentValue { get; }
    string Unit { get; }
    double? MinValue { get; }
    double? MaxValue { get; }
    double? CriticalThreshold { get; }
    DateTime LastUpdatedUtc { get; }
}

public class SystemTelemetrySnapshot
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    // CPU
    public string CpuName { get; set; } = "Unknown CPU";
    public int CpuLogicalCores { get; set; }
    public double? CpuUsagePercent { get; set; }
    public double? CpuTemperatureCelsius { get; set; }
    public double? CpuPowerWatts { get; set; }
    public double? CpuFrequencyMhz { get; set; }

    // GPU
    public string GpuName { get; set; } = "Unknown GPU";
    public string? GpuDriverVersion { get; set; }
    public double? GpuUsagePercent { get; set; }
    public double? GpuTemperatureCelsius { get; set; }
    public double? GpuMemoryUsedGb { get; set; }
    public double? GpuMemoryTotalGb { get; set; }
    public double? GpuPowerWatts { get; set; }
    public double? GpuFanSpeedPercent { get; set; }

    // Memory
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public double RamUsagePercent => RamTotalGb > 0 ? (RamUsedGb / RamTotalGb) * 100.0 : 0;

    // Storage
    public List<StorageTelemetry> Disks { get; set; } = new();

    // Cooling
    public List<FanTelemetry> Fans { get; set; } = new();

    // System
    public TimeSpan SystemUptime { get; set; }
    public string OperatingSystem { get; set; } = string.Empty;
}

public class StorageTelemetry
{
    public string Name { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public double TotalGb { get; set; }
    public double FreeGb { get; set; }
    public double UsedGb => Math.Max(0, TotalGb - FreeGb);
    public double UsagePercent => TotalGb > 0 ? (UsedGb / TotalGb) * 100.0 : 0;
    public double? TemperatureCelsius { get; set; }
}

public class FanTelemetry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Rpm { get; set; }
    public double? TargetPercent { get; set; }
    public bool IsControlledByNexus { get; set; }
}

public interface IHardwareMonitor
{
    bool IsRunning { get; }
    TimeSpan PollingInterval { get; set; }
    IReadOnlyList<ISensor> Sensors { get; }
    SystemTelemetrySnapshot CurrentSnapshot { get; }

    event EventHandler<SystemTelemetrySnapshot>? SnapshotUpdated;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<SystemTelemetrySnapshot> RefreshSnapshotAsync(CancellationToken cancellationToken = default);
}
