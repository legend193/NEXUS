using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;
using Nexus.Integrations.OpenRGB;

namespace Nexus.Integrations.Memory;

/// <summary>
/// Autonomous Hardware Driver for Corsair VENGEANCE RGB RT DDR4 Memory Modules (4x 8GB Quad Kit).
/// Controls physical RAM LEDs directly via OpenRGB SMBus / PawnIO Driver (Zero iCUE bloat).
/// Manages 40 addressable LEDs across 4 DIMM slots (10 zones per module).
/// </summary>
public class CorsairVengeanceRgbController : IRgbDevice, IDisposable, IAsyncDisposable
{
    private readonly ILogger? _logger;
    private readonly List<RgbZone> _zones = new();
    private readonly object _ioLock = new();
    private readonly SemaphoreSlim _dramLock = new(1, 1);
    private double _brightness = 1.0;
    private bool _isDisposed;

    private readonly OpenRgbClient _openRgbClient;
    private readonly List<(int ControllerIndex, int LedCount)> _dramControllers = new();
    private bool _isConnectedToOpenRgb;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private RgbColor _lastAppliedColor = new(0, 0, 0, 0);
    private DateTime _lastAllColorUpdate = DateTime.MinValue;
    private readonly DateTime[] _lastZoneTimes = new DateTime[4];

    public string Id { get; } = "corsair-vengeance-rgb-rt-32gb";
    public string Name { get; } = "Corsair VENGEANCE RGB RT (4x 8GB)";
    public string Manufacturer { get; } = "Corsair";
    public string Model { get; } = "VENGEANCE RGB RT DDR4-3600 (4x 8GB Quad-Kit)";
    public string SerialNumber { get; } = "CMN16GX4M2Z3600C18";
    public DeviceType Type => DeviceType.Memory;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Connected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
    public string? FirmwareVersion { get; private set; } = "v1.03.22";
    public string IntegrationProvider => "OpenRGB SMBus / PawnIO Autonomous Driver";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;

    public double Brightness
    {
        get => _brightness;
        set => _brightness = Math.Clamp(value, 0.0, 1.0);
    }

    public CorsairVengeanceRgbController(ILogger? logger = null)
    {
        _logger = logger;
        _openRgbClient = new OpenRgbClient(host: "127.0.0.1", port: 6742, logger: logger);

        // 4 Physical DIMM Slots with 10 addressable LEDs per module
        _zones.Add(new RgbZone("dimm-1", "DIMM Slot 1 (Channel A1)", 10));
        _zones.Add(new RgbZone("dimm-2", "DIMM Slot 2 (Channel A2)", 10));
        _zones.Add(new RgbZone("dimm-3", "DIMM Slot 3 (Channel B1)", 10));
        _zones.Add(new RgbZone("dimm-4", "DIMM Slot 4 (Channel B2)", 10));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Status = DeviceConnectionStatus.Connected;
        _logger?.LogInformation("Initializing Corsair VENGEANCE RGB RT Memory Driver via OpenRGB SMBus.");
        
        for (int retry = 0; retry < 6; retry++)
        {
            bool ok = await TryConnectAndDiscoverAsync(cancellationToken);
            if (ok && _dramControllers.Count > 0)
            {
                break;
            }
            await Task.Delay(800, cancellationToken);
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RegisterDramController(int controllerIndex, int ledCount, string name)
    {
        lock (_ioLock)
        {
            if (!_dramControllers.Exists(c => c.ControllerIndex == controllerIndex))
            {
                _dramControllers.Add((controllerIndex, ledCount));
                _isConnectedToOpenRgb = true;
                _ = _openRgbClient.SetCustomModeAsync(controllerIndex);
                _logger?.LogInformation("Hardware RAM Controller mapped & Direct Mode enabled: [{Index}] {Name} with {Leds} LEDs", 
                    controllerIndex, name, ledCount);

                if (_lastAppliedColor != default)
                {
                    var stickColors = new RgbColor[ledCount];
                    Array.Fill(stickColors, _lastAppliedColor);
                    _ = _openRgbClient.UpdateLedsAsync(controllerIndex, stickColors);
                }
            }
        }
    }

    private async Task<bool> TryConnectAndDiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnectedToOpenRgb && _dramControllers.Count > 0) return true;

        if ((DateTime.UtcNow - _lastConnectionAttempt).TotalSeconds < 2)
            return false;

        _lastConnectionAttempt = DateTime.UtcNow;

        await _dramLock.WaitAsync(cancellationToken);
        try
        {
            if (_isConnectedToOpenRgb && _dramControllers.Count > 0) return true;

            if (!_openRgbClient.IsConnected)
            {
                bool connected = await _openRgbClient.ConnectAsync(cancellationToken);
                if (!connected)
                {
                    TryStartOpenRgbServer();
                    await Task.Delay(1000, cancellationToken);
                    connected = await _openRgbClient.ConnectAsync(cancellationToken);
                }

                if (!connected)
                {
                    _logger?.LogDebug("OpenRGB hardware server not yet reachable on port 6742.");
                    return false;
                }
            }

            int count = await _openRgbClient.GetControllerCountAsync(cancellationToken);
            if (count == 0) return false;

            _dramControllers.Clear();

            for (int i = 0; i < count; i++)
            {
                var info = await _openRgbClient.GetControllerDataAsync(i, cancellationToken);
                if (info != null)
                {
                    bool isDram = info.Type == 1 ||
                                  info.Name.Contains("DRAM", StringComparison.OrdinalIgnoreCase) ||
                                  info.Name.Contains("Corsair", StringComparison.OrdinalIgnoreCase) ||
                                  info.Name.Contains("Vengeance", StringComparison.OrdinalIgnoreCase) ||
                                  info.Description.Contains("CMN", StringComparison.OrdinalIgnoreCase);

                    if (isDram)
                    {
                        int leds = info.LedCount > 0 ? info.LedCount : 10;
                        _dramControllers.Add((i, leds));
                        await _openRgbClient.SetCustomModeAsync(i, cancellationToken);
                        _logger?.LogInformation("Hardware RAM Controller mapped & Direct Mode enabled: [{Index}] {Name} ({Vendor}) with {Leds} LEDs", 
                            i, info.Name, info.Vendor, leds);
                    }
                }
            }

            _isConnectedToOpenRgb = _dramControllers.Count > 0;
            return _isConnectedToOpenRgb;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "OpenRGB hardware DRAM connection attempt exception.");
            return false;
        }
        finally
        {
            _dramLock.Release();
        }
    }

    private static readonly object _openRgbLaunchLock = new();

    private void TryStartOpenRgbServer()
    {
        lock (_openRgbLaunchLock)
        {
            try
            {
                var existing = Process.GetProcessesByName("OpenRGB");
                if (existing.Length > 1)
                {
                    // Kill duplicate instances so only exactly 1 OpenRGB process remains
                    for (int i = 1; i < existing.Length; i++)
                    {
                        try { existing[i].Kill(); } catch { }
                    }
                    return;
                }
                else if (existing.Length == 1)
                {
                    return;
                }

                // 1. Ensure PawnIO kernel driver service is running
                try
                {
                    var psiPawn = new ProcessStartInfo
                    {
                        FileName = "net.exe",
                        Arguments = "start PawnIO",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var procPawn = Process.Start(psiPawn);
                    procPawn?.WaitForExit(1000);
                }
                catch { }

                if (Process.GetProcessesByName("OpenRGB").Length > 0) return;

                // 2. Direct process launch (inherits Administrator privileges from Nexus.App)
                string openRgbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NEXUS", "tools", "OpenRGB", "OpenRGB Windows 64-bit", "OpenRGB.exe");

                if (File.Exists(openRgbPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = openRgbPath,
                        Arguments = "--server --startminimized",
                        WorkingDirectory = Path.GetDirectoryName(openRgbPath),
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                }
            }
            catch { }
        }
    }

    public async Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        byte r = (byte)(color.R * _brightness);
        byte g = (byte)(color.G * _brightness);
        byte b = (byte)(color.B * _brightness);
        var effectiveColor = new RgbColor(r, g, b);

        lock (_ioLock)
        {
            if (_isConnectedToOpenRgb && _dramControllers.Count > 0 && effectiveColor == _lastAppliedColor && (DateTime.UtcNow - _lastAllColorUpdate).TotalMilliseconds < 1500)
            {
                return;
            }
            _lastAppliedColor = effectiveColor;
            _lastAllColorUpdate = DateTime.UtcNow;

            foreach (var zone in _zones)
            {
                Array.Fill(zone.CurrentColors, effectiveColor);
            }
        }

        if (!_isConnectedToOpenRgb || _dramControllers.Count == 0)
        {
            await TryConnectAndDiscoverAsync(cancellationToken);
        }

        if (_isConnectedToOpenRgb && _dramControllers.Count > 0)
        {
            foreach (var (idx, ledCount) in _dramControllers)
            {
                var stickColors = new RgbColor[ledCount];
                Array.Fill(stickColors, effectiveColor);
                await _openRgbClient.UpdateLedsAsync(idx, stickColors, cancellationToken);
            }
        }
    }

    public async Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        var zoneIndex = _zones.FindIndex(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
        if (zoneIndex < 0 || colors.IsEmpty) return;
        var zone = _zones[zoneIndex];

        var color = colors.Span[0];
        byte r = (byte)(color.R * _brightness);
        byte g = (byte)(color.G * _brightness);
        byte b = (byte)(color.B * _brightness);
        var effectiveColor = new RgbColor(r, g, b);

        lock (_ioLock)
        {
            if (zoneIndex < _lastZoneTimes.Length)
            {
                if (effectiveColor == zone.CurrentColors[0] && (DateTime.UtcNow - _lastZoneTimes[zoneIndex]).TotalMilliseconds < 1500)
                {
                    return;
                }
                if ((DateTime.UtcNow - _lastZoneTimes[zoneIndex]).TotalMilliseconds < 75)
                {
                    return;
                }
                _lastZoneTimes[zoneIndex] = DateTime.UtcNow;
            }
            Array.Fill(zone.CurrentColors, effectiveColor);
        }

        if (!_isConnectedToOpenRgb || _dramControllers.Count == 0)
        {
            await TryConnectAndDiscoverAsync(cancellationToken);
        }

        if (_isConnectedToOpenRgb && _dramControllers.Count > 0)
        {
            if (zoneIndex < _dramControllers.Count)
            {
                var (idx, ledCount) = _dramControllers[zoneIndex];
                var stickColors = new RgbColor[ledCount];
                Array.Fill(stickColors, effectiveColor);
                await _openRgbClient.UpdateLedsAsync(idx, stickColors, cancellationToken);
            }
            else if (_dramControllers.Count == 1)
            {
                var (idx, ledCount) = _dramControllers[0];
                var allLeds = new RgbColor[ledCount];
                int offset = 0;
                lock (_ioLock)
                {
                    foreach (var z in _zones)
                    {
                        for (int i = 0; i < z.LedCount && offset < ledCount; i++)
                        {
                            allLeds[offset++] = z.CurrentColors[i];
                        }
                    }
                }
                await _openRgbClient.UpdateLedsAsync(idx, allLeds, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _openRgbClient.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
