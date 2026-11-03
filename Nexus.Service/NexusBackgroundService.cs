using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.EventBus;
using Nexus.Abstractions.Sensors;
using Nexus.Core.Automation;
using Nexus.Core.DeviceManager;
using Nexus.Core.ProfileManager;
using Nexus.Engines.FanControl;
using Nexus.Engines.Hardware;
using Nexus.Engines.RGB;
using Nexus.Integrations.Generic;
using Nexus.Integrations.OpenRGB;
using Nexus.Service.Ipc;
using Nexus.Storage.Database;
using Nexus.Storage.Repositories;

namespace Nexus.Service;

public class NexusBackgroundService : BackgroundService
{
    private readonly HardwareEngine _hardwareEngine;
    private readonly RgbEngine _rgbEngine;
    private readonly FanControlEngine _fanEngine;
    private readonly DeviceManager _deviceManager;
    private readonly ProfileManager _profileManager;
    private readonly AutomationEngine _automationEngine;
    private readonly NexusDatabase _database;
    private readonly StorageRepositories _repositories;
    private readonly IEventBus _eventBus;
    private readonly ILogger<NexusBackgroundService> _logger;
    private NexusNamedPipeServer? _ipcServer;

    public NexusBackgroundService(
        HardwareEngine hardwareEngine,
        RgbEngine rgbEngine,
        FanControlEngine fanEngine,
        DeviceManager deviceManager,
        ProfileManager profileManager,
        AutomationEngine automationEngine,
        NexusDatabase database,
        StorageRepositories repositories,
        IEventBus eventBus,
        ILogger<NexusBackgroundService> logger)
    {
        _hardwareEngine = hardwareEngine;
        _rgbEngine = rgbEngine;
        _fanEngine = fanEngine;
        _deviceManager = deviceManager;
        _profileManager = profileManager;
        _automationEngine = automationEngine;
        _database = database;
        _repositories = repositories;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Nexus Background Service initializing...");

        try
        {
            // 1. Initialize SQLite Database
            await _database.InitializeAsync();

            // 2. Register Device Providers & Discover Devices
            _deviceManager.RegisterProvider(new GenericWindowsDeviceProvider());
            await _deviceManager.DiscoverAllAsync(stoppingToken);

            // 3. Start Telemetry Engine
            await _hardwareEngine.StartAsync(stoppingToken);

            // 4. Start RGB Engine
            _rgbEngine.Start();

            // 5. Start Automation Engine
            await _automationEngine.StartAsync(stoppingToken);

            // 6. Start Named Pipe IPC Server
            _ipcServer = new NexusNamedPipeServer(_hardwareEngine, _profileManager, _rgbEngine, _fanEngine);
            _ipcServer.Start();

            _logger.LogInformation("Nexus Background Service is running and listening for IPC connections.");

            // 7. Background logging & periodic maintenance loop (every 5 seconds)
            while (!stoppingToken.IsCancellationRequested)
            {
                var snapshot = _hardwareEngine.CurrentSnapshot;
                if (snapshot.CpuUsagePercent.HasValue || snapshot.RamUsedGb > 0)
                {
                    try
                    {
                        await _repositories.LogTelemetryAsync(
                            DateTime.UtcNow,
                            snapshot.CpuUsagePercent,
                            snapshot.CpuTemperatureCelsius,
                            snapshot.GpuUsagePercent,
                            snapshot.GpuTemperatureCelsius,
                            snapshot.RamUsagePercent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to write telemetry sample to SQLite.");
                    }
                }

                await Task.Delay(5000, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Nexus Background Service cancellation requested.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Nexus Background Service.");
        }
        finally
        {
            _ipcServer?.Stop();
            await _hardwareEngine.StopAsync(CancellationToken.None);
            _rgbEngine.Stop();
            await _automationEngine.StopAsync(CancellationToken.None);
            await _fanEngine.ResetAllToSafeDefaultsAsync();
            _logger.LogInformation("Nexus Background Service shutdown complete.");
        }
    }
}
