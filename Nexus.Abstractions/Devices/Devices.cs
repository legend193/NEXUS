namespace Nexus.Abstractions.Devices;

public enum DeviceType
{
    Motherboard,
    Cpu,
    Gpu,
    Memory,
    Storage,
    Fan,
    LiquidCooler,
    Keyboard,
    Mouse,
    Headset,
    Microphone,
    LightingController,
    PowerSupply,
    Generic
}

[Flags]
public enum DeviceCapabilities
{
    None = 0,
    RgbLighting = 1 << 0,
    FanControl = 1 << 1,
    TemperatureSensor = 1 << 2,
    VoltageSensor = 1 << 3,
    PowerSensor = 1 << 4,
    ClockSensor = 1 << 5,
    AudioPlayback = 1 << 6,
    AudioRecording = 1 << 7,
    FirmwareUpdate = 1 << 8,
    PerformanceProfiles = 1 << 9
}

public enum DeviceConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Degraded,
    Error,
    Unsupported
}

public interface IDeviceControl
{
    string ControlId { get; }
    string Name { get; }
    string Description { get; }
    bool IsAvailable { get; }
    Task<bool> ApplyAsync(object parameter, CancellationToken cancellationToken = default);
}

public interface IDevice
{
    string Id { get; }
    string Name { get; }
    string Manufacturer { get; }
    string Model { get; }
    string SerialNumber { get; }
    DeviceType Type { get; }
    DeviceConnectionStatus Status { get; }
    DeviceCapabilities Capabilities { get; }
    string? FirmwareVersion { get; }
    string IntegrationProvider { get; }
    IReadOnlyList<IDeviceControl> Controls { get; }
    IReadOnlyDictionary<string, string> Properties { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

public interface IDeviceProvider
{
    string ProviderId { get; }
    string ProviderName { get; }
    string Manufacturer { get; }
    bool IsSupportedOnCurrentSystem { get; }
    
    event EventHandler<IDevice>? DeviceConnected;
    event EventHandler<string>? DeviceDisconnected;

    Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default);
}
