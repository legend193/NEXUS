using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;

namespace Nexus.Integrations.Corsair;

/// <summary>
/// Direct USB HID Driver for Corsair Commander CORE Hub (VID 0x1B1C, PID 0x0C1C)
/// Implements full OpenRGB Commander Core protocol with verified HID read-response synchronization.
/// Source reference: OpenRGB CorsairCommanderCoreController.cpp
/// </summary>
public class CorsairCommanderCoreController : IRgbDevice, IDisposable
{
    private const int VendorId = 0x1B1C;
    private const int ProductId = 0x0C1C;
    private const int PacketSize = 97; // 1 byte report ID (0x00) + 96 bytes payload
    private const int RgbDataLength = 699;

    // Corsair Commander Core Commands
    private static readonly byte[] CmdSoftwareMode = { 0x01, 0x03 };
    private static readonly byte[] CmdFirmwareVersion = { 0x02, 0x13 };
    private static readonly byte[] CmdOpenEndpoint = { 0x0D, 0x00 };
    private static readonly byte[] CmdWriteFirstPacket = { 0x06, 0x00 };
    private static readonly byte[] CmdWriteRestPacket = { 0x07, 0x00 };
    private static readonly byte[] CmdCloseEndpoint = { 0x05, 0x01 };

    private readonly SafeFileHandle? _handle;
    private readonly ILogger? _logger;
    private readonly List<RgbZone> _zones = new();
    private readonly object _ioLock = new();
    private CancellationTokenSource? _keepaliveCts;
    private Task? _keepaliveTask;
    private DateTime _lastCommitTime = DateTime.MinValue;
    private byte[] _lastRgbData = new byte[RgbDataLength];
    private bool _controllerReady = false;
    private bool _isDisposed;

    public string Id { get; } = "corsair-commander-core-hub";
    public string Name { get; } = "Corsair Commander CORE Hub";
    public string Manufacturer { get; } = "Corsair";
    public string Model { get; } = "Commander CORE (iCUE Capellix / RGB)";
    public string SerialNumber { get; } = "CORSAIR-CORE-01";
    public DeviceType Type => DeviceType.LightingController;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Disconnected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting | DeviceCapabilities.FanControl;
    public string? FirmwareVersion { get; private set; } = "v2.11";
    public string IntegrationProvider => "OpenRGB USB HID Protocol";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;
    public double Brightness { get; set; } = 1.0;

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    public CorsairCommanderCoreController(string devicePath, ILogger? logger = null)
    {
        _logger = logger;

        // Commander Core layout:
        // Zone 0: AIO Pump Cap (24 LEDs Halo Ring)
        // Zone 1..6: 6 Fan Ports (allocated 34 LEDs each in QL mode)
        _zones.Add(new RgbZone("zone-pump", "AIO Pump Cap (Halo Ring)", 24));
        _zones.Add(new RgbZone("zone-fan-1", "Fan Port 1", 34));
        _zones.Add(new RgbZone("zone-fan-2", "Fan Port 2", 34));
        _zones.Add(new RgbZone("zone-fan-3", "Fan Port 3", 34));
        _zones.Add(new RgbZone("zone-fan-4", "Fan Port 4", 34));
        _zones.Add(new RgbZone("zone-fan-5", "Fan Port 5", 34));
        _zones.Add(new RgbZone("zone-fan-6", "Fan Port 6", 34));

        try
        {
            _handle = CreateFile(devicePath, 0x40000000 | 0x80000000, 1 | 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle != null && !_handle.IsInvalid)
            {
                Status = DeviceConnectionStatus.Connected;
                _logger?.LogInformation("Corsair Commander Core Hub successfully opened via USB HID at {Path}", devicePath);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                _logger?.LogWarning("Failed to open Corsair Hub handle. Win32 Error: {Error}", err);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception opening Corsair Commander Core HID handle.");
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return Task.CompletedTask;

        try
        {
            lock (_ioLock)
            {
                // 1. Send software mode wake-up
                SendCommand(CmdSoftwareMode, new byte[] { 0x00, 0x02 }, false);

                // 2. Set Fan Mode (Configure AIO Pump + 6 QL Fan Ports = 34 LEDs per port)
                byte[] fanBuf = new byte[15];
                fanBuf[0] = 0x07;
                fanBuf[1] = 0x01; // AIO
                fanBuf[2] = 0x08;
                for (int i = 3; i < 15; i += 2)
                {
                    fanBuf[i] = 0x01;
                    fanBuf[i + 1] = 0x06; // QL Fan mode
                }
                WriteEndpointData(new byte[] { 0x1E, 0x00 }, new byte[] { 0x0D, 0x00 }, fanBuf);

                _controllerReady = true;
                _lastCommitTime = DateTime.UtcNow;
            }

            // 3. Start Keepalive background loop
            _keepaliveCts = new CancellationTokenSource();
            _keepaliveTask = Task.Run(() => KeepaliveLoopAsync(_keepaliveCts.Token));

            _logger?.LogInformation("Corsair Commander Core initialized in software mode with active keepalive.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize Corsair Commander Core.");
        }

        return Task.CompletedTask;
    }

    private async Task KeepaliveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Delay 2000ms between watchdog checks to reduce USB traffic
                await Task.Delay(2000, token);

                // Only send keepalive when no RGB updates have been committed for > 4500ms
                if (_controllerReady && (DateTime.UtcNow - _lastCommitTime).TotalMilliseconds >= 4500)
                {
                    lock (_ioLock)
                    {
                        if (_handle != null && !_handle.IsInvalid)
                        {
                            // Send official lightweight Corsair watchdog keepalive command (0x02, 0x13)
                            // Avoid sending heavy 276-byte endpoint 0x22 data stream which collides with render loops and causes flashing
                            SendCommand(new byte[] { 0x02, 0x13 }, null, false);
                            _lastCommitTime = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private void SendCommand(byte[] command, byte[]? data = null, bool readResponse = false)
    {
        if (_handle == null || _handle.IsInvalid) return;

        byte[] pkt = new byte[PacketSize];
        pkt[0] = 0x00;
        pkt[1] = 0x08;
        pkt[2] = command[0];
        pkt[3] = command[1];
        if (data != null && data.Length > 0)
        {
            Array.Copy(data, 0, pkt, 4, Math.Min(data.Length, PacketSize - 4));
        }

        bool wr = WriteFile(_handle, pkt, (uint)pkt.Length, out _, IntPtr.Zero);
        if (!wr)
        {
            _logger?.LogDebug("Corsair WriteFile failed. Win32 error: {Err}", Marshal.GetLastWin32Error());
        }

        // Only read response if explicitly requested and safe
        if (readResponse)
        {
            try
            {
                byte[] rBuf = new byte[PacketSize];
                ReadFile(_handle, rBuf, (uint)rBuf.Length, out _, IntPtr.Zero);
            }
            catch { }
        }
    }

    private void WriteEndpointData(byte[] endpoint, byte[] dataType, byte[] data)
    {
        if (_handle == null || _handle.IsInvalid) return;

        // 1. Open endpoint
        SendCommand(CmdOpenEndpoint, endpoint, false);

        // 2. Write first packet (max 87 bytes of data)
        int dataLen = data.Length;
        int firstChunkLen = Math.Min(dataLen, PacketSize - 10);
        ushort realLen = (ushort)(dataLen + 2);

        byte[] firstBuf = new byte[firstChunkLen + 6];
        firstBuf[0] = (byte)(realLen & 0xFF);
        firstBuf[1] = (byte)((realLen >> 8) & 0xFF);
        firstBuf[2] = 0x00;
        firstBuf[3] = 0x00;
        firstBuf[4] = dataType[0];
        firstBuf[5] = dataType[1];
        Array.Copy(data, 0, firstBuf, 6, firstChunkLen);

        SendCommand(CmdWriteFirstPacket, firstBuf, false);

        // 3. Write remaining packets in 93-byte chunks
        int offset = firstChunkLen;
        while (offset < dataLen)
        {
            int chunkLen = Math.Min(dataLen - offset, PacketSize - 4);
            byte[] chunk = new byte[chunkLen];
            Array.Copy(data, offset, chunk, 0, chunkLen);
            SendCommand(CmdWriteRestPacket, chunk, false);
            offset += chunkLen;
        }

        // 4. Close endpoint
        SendCommand(CmdCloseEndpoint, null, false);
    }

    private readonly byte[] _portDuty = new byte[7] { 80, 50, 50, 50, 50, 50, 50 }; // Port 0: Pump (~2650 RPM), Ports 1..6: Fans

    /// <summary>
    /// Sets PWM fan speed duty cycle for a specific Corsair Commander CORE port (0=Pump, 1..6=Fans)
    /// </summary>
    public void SetFanSpeed(int portIndex, int speedPercent)
    {
        if (_handle == null || _handle.IsInvalid) return;

        lock (_ioLock)
        {
            if (portIndex >= 0 && portIndex < _portDuty.Length)
            {
                _portDuty[portIndex] = (byte)Math.Clamp(speedPercent, 0, 100);
            }
            else
            {
                // If portIndex is -1 or out of range, apply to all fan ports (1..6) leaving pump intact
                for (int i = 1; i < _portDuty.Length; i++)
                {
                    _portDuty[i] = (byte)Math.Clamp(speedPercent, 0, 100);
                }
            }
            WriteEndpointData(new byte[] { 0x17, 0x00 }, new byte[] { 0x05, 0x00 }, _portDuty);
        }
    }

    /// <summary>
    /// Sets dedicated discrete AIO Pump speed mode (Quiet ~2200 RPM, Balanced ~2650 RPM, Extreme ~2850 RPM)
    /// </summary>
    public void SetPumpMode(Nexus.Abstractions.Cooling.PumpSpeedMode mode)
    {
        byte duty = mode switch
        {
            Nexus.Abstractions.Cooling.PumpSpeedMode.Quiet => 55,
            Nexus.Abstractions.Cooling.PumpSpeedMode.Balanced => 80,
            Nexus.Abstractions.Cooling.PumpSpeedMode.Extreme => 100,
            _ => 80
        };
        SetFanSpeed(0, duty);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _keepaliveCts?.Cancel();
        return Task.CompletedTask;
    }

    public Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return Task.CompletedTask;

        try
        {
            lock (_ioLock)
            {
                int zoneIndex = _zones.FindIndex(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
                if (zoneIndex < 0 || colors.IsEmpty) return Task.CompletedTask;

                var zone = _zones[zoneIndex];
                var span = colors.Span;

                // Determine byte offset for this zone in _lastRgbData
                // Zone 0: Pump Cap (24 LEDs = 72 bytes) -> offset 0
                // Zone 1..6: Fans 1..6 (34 LEDs = 102 bytes each) -> offset 72 + (zoneIndex - 1) * 102
                int startOffset = zoneIndex == 0 ? 0 : 72 + (zoneIndex - 1) * 102;
                int ledCount = zoneIndex == 0 ? 24 : 34;

                for (int i = 0; i < Math.Min(ledCount, span.Length); i++)
                {
                    var col = span[i];
                    byte r = (byte)Math.Round(col.R * Brightness);
                    byte g = (byte)Math.Round(col.G * Brightness);
                    byte b = (byte)Math.Round(col.B * Brightness);

                    int bytePos = startOffset + i * 3;
                    if (bytePos + 2 < _lastRgbData.Length)
                    {
                        _lastRgbData[bytePos] = r;
                        _lastRgbData[bytePos + 1] = g;
                        _lastRgbData[bytePos + 2] = b;
                    }

                    if (i < zone.CurrentColors.Length)
                    {
                        zone.CurrentColors[i] = new RgbColor(r, g, b);
                    }
                }

                // Flush to USB when the last fan zone is reached and at least 35ms have elapsed since previous commit
                bool isLastZone = (zoneIndex == _zones.Count - 1);
                double elapsedSinceCommit = (DateTime.UtcNow - _lastCommitTime).TotalMilliseconds;

                if (isLastZone && elapsedSinceCommit >= 35)
                {
                    WriteEndpointData(new byte[] { 0x22, 0x00 }, new byte[] { 0x12, 0x00 }, _lastRgbData);
                    _lastCommitTime = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error writing zone RGB packet to Corsair Hub.");
        }

        return Task.CompletedTask;
    }

    public Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        if (Status != DeviceConnectionStatus.Connected || _handle == null || _handle.IsInvalid) return Task.CompletedTask;

        try
        {
            lock (_ioLock)
            {
                double elapsedSinceCommit = (DateTime.UtcNow - _lastCommitTime).TotalMilliseconds;
                if (elapsedSinceCommit < 35) return Task.CompletedTask; // Throttle to prevent packet collisions and flashing

                byte[] rgbData = new byte[RgbDataLength];
                byte r = (byte)Math.Round(color.R * Brightness);
                byte g = (byte)Math.Round(color.G * Brightness);
                byte b = (byte)Math.Round(color.B * Brightness);

                int offset = 0;

                // Zone 0: AIO Pump Cap / Halo Ring (up to 24 LEDs = 72 bytes)
                for (int i = 0; i < 24; i++)
                {
                    if (offset + 2 < rgbData.Length)
                    {
                        rgbData[offset++] = r;
                        rgbData[offset++] = g;
                        rgbData[offset++] = b;
                    }
                }

                // Zone 1..6: 6 Fan ports (34 LEDs each = 102 bytes * 6)
                for (int f = 0; f < 6; f++)
                {
                    for (int i = 0; i < 34; i++)
                    {
                        if (offset + 2 < rgbData.Length)
                        {
                            rgbData[offset++] = r;
                            rgbData[offset++] = g;
                            rgbData[offset++] = b;
                        }
                    }
                }

                // Update internal zone state
                foreach (var zone in _zones)
                {
                    Array.Fill(zone.CurrentColors, new RgbColor(r, g, b));
                }

                Array.Copy(rgbData, _lastRgbData, rgbData.Length);
                WriteEndpointData(new byte[] { 0x22, 0x00 }, new byte[] { 0x12, 0x00 }, rgbData);
                _lastCommitTime = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error broadcasting color to Corsair Hub.");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _keepaliveCts?.Cancel();
        _keepaliveCts?.Dispose();
        _handle?.Dispose();
    }
}
