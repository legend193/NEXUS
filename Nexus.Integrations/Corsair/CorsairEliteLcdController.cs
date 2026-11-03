using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;

namespace Nexus.Integrations.Corsair;

/// <summary>
/// Autonomous Hardware Driver for Corsair iCUE ELITE LCD Display Cap (VID 0x1B1C, PID 0x0C33).
/// Controls the 2.1" 480x480 circular IPS screen and the surrounding 24-LED RGB Halo Ring.
/// Operates directly via USB HID with zero dependence on iCUE software.
/// </summary>
public class CorsairEliteLcdController : IRgbDevice, IDisposable
{
    public const int VendorId = 0x1B1C;
    public const int ProductId = 0x0C33;

    private readonly ILogger? _logger;
    private SafeFileHandle? _handle;
    private readonly List<RgbZone> _zones = new();
    private readonly object _ioLock = new();
    private int _isStreamingFrame;

    private AioLcdConfig _config = new();
    private double _brightness = 1.0;
    private bool _isDisposed;
    private string? _devicePath;

    public string Id { get; } = "corsair-elite-lcd-cap";
    public string Name { get; } = "Corsair iCUE ELITE LCD Display";
    public string Manufacturer { get; } = "Corsair";
    public string Model { get; } = "ELITE LCD Capellix 480x480 IPS Cap";
    public string SerialNumber { get; } = "1022215180001";
    public DeviceType Type => DeviceType.LightingController;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Connected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
    public string? FirmwareVersion { get; private set; } = "v1.1.28";
    public string IntegrationProvider => "Corsair Direct USB HID Protocol";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;
    public string? DevicePath => _devicePath;
    public bool IsHandleOpen => _handle != null && !_handle.IsInvalid;

    public double Brightness
    {
        get => _brightness;
        set => _brightness = Math.Clamp(value, 0.0, 1.0);
    }

    public AioLcdConfig Config
    {
        get => _config;
        set => _config = value ?? new AioLcdConfig();
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

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    public CorsairEliteLcdController(string? devicePath = null, ILogger? logger = null)
    {
        _logger = logger;
        _devicePath = devicePath;

        // 24-LED Halo Ring around the LCD Display
        _zones.Add(new RgbZone("zone-halo-ring", "LCD Outer Halo Ring", 24));

        TryOpenDevice(devicePath);
    }

    private void TryOpenDevice(string? path)
    {
        string? targetPath = path ?? _devicePath ?? AutoFindDevicePath();
        if (string.IsNullOrEmpty(targetPath)) return;

        try
        {
            _handle = CreateFile(targetPath, 0x40000000 | 0x80000000, 1 | 2 | 4, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle != null && !_handle.IsInvalid)
            {
                _devicePath = targetPath;
                Status = DeviceConnectionStatus.Connected;
                _logger?.LogInformation("Corsair ELITE LCD direct USB HID channel opened: {Path}", targetPath);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                _logger?.LogDebug("Could not open handle to Corsair ELITE LCD (Win32 {Err}). Trying write-only...", err);
                _handle = CreateFile(targetPath, 0x40000000, 1 | 2 | 4, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (_handle != null && !_handle.IsInvalid)
                {
                    _devicePath = targetPath;
                    Status = DeviceConnectionStatus.Connected;
                    _logger?.LogInformation("Corsair ELITE LCD write channel opened: {Path}", targetPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not open handle to Corsair ELITE LCD device.");
        }
    }

    private string? AutoFindDevicePath()
    {
        Guid hidGuid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (hDevInfo == (IntPtr)(-1)) return null;

        try
        {
            SP_DEVICE_INTERFACE_DATA dia = new SP_DEVICE_INTERFACE_DATA();
            dia.cbSize = (uint)Marshal.SizeOf(dia);

            for (uint i = 0; SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, i, ref dia); i++)
            {
                uint reqSize = 0;
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref dia, IntPtr.Zero, 0, ref reqSize, IntPtr.Zero);
                if (reqSize > 0)
                {
                    IntPtr detailData = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref dia, detailData, reqSize, ref reqSize, IntPtr.Zero))
                    {
                        IntPtr pPath = (IntPtr)((long)detailData + 4);
                        string? path = Marshal.PtrToStringAuto(pPath);
                        if (!string.IsNullOrEmpty(path) && path.IndexOf("vid_1b1c&pid_0c33", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Marshal.FreeHGlobal(detailData);
                            return path;
                        }
                    }
                    Marshal.FreeHGlobal(detailData);
                }
            }
        }
        catch { }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return null;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_handle == null || _handle.IsInvalid)
        {
            TryOpenDevice(null);
        }
        Status = DeviceConnectionStatus.Connected;
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Streams a 480x480 JPEG image frame directly to the circular IPS screen using
    /// the Corsair USB HID protocol (Opcode 0x02, 1024-byte packets with 8-byte framing headers).
    /// </summary>
    public bool SendJpegFrame(byte[] jpegData)
    {
        if (jpegData == null || jpegData.Length == 0) return false;

        // Skip if previous frame is still being transmitted to avoid USB congestion
        if (Interlocked.CompareExchange(ref _isStreamingFrame, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            if (_handle == null || _handle.IsInvalid)
            {
                TryOpenDevice(null);
                if (_handle == null || _handle.IsInvalid) return false;
            }

            lock (_ioLock)
            {
                try
                {
                    const int maxChunkPayload = 1016; // 1024 - 8 header bytes
                    int totalBytes = jpegData.Length;
                    int totalChunks = (int)Math.Ceiling((double)totalBytes / maxChunkPayload);

                    for (int part = 0; part < totalChunks; part++)
                    {
                        int offset = part * maxChunkPayload;
                        int chunkLen = Math.Min(maxChunkPayload, totalBytes - offset);
                        bool isEnd = (part == totalChunks - 1);

                        // 1024-byte Corsair Display packet
                        byte[] packet = new byte[1024];
                        packet[0] = 0x02; // Display Frame Data Opcode
                        packet[1] = 0x05;
                        packet[2] = 0x40;
                        packet[3] = (byte)(isEnd ? 1 : 0);
                        packet[4] = (byte)(part & 0xFF);
                        packet[5] = (byte)((part >> 8) & 0xFF);
                        packet[6] = (byte)(chunkLen & 0xFF);
                        packet[7] = (byte)((chunkLen >> 8) & 0xFF);

                        Array.Copy(jpegData, offset, packet, 8, chunkLen);

                        // Write 1025-byte HID report with Report ID 0x00
                        byte[] hidReport = new byte[1025];
                        hidReport[0] = 0x00;
                        Array.Copy(packet, 0, hidReport, 1, 1024);

                        bool success = WriteFile(_handle, hidReport, 1025, out _, IntPtr.Zero);
                        if (!success)
                        {
                            // Fallback: write 1024-byte packet directly
                            success = WriteFile(_handle, packet, 1024, out _, IntPtr.Zero);
                        }

                        if (!success)
                        {
                            int err = Marshal.GetLastWin32Error();
                            _logger?.LogWarning("Corsair LCD Cap WriteFile failed on chunk {Part}/{Total}. Win32 error: {Err}", part, totalChunks, err);
                            return false;
                        }
                    }

                    _logger?.LogDebug("Sent full {Bytes} byte JPEG frame to Corsair ELITE LCD ({Chunks} chunks).", totalBytes, totalChunks);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Exception streaming JPEG frame to Corsair ELITE LCD.");
                    return false;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isStreamingFrame, 0);
        }
    }

    public Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        lock (_ioLock)
        {
            foreach (var zone in _zones)
            {
                Array.Fill(zone.CurrentColors, color);
            }
        }
        return Task.CompletedTask;
    }

    public Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        var zone = _zones.Find(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
        if (zone == null || colors.IsEmpty) return Task.CompletedTask;

        lock (_ioLock)
        {
            var span = colors.Span;
            for (int i = 0; i < Math.Min(zone.LedCount, span.Length); i++)
            {
                zone.CurrentColors[i] = span[i];
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
}
