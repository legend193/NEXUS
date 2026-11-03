using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.EventBus;

namespace Nexus.Core.DeviceManager;

public class DeviceManager
{
    private readonly ConcurrentDictionary<string, IDevice> _devices = new();
    private readonly List<IDeviceProvider> _providers = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<DeviceManager>? _logger;

    public IReadOnlyList<IDevice> Devices => _devices.Values.ToList();

    public event EventHandler<IDevice>? DeviceAdded;
    public event EventHandler<string>? DeviceRemoved;

    public DeviceManager(IEventBus eventBus, ILogger<DeviceManager>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public void RegisterProvider(IDeviceProvider provider)
    {
        _providers.Add(provider);
        provider.DeviceConnected += OnProviderDeviceConnected;
        provider.DeviceDisconnected += OnProviderDeviceDisconnected;
    }

    public async Task DiscoverAllAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Discovering devices across {Count} registered providers...", _providers.Count);

        foreach (var provider in _providers)
        {
            if (!provider.IsSupportedOnCurrentSystem)
            {
                _logger?.LogInformation("Provider {ProviderName} is not supported on this hardware configuration.", provider.ProviderName);
                continue;
            }

            try
            {
                var discovered = await provider.DiscoverDevicesAsync(cancellationToken);
                foreach (var device in discovered)
                {
                    AddOrUpdateDevice(device);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error discovering devices from provider {ProviderName}", provider.ProviderName);
            }
        }
    }

    public void AddOrUpdateDevice(IDevice device)
    {
        _devices.AddOrUpdate(device.Id, device, (_, _) => device);
        DeviceAdded?.Invoke(this, device);
        _ = _eventBus.PublishAsync(device);
    }

    public void RemoveDevice(string deviceId)
    {
        if (_devices.TryRemove(deviceId, out var removed))
        {
            DeviceRemoved?.Invoke(this, deviceId);
        }
    }

    private void OnProviderDeviceConnected(object? sender, IDevice device)
    {
        AddOrUpdateDevice(device);
    }

    private void OnProviderDeviceDisconnected(object? sender, string deviceId)
    {
        RemoveDevice(deviceId);
    }
}
