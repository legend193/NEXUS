using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.EventBus;
using Nexus.Abstractions.Sensors;

namespace Nexus.Engines.Hardware;

public class HardwareEngine : IHardwareMonitor, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<HardwareEngine>? _logger;
    private readonly List<ISensor> _sensors = new();
    private SystemTelemetrySnapshot _currentSnapshot = new();
    private CancellationTokenSource? _cts;
    private Task? _pollLoopTask;
    private bool _isDisposed;

    // CPU Usage calculation using Win32 GetSystemTimes (fast, zero overhead, reliable across all Windows systems)
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out SystemFileTime lpIdleTime, out SystemFileTime lpKernelTime, out SystemFileTime lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemFileTime
    {
        public uint DateTimeLow;
        public uint DateTimeHigh;
        public ulong ToUInt64() => ((ulong)DateTimeHigh << 32) | DateTimeLow;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private bool _hasPrevTimes;

    // Cached hardware static info
    private string _cachedCpuName = string.Empty;
    private string _cachedGpuName = string.Empty;
    private string _cachedGpuDriver = string.Empty;
    private double _cachedGpuTotalVramGb = 0;

    public bool IsRunning => _pollLoopTask != null && !_pollLoopTask.IsCompleted;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(1000);
    public IReadOnlyList<ISensor> Sensors => _sensors;
    public SystemTelemetrySnapshot CurrentSnapshot => _currentSnapshot;

    public event EventHandler<SystemTelemetrySnapshot>? SnapshotUpdated;

    public HardwareEngine(IEventBus eventBus, ILogger<HardwareEngine>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
        InitializeStaticHardwareInfo();
    }

    private void InitializeStaticHardwareInfo()
    {
        try
        {
            // Query CPU Model via WMI
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                _cachedCpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not query Win32_Processor via WMI");
            _cachedCpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Standard CPU";
        }

        try
        {
            // Query GPU via WMI
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(name) && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                {
                    _cachedGpuName = name;
                    _cachedGpuDriver = obj["DriverVersion"]?.ToString()?.Trim() ?? string.Empty;
                    if (obj["AdapterRAM"] != null && ulong.TryParse(obj["AdapterRAM"].ToString(), out var ramBytes) && ramBytes > 0)
                    {
                        _cachedGpuTotalVramGb = ramBytes / (1024.0 * 1024.0 * 1024.0);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not query Win32_VideoController via WMI");
        }

        if (string.IsNullOrEmpty(_cachedGpuName))
        {
            _cachedGpuName = "Integrated Display Controller";
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _pollLoopTask = Task.Run(() => PollingLoopAsync(_cts.Token), CancellationToken.None);
        _logger?.LogInformation("HardwareEngine telemetry monitor started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _cts == null) return;

        _cts.Cancel();
        try
        {
            await _pollLoopTask!;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _pollLoopTask = null;
        }

        _logger?.LogInformation("HardwareEngine telemetry monitor stopped.");
    }

    private async Task PollingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = await RefreshSnapshotAsync(token);
                SnapshotUpdated?.Invoke(this, snapshot);
                await _eventBus.PublishAsync(snapshot, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error occurred during hardware telemetry polling loop.");
            }

            try
            {
                await Task.Delay(PollingInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public Task<SystemTelemetrySnapshot> RefreshSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new SystemTelemetrySnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            CpuName = _cachedCpuName,
            CpuLogicalCores = Environment.ProcessorCount,
            GpuName = _cachedGpuName,
            GpuDriverVersion = _cachedGpuDriver,
            GpuMemoryTotalGb = _cachedGpuTotalVramGb > 0 ? _cachedGpuTotalVramGb : null,
            OperatingSystem = Environment.OSVersion.VersionString,
            SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        };

        // 1. Calculate Real CPU Usage via GetSystemTimes
        if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            ulong idle = idleTime.ToUInt64();
            ulong kernel = kernelTime.ToUInt64();
            ulong user = userTime.ToUInt64();

            if (_hasPrevTimes)
            {
                ulong idleDiff = idle - _prevIdleTime;
                ulong kernelDiff = kernel - _prevKernelTime;
                ulong userDiff = user - _prevUserTime;

                // kernelDiff includes idle time
                ulong totalDiff = kernelDiff + userDiff;
                if (totalDiff > 0)
                {
                    double busy = totalDiff - idleDiff;
                    snapshot.CpuUsagePercent = Math.Clamp((busy / totalDiff) * 100.0, 0.0, 100.0);
                }
            }

            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _hasPrevTimes = true;
        }

        // 2. Real RAM Usage via GlobalMemoryStatusEx
        var memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            snapshot.RamTotalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double availGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            snapshot.RamUsedGb = Math.Max(0, snapshot.RamTotalGb - availGb);
        }

        // 3. Real Storage Drives
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    snapshot.Disks.Add(new StorageTelemetry
                    {
                        Name = drive.Name,
                        VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                        TotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0),
                        FreeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error reading drive info.");
        }

        // 4. Thermal & Sensor readings: ACPI Thermal Zone where technically supported
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (var obj in searcher.Get())
            {
                if (obj["CurrentTemperature"] != null && double.TryParse(obj["CurrentTemperature"].ToString(), out var kelvinTenths))
                {
                    // MSAcpi represents temperature in tenths of Kelvin
                    double celsius = (kelvinTenths / 10.0) - 273.15;
                    if (celsius > 10 && celsius < 120)
                    {
                        snapshot.CpuTemperatureCelsius = Math.Round(celsius, 1);
                        break;
                    }
                }
            }
        }
        catch
        {
            // If ACPI thermal zones are not exposed by the BIOS/Motherboard, we do NOT fake values.
            snapshot.CpuTemperatureCelsius = null;
        }

        _currentSnapshot = snapshot;
        return Task.FromResult(snapshot);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
