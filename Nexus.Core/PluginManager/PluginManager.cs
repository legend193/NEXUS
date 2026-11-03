using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Plugins;

namespace Nexus.Core.PluginManager;

public class PluginManager : IPluginManager
{
    private readonly List<PluginMetadata> _plugins = new();
    private readonly ILogger<PluginManager>? _logger;

    public IReadOnlyList<PluginMetadata> InstalledPlugins => _plugins;

    public PluginManager(ILogger<PluginManager>? logger = null)
    {
        _logger = logger;
        // Built-in manufacturer integration modules
        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-generic-windows",
            Name = "Windows System & Audio Provider",
            Version = "1.0.0",
            Manufacturer = "Microsoft / Windows",
            Description = "Native Core Audio, Performance Counters, and WMI telemetry integration.",
            SupportedDevices = new() { "CPU", "RAM", "Disks", "Speakers", "Microphones" },
            IsEnabled = true,
            Status = "Active"
        });

        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-openrgb",
            Name = "OpenRGB Ecosystem Bridge",
            Version = "1.0.0",
            Manufacturer = "OpenRGB",
            Description = "Direct communication with OpenRGB server for cross-vendor RGB peripherals and motherboards.",
            SupportedDevices = new() { "Motherboards", "RAM", "ARGB Headers", "Keyboards", "Mice" },
            IsEnabled = true,
            Status = "Ready"
        });

        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-corsair",
            Name = "Corsair iCUE Integration",
            Version = "1.0.0",
            Manufacturer = "Corsair",
            Description = "Corsair Commander, Dominator RAM, and K/M peripheral lighting and telemetry.",
            SupportedDevices = new() { "Corsair Keyboards", "Dominator RAM", "Commander Core" },
            IsEnabled = true,
            Status = "Standby"
        });

        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-logitech",
            Name = "Logitech LIGHTSYNC Bridge",
            Version = "1.0.0",
            Manufacturer = "Logitech G",
            Description = "Logitech G-series mice, keyboards, and speaker lighting control.",
            SupportedDevices = new() { "G Pro Wireless", "G502", "G915", "G733" },
            IsEnabled = true,
            Status = "Standby"
        });

        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-razer",
            Name = "Razer Chroma Connect",
            Version = "1.0.0",
            Manufacturer = "Razer",
            Description = "Razer Chroma RGB devices via Chroma REST API / SDK.",
            SupportedDevices = new() { "Chroma Keyboards", "DeathAdder", "Basilisk", "Chroma ARGB" },
            IsEnabled = true,
            Status = "Standby"
        });

        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-asus",
            Name = "ASUS Aura Integration",
            Version = "1.0.0",
            Manufacturer = "ASUS",
            Description = "ROG and TUF motherboards, GPUs, and addressable headers.",
            SupportedDevices = new() { "ROG Motherboards", "TUF Graphics Cards", "Aura Headers" },
            IsEnabled = true,
            Status = "Standby"
        });

        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-msi",
            Name = "MSI Mystic Light Adapter",
            Version = "1.0.0",
            Manufacturer = "MSI",
            Description = "MSI Gaming motherboards and Mystic Light RGB peripherals.",
            SupportedDevices = new() { "MSI Motherboards", "Gaming Trio GPUs" },
            IsEnabled = true,
            Status = "Standby"
        });

        _plugins.Add(new PluginMetadata
        {
            Id = "nexus-nvidia",
            Name = "NVIDIA Hardware Telemetry",
            Version = "1.0.0",
            Manufacturer = "NVIDIA",
            Description = "GeForce RTX/GTX GPU telemetry, power consumption, clock speeds, and thermal sensors.",
            SupportedDevices = new() { "NVIDIA GeForce RTX", "NVIDIA GeForce GTX" },
            IsEnabled = true,
            Status = "Active"
        });
    }

    public Task LoadPluginsAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Loaded {Count} plugins.", _plugins.Count);
        return Task.CompletedTask;
    }

    public Task EnablePluginAsync(string pluginId)
    {
        var p = _plugins.FirstOrDefault(x => x.Id == pluginId);
        if (p != null)
        {
            p.IsEnabled = true;
            p.Status = "Active";
            _logger?.LogInformation("Plugin {Name} enabled.", p.Name);
        }
        return Task.CompletedTask;
    }

    public Task DisablePluginAsync(string pluginId)
    {
        var p = _plugins.FirstOrDefault(x => x.Id == pluginId);
        if (p != null)
        {
            p.IsEnabled = false;
            p.Status = "Disabled";
            _logger?.LogInformation("Plugin {Name} disabled.", p.Name);
        }
        return Task.CompletedTask;
    }
}
