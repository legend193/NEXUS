using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;

namespace Nexus.Integrations.Keyboard;

/// <summary>
/// Direct USB HID Driver for Gaming Mechanical Keyboard (VID 0x0C45, PID 0x652F Sonix / Gaming Keyboard).
/// Implements standard OpenRGB Sonix RGB protocol over Interface 1 COL04.
/// </summary>
public class SonixKeyboardController : IRgbDevice, IDisposable
{
    private const int PacketSize = 65; // 1 byte report ID (0x00) + 64 bytes payload

    private readonly SafeFileHandle? _handle;
    private readonly ILogger? _logger;
    private readonly List<RgbZone> _zones = new();
    private readonly object _ioLock = new();
    private bool _isDisposed;

    public string Id { get; } = "sonix-gaming-keyboard";
    public string Name { get; } = "Mechanical Gaming Keyboard";
    public string Manufacturer { get; } = "Gaming Keyboard";
    public string Model { get; } = "RGB Mechanical Keyboard (Full 104-Key)";
    public string SerialNumber { get; } = "KB-RGB-01";
    public DeviceType Type => DeviceType.Keyboard;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Disconnected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
    public string? FirmwareVersion { get; private set; } = "v1.0";
    public string IntegrationProvider => "OpenRGB USB Keyboard Protocol";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;
    public double Brightness { get; set; } = 1.0;

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

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] lpReportBuffer,
        int reportBufferLength);

    public SonixKeyboardController(string devicePath, ILogger? logger = null)
    {
        _logger = logger;
        _zones.Add(new RgbZone("zone-keys", "Full Keyboard Backlight", 104));

        try
        {
            _handle = CreateFile(devicePath, 0xC0000000, 0x03, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle != null && !_handle.IsInvalid)
            {
                Status = DeviceConnectionStatus.Connected;
                _logger?.LogInformation("Mechanical Gaming Keyboard opened via USB HID at {Path}", devicePath);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                _logger?.LogDebug("Failed to open Keyboard handle. Win32 Error: {Err}", err);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception opening Keyboard handle.");
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        var span = colors.Span;
        var primary = span.Length > 0 ? span[0] : new RgbColor(255, 255, 255);
        return SetAllColorsAsync(primary, cancellationToken);
    }

    public Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return Task.CompletedTask;

        try
        {
            byte r = (byte)Math.Clamp((int)Math.Round(color.R * Brightness), 0, 255);
            byte g = (byte)Math.Clamp((int)Math.Round(color.G * Brightness), 0, 255);
            byte b = (byte)Math.Clamp((int)Math.Round(color.B * Brightness), 0, 255);

            lock (_ioLock)
            {
                // Sonix static color packet:
                // Report ID 0x00, Command 0x05, Sub 0x11, Mode 0x01 (Static), R, G, B
                byte[] pkt = new byte[PacketSize];
                pkt[0] = 0x00;
                pkt[1] = 0x05;
                pkt[2] = 0x11;
                pkt[3] = 0x01; // Static Mode
                pkt[4] = 0x04; // Brightness max
                pkt[5] = 0x01; // Speed
                pkt[6] = r;
                pkt[7] = g;
                pkt[8] = b;

                // Send via WriteFile or SetFeature
                bool wr = WriteFile(_handle, pkt, (uint)pkt.Length, out _, IntPtr.Zero);
                if (!wr)
                {
                    HidD_SetFeature(_handle, pkt, pkt.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to send color packet to Keyboard.");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _handle?.Dispose();
    }
}
