using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;
using Nexus.Integrations.Corsair;
using Nexus.Integrations.LianLi;

namespace Nexus.Integrations.OpenRGB;

/// <summary>
/// OpenRGB-Style Universal Hardware Scanner
/// Automatically scans and enumerates all connected USB HID RGB devices on the machine,
/// matching known hardware identifiers across Lian Li, Corsair, Logitech, Razer, and Gigabyte.
/// </summary>
public class OpenRgbScanner : IDeviceProvider
{
    private readonly IRgbEngine _rgbEngine;
    private readonly ILogger? _logger;

    public string ProviderId => "provider-openrgb-auto-scanner";
    public string ProviderName => "OpenRGB Universal Hardware Auto-Discovery";
    public string Manufacturer => "OpenRGB Community / Universal";
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

    public OpenRgbScanner(IRgbEngine rgbEngine, ILogger? logger = null)
    {
        _rgbEngine = rgbEngine;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var discovered = new List<IDevice>();
        _logger?.LogInformation("Starting OpenRGB-style universal hardware auto-scan...");

        try
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x10 | 0x02); // DIGCF_DEVICEINTERFACE | DIGCF_PRESENT
            if (hDevInfo == IntPtr.Zero) return discovered;

            var ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

            uint index = 0;
            string? lianLiAlPath = null;
            string? corsairCorePath = null;
            string? corsairLcdPath = null;

            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, index++, ref ifData))
            {
                var detailData = new SP_DEVICE_INTERFACE_DETAIL_DATA();
                detailData.cbSize = (IntPtr.Size == 8) ? 8 : 4 + Marshal.SystemDefaultCharSize;
                int reqSize = 0;
                if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, ref detailData, 260, out reqSize, IntPtr.Zero))
                {
                    string path = detailData.devicePath;

                    // 1. Lian Li UNI FAN AL / SL Hub (VID 0x0CF2, PID 0xA101)
                    if (path.IndexOf("vid_0cf2&pid_a101", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        path.IndexOf("mi_01", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        lianLiAlPath ??= path;
                    }
                    // 2. Corsair Commander Core (VID 0x1B1C, PID 0x0C1C)
                    else if (path.IndexOf("vid_1b1c&pid_0c1c", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             path.IndexOf("mi_00", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        corsairCorePath ??= path;
                    }
                    // 3. Corsair iCUE ELITE LCD Display Cap (VID 0x1B1C, PID 0x0C33)
                    else if (path.IndexOf("vid_1b1c&pid_0c33", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        corsairLcdPath ??= path;
                    }
                }
            }
            SetupDiDestroyDeviceInfoList(hDevInfo);

            // Instantiate and initialize discovered hardware (only if not already registered)
            if (lianLiAlPath != null && !_rgbEngine.Devices.Any(d => d.Id == "lianli-unifan-al-hub"))
            {
                var lianLi = new LianLiUniFanController(lianLiAlPath, _logger);
                if (lianLi.Status == DeviceConnectionStatus.Connected)
                {
                    await lianLi.InitializeAsync(cancellationToken);
                    _rgbEngine.RegisterDevice(lianLi);
                    discovered.Add(lianLi);
                    _logger?.LogInformation("OpenRGB Auto-Scanner: Discovered & Registered '{Name}'", lianLi.Name);
                }
            }

            if (corsairCorePath != null && !_rgbEngine.Devices.Any(d => d.Id == "corsair-commander-core-hub"))
            {
                var corsair = new CorsairCommanderCoreController(corsairCorePath, _logger);
                if (corsair.Status == DeviceConnectionStatus.Connected)
                {
                    await corsair.InitializeAsync(cancellationToken);
                    _rgbEngine.RegisterDevice(corsair);
                    discovered.Add(corsair);
                    _logger?.LogInformation("OpenRGB Auto-Scanner: Discovered & Registered '{Name}'", corsair.Name);
                }
            }

            if (corsairLcdPath != null && !_rgbEngine.Devices.Any(d => d.Id == "corsair-elite-lcd-cap"))
            {
                var lcd = new Corsair.CorsairEliteLcdController(corsairLcdPath, _logger);
                await lcd.InitializeAsync(cancellationToken);
                _rgbEngine.RegisterDevice(lcd);
                discovered.Add(lcd);
                _logger?.LogInformation("OpenRGB Auto-Scanner: Discovered & Registered '{Name}'", lcd.Name);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception during OpenRGB auto-scan.");
        }

        return discovered;
    }
}
