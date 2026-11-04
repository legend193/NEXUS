using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;

namespace Nexus.Integrations.LianLi;

/// <summary>
/// Direct USB HID Driver for Lian Li UNI FAN AL Hub (VID 0x0CF2, PID 0xA101)
/// Implements the exact 3-step OpenRGB protocol: SendStartAction, SendColorData, SendCommitAction
/// Source reference: OpenRGB LianLiUniHubALController.cpp
/// </summary>
public class LianLiUniFanController : IRgbDevice, IDisposable
{
    private const int VendorId = 0x0CF2;
    private const int ProductId = 0xA101; // Lian Li UNI FAN AL Hub
    private const byte TransactionId = 0xE0; // UNIHUB_AL_TRANSACTION_ID

    // Windows USB HID Output Report size must match OutputReportByteLength exactly (353 bytes)
    private const int UsbReportSize = 353;

    // OpenRGB Hardware Lighting Mode Constants
    public const byte ModeStaticColor = 0x01;
    public const byte ModeBreathing = 0x02;
    public const byte ModeRainbow = 0x28;
    public const byte ModeColorCycle = 0x2B;
    public const byte ModeRunway = 0x1A;
    public const byte ModeMeteor = 0x19;
    public const byte ModeTaichi = 0x2C;
    public const byte ModeTornado = 0x36;

    // OpenRGB Speed Constants
    public const byte SpeedVerySlow = 0x02;
    public const byte SpeedSlow = 0x01;
    public const byte SpeedMedium = 0x00;
    public const byte SpeedFast = 0xFF;
    public const byte SpeedVeryFast = 0xFE;

    // OpenRGB Brightness Codes
    public const byte Brightness100 = 0x00;
    public const byte Brightness75 = 0x01;
    public const byte Brightness50 = 0x02;
    public const byte Brightness25 = 0x03;
    public const byte BrightnessOff = 0x08;

    private readonly SafeFileHandle? _handle;
    private readonly ILogger? _logger;
    private readonly List<RgbZone> _zones = new();
    private readonly object _ioLock = new();
    private bool _isDisposed;

    // OpenRGB SDK Bridge connection
    private Nexus.Integrations.OpenRGB.OpenRgbClient? _openRgbClient;
    private int _openRgbDeviceIndex = -1;
    private int _openRgbLedCount = 32;
    private readonly RgbColor[] _openRgbBuffer = new RgbColor[32];

    public string Id { get; } = "lianli-unifan-al-hub";
    public string Name { get; } = "Lian Li UNI FAN AL Hub";
    public string Manufacturer { get; } = "Lian Li";
    public string Model { get; } = "UNI FAN AL Controller v1.7 (Dual USB HID / OpenRGB Protocol)";
    public string SerialNumber { get; } = "LL-AL-01";
    public DeviceType Type => DeviceType.LightingController;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Disconnected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting | DeviceCapabilities.FanControl;
    public string? FirmwareVersion => "v1.7";
    public string IntegrationProvider => "OpenRGB USB HID Protocol";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;
    public double Brightness { get; set; } = 1.0;

    public void AttachOpenRgbClient(Nexus.Integrations.OpenRGB.OpenRgbClient client, int deviceIndex, int ledCount)
    {
        _openRgbClient = client;
        _openRgbDeviceIndex = deviceIndex;
        _openRgbLedCount = ledCount > 0 ? ledCount : 32;
        Status = DeviceConnectionStatus.Connected;
        _logger?.LogInformation("Lian Li UNI FAN Hub successfully attached OpenRGB Client [{Index}] ({Leds} LEDs)", deviceIndex, _openRgbLedCount);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
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

    public LianLiUniFanController(string devicePath, ILogger? logger = null)
    {
        _logger = logger;

        // 4 Fan Ports on the Lian Li AL Hub (each cluster supports up to 4 fans * 20 LEDs = 80 LEDs per cluster)
        _zones.Add(new RgbZone("port-1", "Port 1 (Fan Cluster 1)", 80));
        _zones.Add(new RgbZone("port-2", "Port 2 (Fan Cluster 2)", 80));
        _zones.Add(new RgbZone("port-3", "Port 3 (Fan Cluster 3)", 80));
        _zones.Add(new RgbZone("port-4", "Port 4 (Fan Cluster 4)", 80));

        try
        {
            _handle = CreateFile(devicePath, 0x40000000 | 0x80000000, 1 | 2 | 4, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle == null || _handle.IsInvalid)
            {
                // Fallback to write-only mode
                _handle = CreateFile(devicePath, 0x40000000, 1 | 2 | 4, IntPtr.Zero, 3, 0, IntPtr.Zero);
            }

            if (_handle != null && !_handle.IsInvalid)
            {
                Status = DeviceConnectionStatus.Connected;
                _logger?.LogInformation("Lian Li UNI FAN Hub successfully opened via USB HID at {Path}", devicePath);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                _logger?.LogWarning("Failed to open Lian Li Hub handle via USB HID (Win32 {Error}). OpenRGB bridge will serve as primary.", err);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception opening Lian Li Hub HID handle.");
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return;

        try
        {
            // Initialize all 4 channels with OpenRGB sequence to take software LED control
            for (byte ch = 0; ch < 4; ch++)
            {
                await SendStartActionAsync(ch, 4, cancellationToken);
                await SendCommitActionAsync(ch, 0, ModeRainbow, SpeedMedium, 0, Brightness100, cancellationToken);
                await SendCommitActionAsync(ch, 1, ModeRainbow, SpeedMedium, 0, Brightness100, cancellationToken);
            }
            _logger?.LogInformation("Lian Li AL Hub initialized with OpenRGB protocol across 4 channels.");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error during Lian Li AL Hub initialization.");
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Step 1 of OpenRGB Lian Li Protocol: Send Start Action
    /// </summary>
    private Task SendStartActionAsync(byte channel, int numFans, CancellationToken token = default)
    {
        if (_handle == null || _handle.IsInvalid) return Task.CompletedTask;

        lock (_ioLock)
        {
            byte[] packet = new byte[UsbReportSize];
            packet[0] = TransactionId; // 0xE0
            packet[1] = 0x10;
            packet[2] = 0x40;
            packet[3] = (byte)(channel + 1); // Channel 1-4
            packet[4] = (byte)Math.Clamp(numFans, 1, 4);

            WriteFile(_handle, packet, (uint)packet.Length, out _, IntPtr.Zero);
        }

        return Task.Delay(2, token);
    }

    /// <summary>
    /// Step 2 of OpenRGB Lian Li Protocol: Send Color Data (R, B, G order!)
    /// </summary>
    private Task SendColorDataAsync(byte channel, int fanOrEdge, ReadOnlySpan<byte> ledData, CancellationToken token = default)
    {
        if (_handle == null || _handle.IsInvalid) return Task.CompletedTask;

        lock (_ioLock)
        {
            byte[] packet = new byte[UsbReportSize];
            packet[0] = TransactionId; // 0xE0
            // Channel + device offset: 0x30 = Ch1 Fan, 0x31 = Ch1 Edge, 0x32 = Ch2 Fan, 0x33 = Ch2 Edge...
            packet[1] = (byte)(0x30 + fanOrEdge + (channel * 2));

            int copyLen = Math.Min(ledData.Length, packet.Length - 2);
            ledData.Slice(0, copyLen).CopyTo(packet.AsSpan(2));

            WriteFile(_handle, packet, (uint)packet.Length, out _, IntPtr.Zero);
        }

        return Task.Delay(2, token);
    }

    /// <summary>
    /// Set PWM fan speed duty cycle for one of the 4 fan clusters (Channel 0..3)
    /// </summary>
    public void SetChannelFanSpeed(int channel, int speedPercent)
    {
        if (_handle == null || _handle.IsInvalid) return;

        lock (_ioLock)
        {
            byte[] packet = new byte[UsbReportSize];
            packet[0] = TransactionId; // 0xE0
            packet[1] = (byte)(0x20 + Math.Clamp(channel, 0, 3));
            packet[2] = 0x00; // PWM Duty mode
            packet[3] = (byte)Math.Clamp(speedPercent, 20, 100);

            WriteFile(_handle, packet, (uint)packet.Length, out _, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Step 3 of OpenRGB Lian Li Protocol: Send Commit Action
    /// </summary>
    private Task SendCommitActionAsync(byte channel, int fanOrEdge, byte effect, byte speed, byte direction, byte brightness, CancellationToken token = default)
    {
        if (_handle == null || _handle.IsInvalid) return Task.CompletedTask;

        lock (_ioLock)
        {
            byte[] packet = new byte[UsbReportSize];
            packet[0] = TransactionId; // 0xE0
            // Channel + device offset: 0x10 = Ch1 Fan, 0x11 = Ch1 Edge, 0x12 = Ch2 Fan...
            packet[1] = (byte)(0x10 + fanOrEdge + (channel * 2));
            packet[2] = effect;
            packet[3] = speed;
            packet[4] = direction;
            packet[5] = brightness;

            WriteFile(_handle, packet, (uint)packet.Length, out _, IntPtr.Zero);
        }

        return Task.Delay(2, token);
    }

    public async Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        byte channel = 0;
        if (zoneId.Contains("2") || zoneId.Contains("bottom")) channel = 1;
        else if (zoneId.Contains("3") || zoneId.Contains("rear")) channel = 2;
        else if (zoneId.Contains("4")) channel = 3;

        if (colors.IsEmpty) return;

        // 1. Dispatch via OpenRGB Bridge if linked
        if (_openRgbClient != null && _openRgbDeviceIndex >= 0)
        {
            try
            {
                int startLed = channel * 8;
                var memorySpan = colors.Span;
                for (int i = 0; i < 8 && i < memorySpan.Length; i++)
                {
                    if (startLed + i < _openRgbBuffer.Length)
                    {
                        var c = memorySpan[i];
                        _openRgbBuffer[startLed + i] = new RgbColor(
                            (byte)Math.Round(c.R * Brightness),
                            (byte)Math.Round(c.G * Brightness),
                            (byte)Math.Round(c.B * Brightness)
                        );
                    }
                }
                await _openRgbClient.UpdateLedsAsync(_openRgbDeviceIndex, _openRgbBuffer, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "OpenRGB client write zone failed for Lian Li Fans.");
            }
        }

        // 2. Dispatch via Direct USB HID if handle is connected
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return;

        // Build OpenRGB Fan (8 LEDs * 4 fans * 3 = 96 bytes) and Edge (12 LEDs * 4 fans * 3 = 144 bytes) arrays
        byte[] fanLedData = new byte[96];
        byte[] edgeLedData = new byte[144];

        {
            var memorySpan = colors.Span;
            // Ensure all 4 daisy-chained fans in each cluster receive colors
            for (int fan = 0; fan < 4; fan++)
            {
                for (int led = 0; led < 8; led++)
                {
                    int srcIdx = (fan * 20 + led) % memorySpan.Length;
                    var c = memorySpan[srcIdx];
                    byte r = (byte)Math.Round(c.R * Brightness);
                    byte g = (byte)Math.Round(c.G * Brightness);
                    byte b = (byte)Math.Round(c.B * Brightness);
                    if (r > 153 && r == g && r == b) { r = 153; g = 153; b = 153; }

                    int curIdx = (led + (fan * 8)) * 3;
                    fanLedData[curIdx + 0] = r;
                    fanLedData[curIdx + 1] = b; // R, B, G byte order in Lian Li AL!
                    fanLedData[curIdx + 2] = g;
                }

                for (int led = 0; led < 12; led++)
                {
                    int srcIdx = (fan * 20 + 8 + led) % memorySpan.Length;
                    var c = memorySpan[srcIdx];
                    byte r = (byte)Math.Round(c.R * Brightness);
                    byte g = (byte)Math.Round(c.G * Brightness);
                    byte b = (byte)Math.Round(c.B * Brightness);
                    if (r > 153 && r == g && r == b) { r = 153; g = 153; b = 153; }

                    int curIdx = (led + (fan * 12)) * 3;
                    edgeLedData[curIdx + 0] = r;
                    edgeLedData[curIdx + 1] = b; // R, B, G byte order!
                    edgeLedData[curIdx + 2] = g;
                }
            }
        }

        try
        {
            // 1. Send Fan Center LEDs (drive all 4 daisy-chained fans in cluster)
            await SendStartActionAsync(channel, 4, cancellationToken);
            await SendColorDataAsync(channel, 0, fanLedData, cancellationToken);
            await SendCommitActionAsync(channel, 0, ModeStaticColor, SpeedMedium, 0, Brightness100, cancellationToken);

            // 2. Send Frame Edge LEDs
            await SendStartActionAsync(channel, 4, cancellationToken);
            await SendColorDataAsync(channel, 1, edgeLedData, cancellationToken);
            await SendCommitActionAsync(channel, 1, ModeStaticColor, SpeedMedium, 0, Brightness100, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error writing OpenRGB packet sequence to Lian Li Hub.");
        }
    }

    public async Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        byte r = (byte)Math.Round(color.R * Brightness);
        byte g = (byte)Math.Round(color.G * Brightness);
        byte b = (byte)Math.Round(color.B * Brightness);
        if (r > 153 && r == g && r == b) { r = 153; g = 153; b = 153; }

        var targetColor = new RgbColor(r, g, b);

        // 1. Dispatch via OpenRGB Bridge if linked
        if (_openRgbClient != null && _openRgbDeviceIndex >= 0)
        {
            try
            {
                Array.Fill(_openRgbBuffer, targetColor);
                await _openRgbClient.UpdateLedsAsync(_openRgbDeviceIndex, _openRgbBuffer, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "OpenRGB client write all failed for Lian Li Fans.");
            }
        }

        // 2. Dispatch via Direct USB HID if handle is connected
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return;

        byte[] fanData = new byte[96];
        byte[] edgeData = new byte[144];

        for (int i = 0; i < fanData.Length; i += 3)
        {
            fanData[i + 0] = r;
            fanData[i + 1] = b; // R, B, G
            fanData[i + 2] = g;
        }

        for (int i = 0; i < edgeData.Length; i += 3)
        {
            edgeData[i + 0] = r;
            edgeData[i + 1] = b; // R, B, G
            edgeData[i + 2] = g;
        }

        // Broadcast to all 4 channels simultaneously
        for (byte ch = 0; ch < 4; ch++)
        {
            await SendStartActionAsync(ch, 4, cancellationToken);
            await SendColorDataAsync(ch, 0, fanData, cancellationToken);
            await SendCommitActionAsync(ch, 0, ModeStaticColor, SpeedMedium, 0, Brightness100, cancellationToken);

            await SendStartActionAsync(ch, 4, cancellationToken);
            await SendColorDataAsync(ch, 1, edgeData, cancellationToken);
            await SendCommitActionAsync(ch, 1, ModeStaticColor, SpeedMedium, 0, Brightness100, cancellationToken);
        }
    }

    public async Task SetHardwareEffectAsync(byte channel, byte effect, byte speed = SpeedMedium, CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return;

        // Sets native hardware effect directly inside the AL microcontroller
        for (byte ch = 0; ch < 4; ch++)
        {
            if (channel != 0xFF && ch != channel) continue;

            await SendStartActionAsync(ch, 4, cancellationToken);
            await SendCommitActionAsync(ch, 0, effect, speed, 0, Brightness100, cancellationToken);
            await SendCommitActionAsync(ch, 1, effect, speed, 0, Brightness100, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _handle?.Dispose();
    }
}
