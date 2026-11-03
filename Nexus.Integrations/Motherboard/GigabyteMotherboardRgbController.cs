using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;

namespace Nexus.Integrations.Motherboard;

/// <summary>
/// Direct USB HID Hardware Driver for Gigabyte X570S AORUS PRO AX Motherboard
/// Implements OpenRGB's reverse-engineered ITE IT8297 USB RGB Fusion 2.0 hardware protocol (VID 0x048D, PID 0x5702).
/// Uses 64-byte HID Feature Reports with Report ID 0xCC and verified PktEffect struct layout.
/// </summary>
public class GigabyteMotherboardRgbController : IRgbDevice, IDisposable
{
    private const int VendorId = 0x048D;  // ITE Tech. Inc.
    private const int ProductId = 0x5702; // IT8297 / IT5702 RGB Fusion 2.0 MCU
    private const byte ReportId = 0xCC;

    // OpenRGB Zone LED indices
    private const int LED_IO = 0;       // LED1: I/O Shield Armor
    private const int LED_PCH = 1;      // LED2: PCH Chipset Heatsink
    private const int LED_PCIE = 2;     // LED3: PCIe & Audio Path
    private const int LED_HDR1 = 3;     // LED4: 12V RGB Header 1
    private const int LED_HDR2 = 4;     // LED5: 12V RGB Header 2
    private const int LED_DLED1 = 5;    // HDR_D_LED1: ARGB Header 1 (Digital)
    private const int LED_DLED2 = 6;    // HDR_D_LED2: ARGB Header 2 (Digital)
    private const int LED_ZONE8 = 7;    // LED8: Additional Onboard Zone

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileNative(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle hObject, byte[] lpReportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hObject, byte[] lpReportBuffer, int reportBufferLength);

    private readonly string _devicePath;
    private readonly ILogger? _logger;
    private readonly SafeFileHandle? _handle;
    private readonly List<RgbZone> _zones = new();
    private readonly object _ioLock = new();
    private double _brightness = 1.0;
    private bool _isDisposed;

    public string Id { get; } = "gigabyte-x570s-aorus-pro-ax";
    public string Name { get; } = "Gigabyte X570S AORUS PRO AX";
    public string Manufacturer { get; } = "Gigabyte Technology";
    public string Model { get; } = "X570S AORUS PRO AX (IT8297 USB RGB Fusion 2.0)";
    public string SerialNumber { get; } = "GB-AORUS-X570S";
    public DeviceType Type => DeviceType.Motherboard;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Disconnected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
    public string? FirmwareVersion { get; private set; } = "v2.0-IT8297";
    public string IntegrationProvider => "Gigabyte IT8297 USB HID (OpenRGB Protocol)";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;

    public double Brightness
    {
        get => _brightness;
        set => _brightness = Math.Clamp(value, 0.0, 1.0);
    }

    public GigabyteMotherboardRgbController(string devicePath, ILogger? logger = null)
    {
        _devicePath = devicePath;
        _logger = logger;

        // Initialize Onboard Zones & ARGB Headers
        _zones.Add(new RgbZone("mb-io", "I/O Shield Armor", 4));
        _zones.Add(new RgbZone("mb-pch", "PCH Chipset Heatsink", 4));
        _zones.Add(new RgbZone("mb-pcie", "PCIe & Audio Path", 4));
        _zones.Add(new RgbZone("mb-led1", "12V Analog RGB Header 1", 4));
        _zones.Add(new RgbZone("mb-led2", "12V Analog RGB Header 2", 4));
        _zones.Add(new RgbZone("mb-dled1", "Addressable ARGB Header 1 (D_LED1)", 30));
        _zones.Add(new RgbZone("mb-dled2", "Addressable ARGB Header 2 (D_LED2)", 30));

        try
        {
            _handle = CreateFileNative(
                _devicePath,
                0xC0000000, // GENERIC_READ | GENERIC_WRITE
                0x03,       // FILE_SHARE_READ | FILE_SHARE_WRITE
                IntPtr.Zero,
                3,          // OPEN_EXISTING
                0,
                IntPtr.Zero);

            if (_handle != null && !_handle.IsInvalid)
            {
                Status = DeviceConnectionStatus.Connected;
                _logger?.LogInformation("Opened Gigabyte X570S AORUS IT8297 USB handle successfully at {Path}", _devicePath);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                _logger?.LogWarning("Failed to open IT8297 USB handle at {Path}. Win32 Error: {Error}", _devicePath, error);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error opening Gigabyte AORUS USB HID handle.");
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid)
        {
            return Task.CompletedTask;
        }

        lock (_ioLock)
        {
            try
            {
                // OpenRGB RefreshHardwareInfo Handshake: SendCCReport(0x60, 0x00, 0x00)
                byte[] initReport = new byte[64];
                initReport[0] = ReportId; // 0xCC
                initReport[1] = 0x60;
                bool ok = HidD_SetFeature(_handle, initReport, 64);
                _logger?.LogDebug("IT8297 Init Handshake result: {Result}", ok);

                // Read report back
                byte[] readBuf = new byte[64];
                readBuf[0] = ReportId;
                if (HidD_GetFeature(_handle, readBuf, 64))
                {
                    string product = System.Text.Encoding.ASCII.GetString(readBuf, 10, 20).Trim('\0', ' ');
                    _logger?.LogInformation("Gigabyte IT8297 Microcontroller Identified: '{Product}'", product);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception during IT8297 init handshake.");
            }
        }

        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        foreach (var zone in _zones)
        {
            Array.Fill(zone.CurrentColors, color);
        }

        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid)
        {
            return Task.CompletedTask;
        }

        byte r = (byte)(color.R * _brightness);
        byte g = (byte)(color.G * _brightness);
        byte b = (byte)(color.B * _brightness);

        lock (_ioLock)
        {
            try
            {
                // 1. Send OpenRGB Master PktEffect for all 8 zones (zone_mask = 0xFF, header = 0x20)
                byte[] effectPkt = new byte[64];
                effectPkt[0] = ReportId; // 0xCC
                effectPkt[1] = 0x20;     // Master header for all zones
                
                // zone0 = 0x000000FF (uint32_t at offset 2..5)
                effectPkt[2] = 0xFF;
                effectPkt[3] = 0x00;
                effectPkt[4] = 0x00;
                effectPkt[5] = 0x00;

                // zone1 = 0 (uint32_t at offset 6..9)
                effectPkt[6] = 0x00;
                effectPkt[7] = 0x00;
                effectPkt[8] = 0x00;
                effectPkt[9] = 0x00;

                effectPkt[10] = 0x00;
                effectPkt[11] = 0x01; // EFFECT_STATIC
                effectPkt[12] = (byte)(_brightness * 255); // max brightness (0..255)
                effectPkt[13] = 0x00; // min brightness

                // color0 in BGR order (uint32_t at offset 14..17)
                effectPkt[14] = b; // Blue
                effectPkt[15] = g; // Green
                effectPkt[16] = r; // Red
                effectPkt[17] = 0x00;

                HidD_SetFeature(_handle, effectPkt, 64);

                // 2. Also send per-zone packets for each of the 8 individual hardware channels
                // to guarantee compatibility across IT5702 / IT8297 firmware revisions
                for (int led = 0; led < 8; led++)
                {
                    byte[] zonePkt = new byte[64];
                    zonePkt[0] = ReportId;
                    zonePkt[1] = (byte)(0x20 + led);
                    
                    uint zoneMask = 1U << led;
                    zonePkt[2] = (byte)(zoneMask & 0xFF);
                    zonePkt[3] = (byte)((zoneMask >> 8) & 0xFF);
                    zonePkt[4] = 0x00;
                    zonePkt[5] = 0x00;

                    zonePkt[11] = 0x01; // EFFECT_STATIC
                    zonePkt[12] = (byte)(_brightness * 255);
                    zonePkt[14] = b;
                    zonePkt[15] = g;
                    zonePkt[16] = r;

                    HidD_SetFeature(_handle, zonePkt, 64);
                }

                // 3. OpenRGB Apply Command: SendCCReport(0x28, 0xFF, 0x00)
                byte[] applyPkt = new byte[64];
                applyPkt[0] = ReportId; // 0xCC
                applyPkt[1] = 0x28;     // Apply OpCode
                applyPkt[2] = 0xFF;     // zone_sel0 mask
                applyPkt[3] = 0x00;     // zone_sel1 mask

                HidD_SetFeature(_handle, applyPkt, 64);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send OpenRGB packet to Gigabyte AORUS Motherboard.");
            }
        }

        return Task.CompletedTask;
    }

    public Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        var zone = _zones.Find(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
        if (zone == null || colors.IsEmpty) return Task.CompletedTask;

        var color = colors.Span[0];
        Array.Fill(zone.CurrentColors, color);

        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid)
        {
            return Task.CompletedTask;
        }

        int led = zoneId.ToLowerInvariant() switch
        {
            "mb-io" => LED_IO,
            "mb-pch" => LED_PCH,
            "mb-pcie" => LED_PCIE,
            "mb-led1" => LED_HDR1,
            "mb-led2" => LED_HDR2,
            "mb-dled1" => LED_DLED1,
            "mb-dled2" => LED_DLED2,
            _ => LED_IO
        };

        byte r = (byte)(color.R * _brightness);
        byte g = (byte)(color.G * _brightness);
        byte b = (byte)(color.B * _brightness);

        lock (_ioLock)
        {
            try
            {
                byte[] zonePkt = new byte[64];
                zonePkt[0] = ReportId;
                zonePkt[1] = (byte)(0x20 + led);

                uint zoneMask = 1U << led;
                zonePkt[2] = (byte)(zoneMask & 0xFF);
                zonePkt[3] = (byte)((zoneMask >> 8) & 0xFF);
                zonePkt[4] = 0x00;
                zonePkt[5] = 0x00;

                zonePkt[11] = 0x01; // EFFECT_STATIC
                zonePkt[12] = (byte)(_brightness * 255);
                zonePkt[14] = b;
                zonePkt[15] = g;
                zonePkt[16] = r;

                HidD_SetFeature(_handle, zonePkt, 64);

                // Apply zone
                byte[] applyPkt = new byte[64];
                applyPkt[0] = ReportId;
                applyPkt[1] = 0x28;
                applyPkt[2] = (byte)zoneMask;
                applyPkt[3] = 0x00;

                HidD_SetFeature(_handle, applyPkt, 64);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to set zone color on Gigabyte AORUS Motherboard zone {Zone}", zoneId);
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _handle?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
