using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;
using Nexus.Integrations.Corsair;
using Nexus.Integrations.Gpu;
using Nexus.Integrations.Keyboard;
using Nexus.Integrations.LianLi;
using Nexus.Integrations.Logitech;
using Nexus.Integrations.Memory;
using Nexus.Integrations.Motherboard;

namespace Nexus.Integrations;

public class HardwareHubProvider : IDeviceProvider
{
    private readonly ILogger? _logger;
    private readonly IRgbEngine _rgbEngine;

    public string ProviderId => "provider-hardware-hubs";
    public string ProviderName => "Unified PC Hardware Ecosystem (Lian Li, Corsair, GPU, Mouse & Keyboard)";
    public string Manufacturer => "Lian Li / Corsair / MSI / Logitech";
    public bool IsSupportedOnCurrentSystem => OperatingSystem.IsWindows();

    public event EventHandler<IDevice>? DeviceConnected;
    public event EventHandler<string>? DeviceDisconnected;

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid interfaceClassGuid;
        public int flags;
        public IntPtr reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string devicePath;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    public HardwareHubProvider(IRgbEngine rgbEngine, ILogger<HardwareHubProvider>? logger = null)
    {
        _rgbEngine = rgbEngine;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();

        try
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x10 | 0x02); // DIGCF_DEVICEINTERFACE | DIGCF_PRESENT
            if (hDevInfo == IntPtr.Zero) return devices;

            var ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

            uint index = 0;
            string? lianLiPath = null;
            string? corsairPath = null;
            string? logitechMousePath = null;
            string? keyboardPath = null;
            string? gigabyteMbPath = null;
            string? corsairLcdPath = null;

            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, index++, ref ifData))
            {
                var detailData = new SP_DEVICE_INTERFACE_DETAIL_DATA();
                detailData.cbSize = (IntPtr.Size == 8) ? 8 : 4 + Marshal.SystemDefaultCharSize;
                int reqSize = 0;
                if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, ref detailData, 260, out reqSize, IntPtr.Zero))
                {
                    string path = detailData.devicePath;

                    // Lian Li UNI FAN Hub (VID 0x0CF2, PID 0xA101, MI_01)
                    if (path.IndexOf("vid_0cf2&pid_a101", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("mi_01", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        lianLiPath ??= path;
                    }
                    // Corsair Commander Core (VID 0x1B1C, PID 0x0C1C, MI_00)
                    else if (path.IndexOf("vid_1b1c&pid_0c1c", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("mi_00", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        corsairPath ??= path;
                    }
                    // Logitech G502 HERO (VID 0x046D, PID 0xC08B, MI_01 & COL05 for Long Reports)
                    else if (path.IndexOf("vid_046d&pid_c08b", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("col05", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        logitechMousePath ??= path;
                    }
                    // Sonix Gaming Keyboard (VID 0x0C45, PID 0x652F, MI_01 & COL04)
                    else if (path.IndexOf("vid_0c45&pid_652f", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("col04", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        keyboardPath ??= path;
                    }
                    // Gigabyte Motherboard IT8297 USB RGB Fusion 2.0 (VID 0x048D, PID 0x5702, COL02)
                    else if (path.IndexOf("vid_048d&pid_5702", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("col02", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        gigabyteMbPath ??= path;
                    }
                    // Corsair iCUE ELITE LCD Display Cap (VID 0x1B1C, PID 0x0C33)
                    else if (path.IndexOf("vid_1b1c&pid_0c33", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        corsairLcdPath ??= path;
                    }
                }
            }
            SetupDiDestroyDeviceInfoList(hDevInfo);

            // 1. Lian Li UNI FAN Controller
            if (!string.IsNullOrEmpty(lianLiPath))
            {
                var lianLiController = new LianLiUniFanController(lianLiPath, _logger);
                await lianLiController.InitializeAsync(cancellationToken);
                devices.Add(lianLiController);
                _rgbEngine.RegisterDevice(lianLiController);
                _logger?.LogInformation("Registered Lian Li UNI FAN Hub into NEXUS RGB Ecosystem.");
            }

            // 2. Corsair Commander CORE Controller
            if (!string.IsNullOrEmpty(corsairPath))
            {
                var corsairController = new CorsairCommanderCoreController(corsairPath, _logger);
                await corsairController.InitializeAsync(cancellationToken);
                devices.Add(corsairController);
                _rgbEngine.RegisterDevice(corsairController);
                _logger?.LogInformation("Registered Corsair Commander Core Hub into NEXUS RGB Ecosystem.");
            }

            bool openRgbActive = IsOpenRgbServerRunning();

            // 3. Logitech G502 HERO Gaming Mouse (Bypass raw HID if OpenRGB is actively managing it)
            if (!string.IsNullOrEmpty(logitechMousePath) && !openRgbActive)
            {
                var logiController = new LogitechG502Controller(logitechMousePath, _logger);
                await logiController.InitializeAsync(cancellationToken);
                devices.Add(logiController);
                _rgbEngine.RegisterDevice(logiController);
                _logger?.LogInformation("Registered Logitech G502 HERO into NEXUS RGB Ecosystem.");
            }
            else if (openRgbActive)
            {
                _logger?.LogInformation("OpenRGB active: Delegating Logitech Mouse control to OpenRGB Provider.");
            }

            // 4. Mechanical Keyboard (Sonix / Gaming Keyboard - Bypass raw HID if OpenRGB is active)
            if (!string.IsNullOrEmpty(keyboardPath) && !openRgbActive)
            {
                var kbController = new SonixKeyboardController(keyboardPath, _logger);
                await kbController.InitializeAsync(cancellationToken);
                devices.Add(kbController);
                _rgbEngine.RegisterDevice(kbController);
                _logger?.LogInformation("Registered Gaming Mechanical Keyboard into NEXUS RGB Ecosystem.");
            }
            else if (openRgbActive)
            {
                _logger?.LogInformation("OpenRGB active: Delegating Mechanical Keyboard control to OpenRGB Provider.");
            }

            // 5. NVIDIA GeForce RTX 3080 GPU (MSI Gaming X Trio)
            var gpuController = new GpuRgbController(_logger);
            await gpuController.InitializeAsync(cancellationToken);
            devices.Add(gpuController);
            _rgbEngine.RegisterDevice(gpuController);
            _logger?.LogInformation("Registered MSI GeForce RTX 3080 into NEXUS RGB Ecosystem.");

            // 6. Gigabyte X570S AORUS PRO AX Motherboard (IT8297 USB RGB Fusion 2.0)
            string effectiveMbPath = gigabyteMbPath ?? @"\\?\hid#vid_048d&pid_5702&col02#9&1f5acea&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}";
            var mbController = new GigabyteMotherboardRgbController(effectiveMbPath, _logger);
            await mbController.InitializeAsync(cancellationToken);
            devices.Add(mbController);
            _rgbEngine.RegisterDevice(mbController);
            _logger?.LogInformation("Registered Gigabyte X570S AORUS Motherboard into NEXUS RGB Ecosystem.");

            // 7. Corsair VENGEANCE RGB RT Memory (4x 8GB Quad Kit)
            var ramController = new CorsairVengeanceRgbController(_logger);
            await ramController.InitializeAsync(cancellationToken);
            devices.Add(ramController);
            _rgbEngine.RegisterDevice(ramController);
            _logger?.LogInformation("Registered Corsair VENGEANCE RGB RT Memory into NEXUS RGB Ecosystem.");

            // 8. Corsair iCUE ELITE LCD Display Cap (VID 0x1B1C, PID 0x0C33)
            var lcdController = new CorsairEliteLcdController(corsairLcdPath, _logger);
            await lcdController.InitializeAsync(cancellationToken);
            devices.Add(lcdController);
            _rgbEngine.RegisterDevice(lcdController);
            _logger?.LogInformation("Registered Corsair iCUE ELITE LCD Display into NEXUS RGB Ecosystem.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error discovering physical hardware ecosystem.");
        }

        return devices;
    }

    private static bool IsOpenRgbServerRunning()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var ar = client.BeginConnect("127.0.0.1", 6742, null, null);
            bool success = ar.AsyncWaitHandle.WaitOne(100);
            if (success && client.Connected)
            {
                client.EndConnect(ar);
                return true;
            }
        }
        catch { }
        return System.Diagnostics.Process.GetProcessesByName("OpenRGB").Length > 0;
    }
}
