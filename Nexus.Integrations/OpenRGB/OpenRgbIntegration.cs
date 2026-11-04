using System.IO;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Rgb;
using Nexus.Integrations.Corsair;
using Nexus.Integrations.LianLi;

namespace Nexus.Integrations.OpenRGB;

public record OpenRgbControllerInfo(int DeviceId, string Name, string Vendor, string Description, int Type, int LedCount);

public class OpenRgbDevice : IRgbDevice
{
    private readonly OpenRgbClient _client;
    private readonly int _deviceIndex;
    private readonly List<RgbZone> _zones = new();

    public string Id { get; }
    public string Name { get; }
    public string Manufacturer { get; }
    public string Model { get; }
    public string SerialNumber { get; }
    public DeviceType Type { get; }
    public DeviceConnectionStatus Status { get; set; } = DeviceConnectionStatus.Connected;
    public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
    public string? FirmwareVersion { get; }
    public string IntegrationProvider => "OpenRGB Protocol";
    public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public IReadOnlyList<RgbZone> Zones => _zones;
    public double Brightness { get; set; } = 1.0;

    public OpenRgbDevice(OpenRgbClient client, int deviceIndex, string name, string vendor, string description, int ledCount)
    {
        _client = client;
        _deviceIndex = deviceIndex;
        Id = $"openrgb-{deviceIndex}-{name.Replace(" ", "_")}";
        Name = name;
        Manufacturer = string.IsNullOrWhiteSpace(vendor) ? "OpenRGB Device" : vendor;
        Model = description;
        SerialNumber = $"ORGB-{deviceIndex}";
        Type = DeviceType.LightingController;

        // Create main zone
        var mainZone = new RgbZone($"zone-0", "Main", ledCount);
        _zones.Add(mainZone);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        var zone = _zones.FirstOrDefault(z => z.ZoneId == zoneId);
        if (zone != null)
        {
            var span = colors.Span;
            for (int i = 0; i < Math.Min(zone.LedCount, span.Length); i++)
            {
                zone.CurrentColors[i] = span[i];
            }
        }

        await _client.UpdateLedsAsync(_deviceIndex, colors, cancellationToken);
    }

    public Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
    {
        foreach (var zone in _zones)
        {
            var colors = new RgbColor[zone.LedCount];
            Array.Fill(colors, color);
            return SetZoneColorsAsync(zone.ZoneId, colors, cancellationToken);
        }
        return Task.CompletedTask;
    }
}

public class OpenRgbClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private bool _isDisposed;

    public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

    public OpenRgbClient(string host = "127.0.0.1", int port = 6742, ILogger? logger = null)
    {
        _host = host;
        _port = port;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return true;

        try
        {
            Disconnect();
            _tcpClient = new TcpClient();
            var connectTask = _tcpClient.ConnectAsync(_host, _port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(1500, cancellationToken));
            if (completed == connectTask && _tcpClient.Connected)
            {
                _stream = _tcpClient.GetStream();
                // Register client name with OpenRGB server (Command 50 = NET_PACKET_ID_SET_CLIENT_NAME)
                byte[] nameBytes = Encoding.ASCII.GetBytes("Nexus Ecosystem\0");
                await SendPacketAsync(0, 50, nameBytes, cancellationToken);
                _logger?.LogInformation("Connected and registered with OpenRGB server at {Host}:{Port}", _host, _port);
                return true;
            }
        }
        catch (Exception ex)
        {
            Disconnect();
            _logger?.LogDebug(ex, "OpenRGB server not reachable at {Host}:{Port}", _host, _port);
        }

        return false;
    }

    private static readonly bool IsTestHost =
        System.Diagnostics.Process.GetCurrentProcess().ProcessName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName != null && a.FullName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase));

    public async Task<bool> ConnectWithAutoStartAsync(CancellationToken cancellationToken = default)
    {
        bool ok = await ConnectAsync(cancellationToken);
        if (ok) return true;

        if (IsTestHost) return false;

        EnsureOpenRgbServerRunning(_logger);
        for (int i = 0; i < 6; i++)
        {
            await Task.Delay(800, cancellationToken);
            ok = await ConnectAsync(cancellationToken);
            if (ok) return true;
        }

        return false;
    }

    public static void EnsureOpenRgbServerRunning(ILogger? logger = null)
    {
        try
        {
            // 1. Ensure PawnIO kernel driver service is running
            try
            {
                using var procPawn = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = "start PawnIO",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                procPawn?.WaitForExit(1000);
            }
            catch { }

            // 2. Check if port 6742 is already reachable
            try
            {
                using var probe = new TcpClient();
                var probeTask = probe.ConnectAsync("127.0.0.1", 6742);
                if (probeTask.Wait(300) && probe.Connected)
                {
                    return;
                }
            }
            catch { }

            // 3. Port 6742 is NOT accepting connections. Kill any existing non-server/hung OpenRGB processes
            var existing = System.Diagnostics.Process.GetProcessesByName("OpenRGB");
            foreach (var p in existing)
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
            }

            // 4. Find OpenRGB.exe path
            string[] searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "tools", "OpenRGB", "OpenRGB Windows 64-bit", "OpenRGB.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenRGB", "OpenRGB.exe")
            };

            string? exePath = searchPaths.FirstOrDefault(File.Exists);
            if (exePath != null)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start /B \"\" \"{exePath}\" --server --startminimized",
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi);
                logger?.LogInformation("Launched OpenRGB background server from {Path}", exePath);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to launch OpenRGB background server.");
        }
    }

    public static void StopOpenRgbServer(ILogger? logger = null)
    {
        try
        {
            var existing = System.Diagnostics.Process.GetProcessesByName("OpenRGB");
            foreach (var p in existing)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
                catch { }
            }
            logger?.LogInformation("Stopped OpenRGB background processes.");
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Error while stopping OpenRGB process.");
        }
    }

    private void Disconnect()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
    }

    public async Task<int> GetControllerCountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null) return 0;
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            // OpenRGB Command: 0 = RequestControllerCount
            await SendPacketAsync(0, 0, Array.Empty<byte>(), cancellationToken);
            for (int retry = 0; retry < 5; retry++)
            {
                var (deviceId, commandId, data) = await ReceivePacketAsync(cancellationToken);
                if (commandId == 0 && data.Length >= 4)
                {
                    return BitConverter.ToInt32(data, 0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting OpenRGB controller count.");
        }
        finally
        {
            _ioLock.Release();
        }
        return 0;
    }

    public async Task<OpenRgbControllerInfo?> GetControllerDataAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null) return null;
        try
        {
            // OpenRGB Command: 1 = RequestControllerData
            // Client protocol version 4
            byte[] payload = BitConverter.GetBytes((uint)4);
            await SendPacketAsync((uint)deviceId, 1, payload, cancellationToken);
            var (respDevId, commandId, data) = await ReceivePacketAsync(cancellationToken);
            if (commandId == 1 && data.Length > 12)
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                uint dataSize = reader.ReadUInt32();
                int devType = reader.ReadInt32();

                string name = ReadLengthPrefixedString(reader);
                string vendor = ReadLengthPrefixedString(reader);
                string description = ReadLengthPrefixedString(reader);
                string version = ReadLengthPrefixedString(reader);
                string serial = ReadLengthPrefixedString(reader);
                string location = ReadLengthPrefixedString(reader);

                int parsedLeds = 0;
                try
                {
                    // Modes
                    ushort numModes = reader.ReadUInt16();
                    for (int m = 0; m < numModes; m++)
                    {
                        ReadLengthPrefixedString(reader);
                        reader.ReadInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        ushort numColors = reader.ReadUInt16();
                        if (numColors > 0 && reader.BaseStream.Position + numColors * 4 <= reader.BaseStream.Length)
                        {
                            reader.ReadBytes(numColors * 4);
                        }
                    }

                    // Zones
                    ushort numZones = reader.ReadUInt16();
                    int zoneLedsSum = 0;
                    for (int z = 0; z < numZones; z++)
                    {
                        ReadLengthPrefixedString(reader);
                        reader.ReadInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        uint zCount = reader.ReadUInt32();
                        zoneLedsSum += (int)zCount;
                        ushort matLen = reader.ReadUInt16();
                        if (matLen > 0)
                        {
                            uint matH = reader.ReadUInt32();
                            uint matW = reader.ReadUInt32();
                            int matBytes = (int)(matH * matW * 4);
                            if (matBytes > 0 && reader.BaseStream.Position + matBytes <= reader.BaseStream.Length)
                            {
                                reader.ReadBytes(matBytes);
                            }
                        }
                    }

                    // LEDs count
                    if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
                    {
                        ushort numLeds = reader.ReadUInt16();
                        parsedLeds = numLeds > 0 ? numLeds : zoneLedsSum;
                    }
                    else
                    {
                        parsedLeds = zoneLedsSum;
                    }
                }
                catch
                {
                    if (name.Contains("Vengeance", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("DRAM", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedLeds = 10;
                    }
                    else
                    {
                        parsedLeds = 32;
                    }
                }

                if (parsedLeds <= 0)
                {
                    parsedLeds = (name.Contains("Vengeance", StringComparison.OrdinalIgnoreCase) ||
                                  name.Contains("DRAM", StringComparison.OrdinalIgnoreCase)) ? 10 : 32;
                }

                return new OpenRgbControllerInfo(deviceId, name, vendor, description, devType, parsedLeds);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to get controller data for device {DeviceId}", deviceId);
        }
        return null;
    }

    private static string ReadLengthPrefixedString(BinaryReader reader)
    {
        if (reader.BaseStream.Position + 2 > reader.BaseStream.Length) return string.Empty;
        ushort len = reader.ReadUInt16();
        if (len == 0 || reader.BaseStream.Position + len > reader.BaseStream.Length) return string.Empty;
        byte[] bytes = reader.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    public async Task SetCustomModeAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null) return;
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            // OpenRGB Command: 1100 = RGBCONTROLLER_SETCUSTOMMODE (0 byte payload)
            await SendPacketAsync((uint)deviceId, 1100, Array.Empty<byte>(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to set custom mode on OpenRGB device {DeviceId}", deviceId);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task UpdateLedsAsync(int deviceId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null || colors.IsEmpty) return;
        var span = colors.Span;
        int count = span.Length;
        // Payload: uint32 data_size, uint16 num_colors, [r, g, b, 0] * count
        uint dataSize = (uint)(6 + count * 4);
        byte[] payload = new byte[dataSize];
        BitConverter.GetBytes(dataSize).CopyTo(payload, 0);
        BitConverter.GetBytes((ushort)count).CopyTo(payload, 4);

        int offset = 6;
        for (int i = 0; i < count; i++)
        {
            payload[offset++] = span[i].R;
            payload[offset++] = span[i].G;
            payload[offset++] = span[i].B;
            payload[offset++] = 0; // padding
        }

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await SendPacketAsync((uint)deviceId, 1050, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to send LED update to OpenRGB device {DeviceId}", deviceId);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task SendPacketAsync(uint deviceId, uint commandId, byte[] data, CancellationToken token)
    {
        if (_stream == null) return;
        try
        {
            // Header: "ORGB" (4 bytes) + DeviceId (4) + CommandId (4) + DataLength (4)
            byte[] header = new byte[16];
            Encoding.ASCII.GetBytes("ORGB").CopyTo(header, 0);
            BitConverter.GetBytes(deviceId).CopyTo(header, 4);
            BitConverter.GetBytes(commandId).CopyTo(header, 8);
            BitConverter.GetBytes((uint)data.Length).CopyTo(header, 12);

            await _stream.WriteAsync(header, token);
            if (data.Length > 0)
            {
                await _stream.WriteAsync(data, token);
            }
        }
        catch (Exception ex)
        {
            Disconnect();
            throw;
        }
    }

    private async Task<(uint deviceId, uint commandId, byte[] data)> ReceivePacketAsync(CancellationToken token)
    {
        if (_stream == null) return (0, 0, Array.Empty<byte>());
        try
        {
            byte[] header = new byte[16];
            await _stream.ReadExactlyAsync(header.AsMemory(0, 16), token);

            uint devId = BitConverter.ToUInt32(header, 4);
            uint cmdId = BitConverter.ToUInt32(header, 8);
            uint dataLen = BitConverter.ToUInt32(header, 12);

            byte[] data = new byte[dataLen];
            if (dataLen > 0)
            {
                await _stream.ReadExactlyAsync(data.AsMemory(0, (int)dataLen), token);
            }

            return (devId, cmdId, data);
        }
        catch (Exception ex)
        {
            Disconnect();
            throw;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _ioLock.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();
    }
}

public class OpenRgbDeviceProvider : IDeviceProvider
{
    private readonly OpenRgbClient _client;
    private readonly IRgbEngine _rgbEngine;
    private readonly ILogger? _logger;

    public string ProviderId => "provider-openrgb-sdk";
    public string ProviderName => "OpenRGB SDK Server (localhost:6742)";
    public string Manufacturer => "OpenRGB";
    public bool IsSupportedOnCurrentSystem => true;

    public event EventHandler<IDevice>? DeviceConnected;
    public event EventHandler<string>? DeviceDisconnected;

    public OpenRgbDeviceProvider(IRgbEngine rgbEngine, ILogger? logger = null)
    {
        _rgbEngine = rgbEngine;
        _client = new OpenRgbClient(logger: logger);
        _logger = logger;
    }

    public async Task<IReadOnlyList<IDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();
        try
        {
            bool connected = await _client.ConnectWithAutoStartAsync(cancellationToken);
            if (!connected)
            {
                _logger?.LogWarning("OpenRGB SDK Bridge could not connect to 127.0.0.1:6742.");
                return devices;
            }

            int count = await _client.GetControllerCountAsync(cancellationToken);
            _logger?.LogInformation("OpenRGB SDK Server connected. Found {Count} hardware controllers.", count);

            for (int i = 0; i < count; i++)
            {
                var info = await _client.GetControllerDataAsync(i, cancellationToken);
                string devName = info?.Name ?? $"OpenRGB Hardware Device #{i + 1}";
                string devVendor = info?.Vendor ?? "OpenRGB Universal";
                string devDesc = info?.Description ?? "Synced via OpenRGB SDK Port 6742";
                int ledCount = (info?.LedCount > 0) ? info.LedCount : 10;
                int devType = info?.Type ?? 0;

                // 1. RAM / DRAM Modules (Corsair Vengeance RT / Pro / DDR4)
                if (devType == 1 || devName.Contains("Vengeance", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("DRAM", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("Corsair Memory", StringComparison.OrdinalIgnoreCase))
                {
                    var ramCtrl = _rgbEngine.Devices.OfType<Nexus.Integrations.Memory.CorsairVengeanceRgbController>().FirstOrDefault();
                    if (ramCtrl != null)
                    {
                        ramCtrl.AttachOpenRgbClient(_client, i, ledCount, devName);
                    }

                    await _client.SetCustomModeAsync(i, cancellationToken);
                    var ramDev = new OpenRgbDevice(_client, i, $"RAM: {devName}", devVendor, devDesc, ledCount);
                    _rgbEngine.RegisterDevice(ramDev);
                    devices.Add(ramDev);
                    _logger?.LogInformation("OpenRGB Bridge: Registered RAM Controller [{Index}] '{Name}' ({Leds} LEDs)", i, devName, ledCount);
                    continue;
                }

                // 2. Gaming Mouse (Logitech G502 HERO / Razer / Corsair / SteelSeries)
                if (devType == 6 || devName.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("G502", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("HERO", StringComparison.OrdinalIgnoreCase))
                {
                    await _client.SetCustomModeAsync(i, cancellationToken);
                    var mouseDev = new OpenRgbDevice(_client, i, $"Mouse: {devName}", devVendor, devDesc, ledCount);
                    _rgbEngine.RegisterDevice(mouseDev);
                    devices.Add(mouseDev);
                    _logger?.LogInformation("OpenRGB Bridge: Registered Mouse Controller [{Index}] '{Name}' ({Leds} LEDs)", i, devName, ledCount);
                    continue;
                }

                // 3. Gaming Keyboard (Mechanical / EVision / Sonix / Corsair / Razer / Ducky)
                if (devType == 5 || devName.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("Keychron", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("EVision", StringComparison.OrdinalIgnoreCase))
                {
                    await _client.SetCustomModeAsync(i, cancellationToken);
                    var kbDev = new OpenRgbDevice(_client, i, $"Keyboard: {devName}", devVendor, devDesc, ledCount);
                    _rgbEngine.RegisterDevice(kbDev);
                    devices.Add(kbDev);
                    _logger?.LogInformation("OpenRGB Bridge: Registered Keyboard Controller [{Index}] '{Name}' ({Leds} LEDs)", i, devName, ledCount);
                    continue;
                }

                // 4. GPU (MSI GeForce RTX 3080 Gaming X Trio / Suprim / Ventus)
                if (devType == 2 || devName.Contains("3080", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                    devName.Contains("Gaming X Trio", StringComparison.OrdinalIgnoreCase))
                {
                    var gpuCtrl = _rgbEngine.Devices.OfType<Nexus.Integrations.Gpu.GpuRgbController>().FirstOrDefault();
                    if (gpuCtrl != null)
                    {
                        gpuCtrl.AttachOpenRgbClient(_client, i, ledCount, devName);
                    }

                    await _client.SetCustomModeAsync(i, cancellationToken);
                    var gpuDev = new OpenRgbDevice(_client, i, $"GPU: {devName}", devVendor, devDesc, ledCount);
                    _rgbEngine.RegisterDevice(gpuDev);
                    devices.Add(gpuDev);
                    _logger?.LogInformation("OpenRGB Bridge: Registered GPU Controller [{Index}] '{Name}'", i, devName);
                    continue;
                }

                // 5. Skip devices that are already managed natively to prevent redundant packet collisions
                bool isNativeLianLi = devName.Contains("Lian Li", StringComparison.OrdinalIgnoreCase) && _rgbEngine.Devices.Any(d => d is LianLiUniFanController);
                bool isNativeCommander = devName.Contains("Commander", StringComparison.OrdinalIgnoreCase) && _rgbEngine.Devices.Any(d => d is CorsairCommanderCoreController);

                if (isNativeLianLi || isNativeCommander)
                {
                    _logger?.LogDebug("OpenRGB Device [{Index}] '{Name}' skipped (driven by native NEXUS USB HID driver)", i, devName);
                    continue;
                }

                await _client.SetCustomModeAsync(i, cancellationToken);
                var dev = new OpenRgbDevice(_client, i, devName, devVendor, devDesc, ledCount);
                _rgbEngine.RegisterDevice(dev);
                devices.Add(dev);
            }
            _logger?.LogInformation("OpenRGB SDK Bridge: Discovered & Synced {Count} devices.", devices.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "OpenRGB SDK Bridge query encountered an exception.");
        }
        return devices;
    }
}
