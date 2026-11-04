using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Audio;
using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.EventBus;
using Nexus.Abstractions.Profiles;
using Nexus.Abstractions.Rgb;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Core.Automation;
using Nexus.Core.Configuration;
using Nexus.Core.DeviceManager;
using Nexus.Core.EventBus;
using Nexus.Core.PluginManager;
using Nexus.Core.ProfileManager;
using Nexus.Core.Services;
using Nexus.Engines.Audio;
using Nexus.Engines.FanControl;
using Nexus.Engines.Hardware;
using Nexus.Engines.RGB;
using Nexus.Integrations;
using Nexus.Integrations.Generic;
using Nexus.Integrations.OpenRGB;
using Nexus.Storage.Database;
using Nexus.Storage.Repositories;
using Serilog;

namespace Nexus.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    public static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? PrewarmedWebView2Environment { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Pre-warm WebView2 Environment asynchronously in the background so RGB Studio opens instantly with zero lag
        try
        {
            string webView2Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "WebView2");
            Directory.CreateDirectory(webView2Folder);
            PrewarmedWebView2Environment = Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, webView2Folder);
        }
        catch { }

        // Prevent premature shutdown during async initialization
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled Dispatcher exception");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled AppDomain fatal exception");
            }
        };

        string logDir;
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            logDir = Path.Combine(appData, "NEXUS", "logs");
            Directory.CreateDirectory(logDir);
        }
        catch
        {
            logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
        }

        // Configure Structured Logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "nexus-.log"),
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Configuration
        services.AddSingleton<NexusConfiguration>();

        // Core Infrastructure
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<NexusDatabase>();
        services.AddSingleton<StorageRepositories>();

        // Hardware & Device Engines
        services.AddSingleton<HardwareEngine>();
        services.AddSingleton<AudioEngine>();
        services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<AudioEngine>());

        services.AddSingleton<RgbEngine>();
        services.AddSingleton<IRgbEngine>(sp => sp.GetRequiredService<RgbEngine>());

        services.AddSingleton<FanControlEngine>();
        services.AddSingleton<IFanControlEngine>(sp => sp.GetRequiredService<FanControlEngine>());

        services.AddSingleton<DeviceManager>();
        services.AddSingleton<ProfileManager>();
        services.AddSingleton<IProfileManager>(sp => sp.GetRequiredService<ProfileManager>());

        services.AddSingleton<HardwareConflictManager>();
        services.AddSingleton<AutomationEngine>();
        services.AddSingleton<PluginManager>();

        // ViewModels
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<RgbStudioViewModel>();
        services.AddSingleton<AudioStudioViewModel>();
        services.AddSingleton<HardwareMonitorViewModel>();
        services.AddSingleton<CoolingViewModel>();
        services.AddSingleton<DevicesViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<AutomationViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<PluginsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        // MainWindow & System Tray & Overlay
        services.AddSingleton<MainWindow>();
        services.AddSingleton<Window>(sp => sp.GetRequiredService<MainWindow>());
        services.AddSingleton<NexusTrayService>();
        services.AddSingleton<Views.MiniHudWindow>();
        services.AddTransient<Views.SonarPopoutWindow>();

        _serviceProvider = services.BuildServiceProvider();

        try
        {
            // Initialize SQLite Database
            var db = _serviceProvider.GetRequiredService<NexusDatabase>();
            await db.InitializeAsync();

            // Hardware Conflict Check
            var conflictManager = _serviceProvider.GetRequiredService<HardwareConflictManager>();
            conflictManager.CheckConflicts();

            // Discover Native Hardware Devices & USB Hubs
            var deviceManager = _serviceProvider.GetRequiredService<DeviceManager>();
            var rgbEngine = _serviceProvider.GetRequiredService<RgbEngine>();

            // Register Lian Li & Corsair hardware hub provider FIRST so physical USB hubs are wired to RgbEngine
            var hubProvider = new HardwareHubProvider(rgbEngine, _serviceProvider.GetService<ILogger<HardwareHubProvider>>());
            deviceManager.RegisterProvider(hubProvider);

            // Register OpenRGB Universal Hardware Auto-Scanner
            var openRgbScanner = new OpenRgbScanner(rgbEngine, _serviceProvider.GetService<ILogger<OpenRgbScanner>>());
            deviceManager.RegisterProvider(openRgbScanner);

            // Register OpenRGB SDK Device Provider (if OpenRGB server is running on 127.0.0.1:6742)
            var openRgbDeviceProvider = new OpenRgbDeviceProvider(rgbEngine, _serviceProvider.GetService<ILogger<OpenRgbDeviceProvider>>());
            deviceManager.RegisterProvider(openRgbDeviceProvider);

            deviceManager.RegisterProvider(new GenericWindowsDeviceProvider());
            await deviceManager.DiscoverAllAsync();

            // Start Hardware Telemetry & RGB Animation Loops
            var hardwareEngine = _serviceProvider.GetRequiredService<HardwareEngine>();
            var audioEngine = _serviceProvider.GetRequiredService<AudioEngine>();
            await hardwareEngine.StartAsync();

            rgbEngine.AudioPeakLevelProvider = () => audioEngine.DefaultPlaybackDevice?.PeakLevel ?? 0f;
            rgbEngine.Start();

            // Immediately dispatch saved RGB color, effect, and LCD settings to physical hardware
            var rgbStudioVm = _serviceProvider.GetRequiredService<RgbStudioViewModel>();
            rgbStudioVm.PushCurrentStateToHardware();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            mainWindow.Show();

            var trayService = _serviceProvider.GetRequiredService<NexusTrayService>();
            mainWindow.AttachTrayService(trayService);

            var miniHud = _serviceProvider.GetRequiredService<Views.MiniHudWindow>();
            mainWindow.SetMiniHud(miniHud);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "NEXUS fatal startup exception.");
            MessageBox.Show($"NEXUS Startup Exception: {ex.Message}", "NEXUS Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            var hardwareEngine = _serviceProvider.GetService<HardwareEngine>();
            if (hardwareEngine != null) await hardwareEngine.StopAsync();

            var rgbEngine = _serviceProvider.GetService<RgbEngine>();
            rgbEngine?.Stop();

            var fanEngine = _serviceProvider.GetService<FanControlEngine>();
            if (fanEngine != null) await fanEngine.ResetAllToSafeDefaultsAsync();

            Nexus.Integrations.OpenRGB.OpenRgbClient.StopOpenRgbServer();

            await _serviceProvider.DisposeAsync();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
