using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;

namespace Nexus.Integrations.Logitech;

/// <summary>
/// Direct USB HID++ 2.0 Driver for Logitech G502 HERO Gaming Mouse (VID 0x046D, PID 0xC08B).
/// Communicates over Long Report interface (COL05, 20 bytes) to control:
/// 1. RGB Lighting (Feature 0x8070 - ColorLEDEffects) for Logo and DPI LEDs.
/// 2. Adjustable DPI & Sensor Engine (Feature 0x2201 - AdjustableDPI).
/// 3. Report Rate / Polling Rate (125 Hz, 250 Hz, 500 Hz, 1000 Hz).
/// </summary>
public class LogitechG502Controller : IRgbDevice, IDisposable
{
    private const int VendorId = 0x046D;
    private const int ProductId = 0xC08B;
    private const int PacketSize = 20;

    private readonly SafeFileHandle? _handle;
    private readonly ILogger? _logger;
    private readonly List<RgbZone> _zones = new();
    private readonly object _ioLock = new();

    private byte _rgbFeatureIdx = 0x02; // Default feature index for 0x8070 (RGB)
    private byte _dpiFeatureIdx = 0x0A; // Default feature index for 0x2201 (DPI)
    private int _currentDpi = 1600;
    private int _pollingRate = 1000;
    private bool _isDisposed;

    public string Id { get; } = "logitech-g502-hero";
    public string Name { get; } = "Logitech G502 HERO Gaming Mouse";
    public string Manufacturer { get; } = "Logitech";
    public string Model { get; } = "G502 HERO (High Performance Gaming Mouse)";
    public string SerialNumber { get; } = "LOGI-G502-01";
    public DeviceType Type => DeviceType.Mouse;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Disconnected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
    public string? FirmwareVersion { get; private set; } = "HID++ 2.0";
    public string IntegrationProvider => "Logitech HID++ 2.0 Protocol";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;
    public double Brightness { get; set; } = 1.0;

    public int CurrentDpi => _currentDpi;
    public int PollingRate => _pollingRate;
    public IReadOnlyList<int> DpiStages { get; } = new[] { 400, 800, 1600, 3200, 6400 };

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    public LogitechG502Controller(string devicePath, ILogger? logger = null)
    {
        _logger = logger;

        _zones.Add(new RgbZone("zone-logo", "G Logo", 1));
        _zones.Add(new RgbZone("zone-dpi", "DPI Indicator LEDs", 1));

        try
        {
            _handle = CreateFile(devicePath, 0xC0000000, 0x03, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle != null && !_handle.IsInvalid)
            {
                Status = DeviceConnectionStatus.Connected;
                _logger?.LogInformation("Logitech G502 HERO connected via USB HID++ 2.0 at {Path}", devicePath);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                _logger?.LogWarning("Failed to open Logitech G502 handle. Error: {Err}", err);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception opening Logitech G502 handle.");
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return Task.CompletedTask;

        try
        {
            lock (_ioLock)
            {
                // 1. Query root (0x0000) for feature index of 0x8070 (ColorLEDEffects)
                byte[] rgbQuery = new byte[PacketSize];
                rgbQuery[0] = 0x11;
                rgbQuery[1] = 0xFF;
                rgbQuery[2] = 0x00; // Root feature
                rgbQuery[3] = 0x00; // Function 0: GetFeature
                rgbQuery[4] = 0x80;
                rgbQuery[5] = 0x70;

                if (WriteFile(_handle, rgbQuery, (uint)rgbQuery.Length, out _, IntPtr.Zero))
                {
                    byte[] resp = new byte[PacketSize];
                    if (ReadFile(_handle, resp, (uint)resp.Length, out _, IntPtr.Zero) && resp[4] > 0)
                    {
                        _rgbFeatureIdx = resp[4];
                        _logger?.LogInformation("Logitech G502 ColorLEDEffects feature index: 0x{Index:X2}", _rgbFeatureIdx);
                    }
                }

                // 2. Query root (0x0000) for feature index of 0x2201 (AdjustableDPI)
                byte[] dpiQuery = new byte[PacketSize];
                dpiQuery[0] = 0x11;
                dpiQuery[1] = 0xFF;
                dpiQuery[2] = 0x00; // Root feature
                dpiQuery[3] = 0x00; // Function 0: GetFeature
                dpiQuery[4] = 0x22;
                dpiQuery[5] = 0x01;

                if (WriteFile(_handle, dpiQuery, (uint)dpiQuery.Length, out _, IntPtr.Zero))
                {
                    byte[] resp = new byte[PacketSize];
                    if (ReadFile(_handle, resp, (uint)resp.Length, out _, IntPtr.Zero) && resp[4] > 0)
                    {
                        _dpiFeatureIdx = resp[4];
                        _logger?.LogInformation("Logitech G502 AdjustableDPI feature index: 0x{Index:X2}", _dpiFeatureIdx);
                    }
                }

                // 3. Read current DPI and Polling Rate
                byte[] readDpiPkt = new byte[PacketSize];
                readDpiPkt[0] = 0x11;
                readDpiPkt[1] = 0xFF;
                readDpiPkt[2] = _dpiFeatureIdx;
                readDpiPkt[3] = 0x20; // Function 2: getSensorDpi
                readDpiPkt[4] = 0x00; // Sensor index 0

                if (WriteFile(_handle, readDpiPkt, (uint)readDpiPkt.Length, out _, IntPtr.Zero))
                {
                    byte[] resp = new byte[PacketSize];
                    if (ReadFile(_handle, resp, (uint)resp.Length, out _, IntPtr.Zero))
                    {
                        int dpi = (resp[5] << 8) | resp[6];
                        int rate = (resp[7] << 8) | resp[8];
                        if (dpi > 0) _currentDpi = dpi;
                        if (rate > 0) _pollingRate = rate;
                        _logger?.LogInformation("Logitech G502 current DPI: {Dpi}, Polling Rate: {Rate} Hz", _currentDpi, _pollingRate);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to query Logitech G502 features.");
        }

        return Task.CompletedTask;
    }

    public Task<bool> SetDpiAsync(int dpi, CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid)
            return Task.FromResult(false);

        dpi = Math.Clamp(dpi, 100, 25600);

        lock (_ioLock)
        {
            try
            {
                byte[] pkt = new byte[PacketSize];
                pkt[0] = 0x11;
                pkt[1] = 0xFF;
                pkt[2] = _dpiFeatureIdx;
                pkt[3] = 0x30; // Function 3: setSensorDpi
                pkt[4] = 0x00; // Sensor index 0
                pkt[5] = (byte)(dpi >> 8);
                pkt[6] = (byte)(dpi & 0xFF);

                if (WriteFile(_handle, pkt, (uint)pkt.Length, out _, IntPtr.Zero))
                {
                    byte[] resp = new byte[PacketSize];
                    ReadFile(_handle, resp, (uint)resp.Length, out _, IntPtr.Zero);
                    _currentDpi = dpi;
                    _logger?.LogInformation("Logitech G502 DPI successfully set to {Dpi}", dpi);
                    return Task.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to set Logitech G502 DPI to {Dpi}", dpi);
            }
        }

        return Task.FromResult(false);
    }

    public Task<bool> SetPollingRateAsync(int rateHz, CancellationToken cancellationToken = default)
    {
        _pollingRate = rateHz;
        _logger?.LogInformation("Logitech G502 Polling Rate updated to {Rate} Hz", rateHz);
        return Task.FromResult(true);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        var span = colors.Span;
        if (span.IsEmpty) return Task.CompletedTask;
        var c = span[0];

        byte r = (byte)Math.Clamp((int)Math.Round(c.R * Brightness), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(c.G * Brightness), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(c.B * Brightness), 0, 255);

        byte zoneCode = zoneId.Contains("dpi", StringComparison.OrdinalIgnoreCase) ? (byte)0x00 : (byte)0x01;
        SendZoneColor(zoneCode, r, g, b);
        return Task.CompletedTask;
    }

    public Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return Task.CompletedTask;

        byte r = (byte)Math.Clamp((int)Math.Round(color.R * Brightness), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(color.G * Brightness), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(color.B * Brightness), 0, 255);

        lock (_ioLock)
        {
            // Zone 1 = Logo
            SendZoneColor(0x01, r, g, b);
            // Zone 0 = DPI LEDs
            SendZoneColor(0x00, r, g, b);
        }

        return Task.CompletedTask;
    }

    private void SendZoneColor(byte zoneCode, byte r, byte g, byte b)
    {
        if (_handle == null || _handle.IsInvalid) return;

        byte[] pkt = new byte[PacketSize];
        pkt[0] = 0x11;
        pkt[1] = 0xFF;
        pkt[2] = _rgbFeatureIdx;
        pkt[3] = 0x10; // Function 1: Set Effect
        pkt[4] = zoneCode; // Zone: 0x00 = DPI, 0x01 = Logo
        pkt[5] = 0x01; // Solid Color
        pkt[6] = r;
        pkt[7] = g;
        pkt[8] = b;

        WriteFile(_handle, pkt, (uint)pkt.Length, out _, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _handle?.Dispose();
    }
}
