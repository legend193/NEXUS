using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Audio;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Sensors;

namespace Nexus.Integrations.Generic;

public class GenericDevice : IDevice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public DeviceType Type { get; init; }
    public DeviceConnectionStatus Status { get; set; } = DeviceConnectionStatus.Connected;
    public DeviceCapabilities Capabilities { get; init; }
    public string? FirmwareVersion { get; init; }
    public string IntegrationProvider { get; init; } = "Windows Native";
    public IReadOnlyList<IDeviceControl> Controls { get; init; } = Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class GenericWindowsDeviceProvider : IDeviceProvider
{
    private readonly ILogger<GenericWindowsDeviceProvider>? _logger;

    public string ProviderId => "provider-windows-native";
    public string ProviderName => "Windows Hardware & PnP Subsystem";
    public string Manufacturer => "Microsoft / Windows";
    public bool IsSupportedOnCurrentSystem => OperatingSystem.IsWindows();

    public event EventHandler<IDevice>? DeviceConnected;
    public event EventHandler<string>? DeviceDisconnected;

    public GenericWindowsDeviceProvider(ILogger<GenericWindowsDeviceProvider>? logger = null)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();

        // 1. CPU Device
        try
        {
            string cpuName = "Central Processing Unit";
            string cpuManufacturer = "Generic";
            using var searcher = new ManagementObjectSearcher("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                cpuName = obj["Name"]?.ToString()?.Trim() ?? cpuName;
                cpuManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? cpuManufacturer;
                break;
            }

            devices.Add(new GenericDevice
            {
                Id = "dev-cpu-primary",
                Name = cpuName,
                Manufacturer = cpuManufacturer,
                Model = cpuName,
                Type = DeviceType.Cpu,
                Capabilities = DeviceCapabilities.TemperatureSensor | DeviceCapabilities.ClockSensor | DeviceCapabilities.PowerSensor,
                Properties = new Dictionary<string, string>
                {
                    { "Cores", Environment.ProcessorCount.ToString() },
                    { "Architecture", RuntimeInformation.ProcessArchitecture.ToString() }
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to query CPU device.");
        }

        // 2. GPU Devices
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, VideoProcessor, AdapterRAM FROM Win32_VideoController");
            int gpuIndex = 0;
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(name) || name.Contains("Basic", StringComparison.OrdinalIgnoreCase)) continue;

                var driver = obj["DriverVersion"]?.ToString()?.Trim() ?? "Standard";
                string mfg = "Display Adapter";
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) mfg = "NVIDIA";
                else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) mfg = "AMD";
                else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) mfg = "Intel";

                devices.Add(new GenericDevice
                {
                    Id = $"dev-gpu-{gpuIndex++}",
                    Name = name,
                    Manufacturer = mfg,
                    Model = name,
                    Type = DeviceType.Gpu,
                    FirmwareVersion = $"Driver {driver}",
                    Capabilities = DeviceCapabilities.TemperatureSensor | DeviceCapabilities.ClockSensor | DeviceCapabilities.PowerSensor | DeviceCapabilities.FanControl,
                    Properties = new Dictionary<string, string>
                    {
                        { "Driver", driver }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to query GPU devices.");
        }

        // 3. Motherboard Device
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                var mfg = obj["Manufacturer"]?.ToString()?.Trim() ?? "Motherboard";
                var prod = obj["Product"]?.ToString()?.Trim() ?? "Standard Motherboard";
                var serial = obj["SerialNumber"]?.ToString()?.Trim() ?? "N/A";

                devices.Add(new GenericDevice
                {
                    Id = "dev-motherboard-primary",
                    Name = $"{mfg} {prod}",
                    Manufacturer = mfg,
                    Model = prod,
                    SerialNumber = serial,
                    Type = DeviceType.Motherboard,
                    Capabilities = DeviceCapabilities.TemperatureSensor | DeviceCapabilities.FanControl | DeviceCapabilities.VoltageSensor
                });
                break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to query BaseBoard.");
        }

        return Task.FromResult<IReadOnlyList<IDevice>>(devices);
    }
}
