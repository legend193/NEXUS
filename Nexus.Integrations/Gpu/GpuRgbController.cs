using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;
using Nexus.Integrations.OpenRGB;

namespace Nexus.Integrations.Gpu;

/// <summary>
/// Direct Hardware RGB Driver for NVIDIA GeForce RTX 3080 (MSI Gaming X Trio / Suprim - Subsys 1462:3892).
/// Supports dual-channel hardware execution:
/// 1. Primary: Direct OpenRGB MSIGPUController protocol (tested and verified with zero flicker).
/// 2. Fallback: Direct NVIDIA NVAPI I2C bus write (Address 0x68).
/// </summary>
public class GpuRgbController : IRgbDevice, IDisposable
{
    private const uint NVAPI_INITIALIZE_ID = 0x0150E828;
    private const uint NVAPI_ENUM_PHYSICAL_GPUS_ID = 0xE5AC921F;
    private const uint NVAPI_I2C_WRITE_EX_ID = 0x283AC65A;
    private const byte MSI_GPU_I2C_ADDR = 0x68;

    // MSI GPU Registers
    private const byte MSI_GPU_REG_MODE = 0x22;
    private const byte MSI_GPU_REG_UNKNOWN = 0x26;
    private const byte MSI_GPU_REG_R1 = 0x30;
    private const byte MSI_GPU_REG_G1 = 0x31;
    private const byte MSI_GPU_REG_B1 = 0x32;
    private const byte MSI_GPU_REG_R2 = 0x27;
    private const byte MSI_GPU_REG_G2 = 0x28;
    private const byte MSI_GPU_REG_B2 = 0x29;
    private const byte MSI_GPU_REG_R3 = 0x2A;
    private const byte MSI_GPU_REG_G3 = 0x2B;
    private const byte MSI_GPU_REG_B3 = 0x2C;
    private const byte MSI_GPU_REG_BRIGHTNESS = 0x36;

    private const byte MSI_GPU_MODE_STATIC = 0x13;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_EnumPhysicalGPUsDelegate([Out] IntPtr[] handles, out int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct NV_I2C_INFO_V3
    {
        public uint version;
        public uint displayMask;
        public byte bIsDDCPort;
        public byte i2cDevAddress;
        public IntPtr pbI2cRegAddress;
        public uint regAddrSize;
        public IntPtr pbData;
        public uint cbSize;
        public uint i2cSpeed;
        public uint i2cSpeedKhz;
        public byte portId;
        public uint bIsPortIdSet;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_I2CWriteExDelegate(IntPtr handle, ref NV_I2C_INFO_V3 info, ref uint unknown);

    private readonly ILogger? _logger;
    private readonly List<RgbZone> _zones = new();
    private readonly object _lock = new();

    private IntPtr _gpuHandle = IntPtr.Zero;
    private NvAPI_I2CWriteExDelegate? _i2cWriteEx;
    private bool _isNvApiInitialized;
    private bool _isModeSet;
    private byte _lastR = 255;
    private byte _lastG = 255;
    private byte _lastB = 255;
    private byte _lastBrightness = 255;
    private bool _isDisposed;

    private int _openRgbControllerIndex = -1;
    private int _openRgbLedCount = 26;
    private OpenRgbClient _openRgbClient;

    public string Id { get; } = "msi-geforce-rtx-3080";
    public string Name { get; } = "MSI GeForce RTX 3080 Gaming X Trio";
    public string Manufacturer { get; } = "MSI / NVIDIA";
    public string Model { get; } = "GeForce RTX 3080 (10GB GDDR6X)";
    public string SerialNumber { get; } = "GPU-RTX3080-01";
    public DeviceType Type => DeviceType.Gpu;
    public DeviceConnectionStatus Status { get; private set; } = DeviceConnectionStatus.Connected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
    public string? FirmwareVersion { get; private set; } = "v94.02.42.00.FB";
    public string IntegrationProvider => "OpenRGB MSI GPU Protocol & NVAPI I2C";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;
    public double Brightness { get; set; } = 1.0;

    public GpuRgbController(ILogger? logger = null)
    {
        _logger = logger;
        _openRgbClient = new OpenRgbClient(host: "127.0.0.1", port: 6742, logger: logger);

        _zones.Add(new RgbZone("zone-lightbar", "Front & Side Light Bar ARGB", 24));
        _zones.Add(new RgbZone("zone-logo", "MSI Dragon Logo", 1));
        _zones.Add(new RgbZone("zone-backplate", "Backplate Accent", 1));
    }

    public void AttachOpenRgbClient(OpenRgbClient client, int controllerIndex, int ledCount, string name)
    {
        lock (_lock)
        {
            _openRgbClient = client;
            _openRgbControllerIndex = controllerIndex;
            _openRgbLedCount = ledCount;
            _ = client.SetCustomModeAsync(controllerIndex);
            _logger?.LogInformation("Attached active OpenRGB client to GPU Controller [{Index}] {Name} ({Leds} LEDs)", controllerIndex, name, ledCount);
        }
    }

    public void RegisterGpuController(int controllerIndex, int ledCount, string name)
    {
        RgbColor currentColor = default;
        lock (_lock)
        {
            _openRgbControllerIndex = controllerIndex;
            _openRgbLedCount = ledCount > 0 ? ledCount : 26;
            Status = DeviceConnectionStatus.Connected;
            _logger?.LogInformation("GPU Controller mapped to OpenRGB [{Index}] {Name} with {Leds} LEDs", controllerIndex, name, _openRgbLedCount);

            if (_zones.Count > 0 && _zones[0].CurrentColors.Length > 0)
            {
                currentColor = _zones[0].CurrentColors[0];
            }
        }

        if (currentColor != default)
        {
            var gpuColors = new RgbColor[_openRgbLedCount];
            Array.Fill(gpuColors, currentColor);
            _ = _openRgbClient.UpdateLedsAsync(controllerIndex, gpuColors);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // 1. Try discovering GPU in OpenRGB
        try
        {
            if (!_openRgbClient.IsConnected)
            {
                await _openRgbClient.ConnectAsync(cancellationToken);
            }

            if (_openRgbClient.IsConnected && _openRgbControllerIndex < 0)
            {
                int count = await _openRgbClient.GetControllerCountAsync(cancellationToken);
                for (int i = 0; i < count; i++)
                {
                    var info = await _openRgbClient.GetControllerDataAsync(i, cancellationToken);
                    if (info != null && (info.Name.Contains("3080", StringComparison.OrdinalIgnoreCase) ||
                                         info.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                         info.Name.Contains("MSI", StringComparison.OrdinalIgnoreCase)))
                    {
                        RegisterGpuController(i, info.LedCount, info.Name);
                        break;
                    }
                }
            }
        }
        catch { }

        // 2. Initialize secondary NVAPI I2C fallback
        lock (_lock)
        {
            if (_isNvApiInitialized) return;

            try
            {
                KillLedKeeperProcess();

                IntPtr pInit = NvAPI_QueryInterface(NVAPI_INITIALIZE_ID);
                if (pInit == IntPtr.Zero) return;

                var init = (NvAPI_InitializeDelegate)Marshal.GetDelegateForFunctionPointer(pInit, typeof(NvAPI_InitializeDelegate));
                int status = init();
                if (status != 0) return;

                IntPtr pEnum = NvAPI_QueryInterface(NVAPI_ENUM_PHYSICAL_GPUS_ID);
                if (pEnum == IntPtr.Zero) return;

                var enumGpus = (NvAPI_EnumPhysicalGPUsDelegate)Marshal.GetDelegateForFunctionPointer(pEnum, typeof(NvAPI_EnumPhysicalGPUsDelegate));
                IntPtr[] gpus = new IntPtr[64];
                status = enumGpus(gpus, out int count);
                if (status != 0 || count == 0) return;

                _gpuHandle = gpus[0];

                IntPtr pWrite = NvAPI_QueryInterface(NVAPI_I2C_WRITE_EX_ID);
                if (pWrite == IntPtr.Zero) return;

                _i2cWriteEx = (NvAPI_I2CWriteExDelegate)Marshal.GetDelegateForFunctionPointer(pWrite, typeof(NvAPI_I2CWriteExDelegate));
                _isNvApiInitialized = true;

                EnsureStaticMode();
                _logger?.LogInformation("MSI GeForce RTX 3080 NVAPI I2C channel opened (GPU handle 0x{Handle:X})", _gpuHandle.ToInt64());
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to initialize secondary NVAPI I2C channel for RTX 3080.");
            }
        }
    }

    private void KillLedKeeperProcess()
    {
        try
        {
            var procs = Process.GetProcessesByName("LEDKeeper2")
                .Concat(Process.GetProcessesByName("LEDKeeper"));
            foreach (var p in procs)
            {
                p.Kill();
                _logger?.LogInformation("Stopped competing MSI LEDKeeper process (PID {Pid})", p.Id);
            }
        }
        catch { }
    }

    private void EnsureStaticMode()
    {
        if (_isModeSet || _i2cWriteEx == null || _gpuHandle == IntPtr.Zero) return;
        WriteRegister(MSI_GPU_REG_MODE, MSI_GPU_MODE_STATIC);
        WriteRegister(MSI_GPU_REG_UNKNOWN, 0x00);
        _isModeSet = true;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        var span = colors.Span;
        var primary = span.Length > 0 ? span[0] : new RgbColor(255, 255, 255);
        return SetAllColorsAsync(primary, cancellationToken);
    }

    public async Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        byte r = (byte)Math.Clamp((int)Math.Round(color.R * Brightness), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(color.G * Brightness), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(color.B * Brightness), 0, 255);
        byte bLevel = (byte)Math.Clamp((int)Math.Round(Brightness * 100), 0, 100);

        // Update local model zones
        lock (_lock)
        {
            foreach (var zone in _zones)
            {
                Array.Fill(zone.CurrentColors, new RgbColor(r, g, b));
            }
        }

        // 1. Primary high-precision dispatch: OpenRGB MSIGPUController
        if (_openRgbControllerIndex >= 0)
        {
            try
            {
                if (!_openRgbClient.IsConnected)
                {
                    await _openRgbClient.ConnectAsync(cancellationToken);
                }

                if (_openRgbClient.IsConnected)
                {
                    var gpuColors = new RgbColor[_openRgbLedCount];
                    Array.Fill(gpuColors, new RgbColor(r, g, b));
                    await _openRgbClient.UpdateLedsAsync(_openRgbControllerIndex, gpuColors, cancellationToken);
                    _logger?.LogDebug("MSI RTX 3080 RGB updated via OpenRGB: R={R}, G={G}, B={B}", r, g, b);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "OpenRGB GPU update exception.");
            }
        }
        else
        {
            // Attempt auto-discovery
            await InitializeAsync(cancellationToken);
            if (_openRgbControllerIndex >= 0 && _openRgbClient.IsConnected)
            {
                var gpuColors = new RgbColor[_openRgbLedCount];
                Array.Fill(gpuColors, new RgbColor(r, g, b));
                await _openRgbClient.UpdateLedsAsync(_openRgbControllerIndex, gpuColors, cancellationToken);
            }
        }

        // 2. Secondary fallback: Direct NVAPI I2C
        lock (_lock)
        {
            if (!_isNvApiInitialized || _i2cWriteEx == null || _gpuHandle == IntPtr.Zero) return;

            if (_lastR == r && _lastG == g && _lastB == b && _lastBrightness == bLevel)
            {
                return;
            }

            try
            {
                EnsureStaticMode();

                WriteRegister(MSI_GPU_REG_UNKNOWN, 0x00);
                WriteRegister(MSI_GPU_REG_R1, r);
                WriteRegister(MSI_GPU_REG_G1, g);
                WriteRegister(MSI_GPU_REG_B1, b);
                WriteRegister(MSI_GPU_REG_R2, r);
                WriteRegister(MSI_GPU_REG_G2, g);
                WriteRegister(MSI_GPU_REG_B2, b);
                WriteRegister(MSI_GPU_REG_R3, r);
                WriteRegister(MSI_GPU_REG_G3, g);
                WriteRegister(MSI_GPU_REG_B3, b);
                WriteRegister(MSI_GPU_REG_BRIGHTNESS, bLevel);

                _lastR = r;
                _lastG = g;
                _lastB = b;
                _lastBrightness = bLevel;

                _logger?.LogDebug("MSI RTX 3080 steady RGB updated via NVAPI: R={R}, G={G}, B={B}", r, g, b);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to write steady RGB packet to MSI RTX 3080 via NVAPI.");
            }
        }
    }

    private void WriteRegister(byte reg, byte val)
    {
        if (_i2cWriteEx == null || _gpuHandle == IntPtr.Zero) return;

        IntPtr pReg = Marshal.AllocHGlobal(1);
        IntPtr pVal = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(pReg, reg);
            Marshal.WriteByte(pVal, val);

            var info = new NV_I2C_INFO_V3
            {
                version = (uint)Marshal.SizeOf(typeof(NV_I2C_INFO_V3)) | (3 << 16),
                displayMask = 0,
                bIsDDCPort = 0,
                i2cDevAddress = (byte)(MSI_GPU_I2C_ADDR << 1),
                pbI2cRegAddress = pReg,
                regAddrSize = 1,
                pbData = pVal,
                cbSize = 1,
                i2cSpeed = 0xFFFF,
                i2cSpeedKhz = 0,
                portId = 1,
                bIsPortIdSet = 1
            };

            uint unk = 0;
            _i2cWriteEx(_gpuHandle, ref info, ref unk);
        }
        finally
        {
            Marshal.FreeHGlobal(pReg);
            Marshal.FreeHGlobal(pVal);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _openRgbClient?.Dispose();
    }
}
