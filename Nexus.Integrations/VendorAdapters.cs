using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;

namespace Nexus.Integrations;

public abstract class BaseVendorAdapter : IDeviceProvider
{
    protected readonly ILogger? Logger;

    public abstract string ProviderId { get; }
    public abstract string ProviderName { get; }
    public abstract string Manufacturer { get; }
    public abstract bool IsSupportedOnCurrentSystem { get; }

    public event EventHandler<IDevice>? DeviceConnected;
    public event EventHandler<string>? DeviceDisconnected;

    protected BaseVendorAdapter(ILogger? logger = null)
    {
        Logger = logger;
    }

    public abstract Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default);

    protected void NotifyDeviceConnected(IDevice device) => DeviceConnected?.Invoke(this, device);
    protected void NotifyDeviceDisconnected(string id) => DeviceDisconnected?.Invoke(this, id);
}

// Corsair iCUE Integration
public class CorsairDeviceProvider : BaseVendorAdapter
{
    public override string ProviderId => "provider-corsair-icue";
    public override string ProviderName => "Corsair iCUE Integration";
    public override string Manufacturer => "Corsair";

    public override bool IsSupportedOnCurrentSystem
    {
        get
        {
            // Detect Corsair Service / iCUE installation in standard paths
            return Directory.Exists(@"C:\Program Files\Corsair\Corsair iCUE5 Software") ||
                   Directory.Exists(@"C:\Program Files\Corsair\Corsair iCUE4 Software") ||
                   Process.GetProcessesByName("iCUE").Length > 0;
        }
    }

    public CorsairDeviceProvider(ILogger<CorsairDeviceProvider>? logger = null) : base(logger) { }

    public override Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();
        if (!IsSupportedOnCurrentSystem)
        {
            Logger?.LogInformation("Corsair iCUE service is not running on this host.");
            return Task.FromResult<IReadOnlyList<IDevice>>(devices);
        }

        // When iCUE is running, enumerate through iCUE CueSDK
        return Task.FromResult<IReadOnlyList<IDevice>>(devices);
    }
}

// Razer Chroma Integration
public class RazerChromaDeviceProvider : BaseVendorAdapter
{
    public override string ProviderId => "provider-razer-chroma";
    public override string ProviderName => "Razer Chroma Connect";
    public override string Manufacturer => "Razer";

    public override bool IsSupportedOnCurrentSystem
    {
        get
        {
            return Process.GetProcessesByName("Razer Synapse Service").Length > 0 ||
                   Process.GetProcessesByName("RzSDKService").Length > 0;
        }
    }

    public RazerChromaDeviceProvider(ILogger<RazerChromaDeviceProvider>? logger = null) : base(logger) { }

    public override Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();
        if (!IsSupportedOnCurrentSystem)
        {
            Logger?.LogInformation("Razer Synapse service is not running on this host.");
            return Task.FromResult<IReadOnlyList<IDevice>>(devices);
        }

        return Task.FromResult<IReadOnlyList<IDevice>>(devices);
    }
}

// Logitech LIGHTSYNC Integration
public class LogitechLightsyncDeviceProvider : BaseVendorAdapter
{
    public override string ProviderId => "provider-logitech-ghub";
    public override string ProviderName => "Logitech G HUB Integration";
    public override string Manufacturer => "Logitech G";

    public override bool IsSupportedOnCurrentSystem
    {
        get
        {
            return Process.GetProcessesByName("lghub").Length > 0 ||
                   Process.GetProcessesByName("lghub_agent").Length > 0;
        }
    }

    public LogitechLightsyncDeviceProvider(ILogger<LogitechLightsyncDeviceProvider>? logger = null) : base(logger) { }

    public override Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();
        if (!IsSupportedOnCurrentSystem)
        {
            Logger?.LogInformation("Logitech G HUB is not active on this host.");
            return Task.FromResult<IReadOnlyList<IDevice>>(devices);
        }

        return Task.FromResult<IReadOnlyList<IDevice>>(devices);
    }
}

// ASUS Aura Integration
public class AsusAuraDeviceProvider : BaseVendorAdapter
{
    public override string ProviderId => "provider-asus-aura";
    public override string ProviderName => "ASUS Aura / Armoury Crate Service";
    public override string Manufacturer => "ASUS";

    public override bool IsSupportedOnCurrentSystem
    {
        get
        {
            return Directory.Exists(@"C:\Program Files\ASUS\Aura") ||
                   Process.GetProcessesByName("ArmouryCrate.Service").Length > 0 ||
                   Process.GetProcessesByName("LightingService").Length > 0;
        }
    }

    public AsusAuraDeviceProvider(ILogger<AsusAuraDeviceProvider>? logger = null) : base(logger) { }

    public override Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();
        if (!IsSupportedOnCurrentSystem)
        {
            Logger?.LogInformation("ASUS Aura service is not running on this host.");
            return Task.FromResult<IReadOnlyList<IDevice>>(devices);
        }

        return Task.FromResult<IReadOnlyList<IDevice>>(devices);
    }
}

// MSI Mystic Light Integration
public class MsiMysticLightDeviceProvider : BaseVendorAdapter
{
    public override string ProviderId => "provider-msi-mysticlight";
    public override string ProviderName => "MSI Mystic Light Adapter";
    public override string Manufacturer => "MSI";

    public override bool IsSupportedOnCurrentSystem
    {
        get
        {
            return Process.GetProcessesByName("MSI.CentralServer").Length > 0 ||
                   Directory.Exists(@"C:\Program Files (x86)\MSI\One Dragon Center");
        }
    }

    public MsiMysticLightDeviceProvider(ILogger<MsiMysticLightDeviceProvider>? logger = null) : base(logger) { }

    public override Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();
        if (!IsSupportedOnCurrentSystem)
        {
            Logger?.LogInformation("MSI Center service is not running on this host.");
            return Task.FromResult<IReadOnlyList<IDevice>>(devices);
        }

        return Task.FromResult<IReadOnlyList<IDevice>>(devices);
    }
}
