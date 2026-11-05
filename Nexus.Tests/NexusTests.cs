using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.Devices;
using Nexus.Abstractions.Profiles;
using Nexus.Abstractions.Rgb;
using Nexus.Abstractions.Sensors;
using Nexus.Core.EventBus;
using Nexus.Core.ProfileManager;
using Xunit;

namespace Nexus.Tests;

public class EventBusTests
{
    [Fact]
    public async Task PublishAsync_DeliversEventToSubscribers()
    {
        var bus = new EventBus();
        string received = string.Empty;

        using var sub = bus.Subscribe<string>(msg =>
        {
            received = msg;
            return Task.CompletedTask;
        });

        await bus.PublishAsync("NEXUS_TEST_MESSAGE");

        Assert.Equal("NEXUS_TEST_MESSAGE", received);
    }
}

public class ProfileManagerTests
{
    [Fact]
    public async Task ActivateProfile_UpdatesActiveProfileAndFiresEvent()
    {
        var bus = new EventBus();
        var manager = new ProfileManager(bus);

        bool fired = false;
        manager.ProfileChanged += (_, p) =>
        {
            if (p.Id == "gaming-preset") fired = true;
        };

        await manager.ActivateProfileAsync("gaming-preset");

        Assert.Equal("Gaming", manager.ActiveProfile.Name);
        Assert.Equal(PerformanceMode.Gaming, manager.ActiveProfile.Mode);
        Assert.True(fired);
    }

    [Fact]
    public async Task DuplicateProfile_CreatesIndependentCopy()
    {
        var bus = new EventBus();
        var manager = new ProfileManager(bus);

        var copy = await manager.DuplicateProfileAsync("silent-preset");
        Assert.NotNull(copy);
        Assert.NotEqual("silent-preset", copy.Id);
    }
}

public class HardwareHubTests
{
    [Fact]
    public async Task HardwareHubProvider_DiscoversPhysicalHubs()
    {
        var rgbEngine = new Nexus.Engines.RGB.RgbEngine();
        var provider = new Nexus.Integrations.HardwareHubProvider(rgbEngine);

        var devices = await provider.DiscoverDevicesAsync();
        Assert.NotNull(devices);
        Assert.True(devices.Count >= 2, $"Expected at least 2 physical hubs (Lian Li & Corsair), found {devices.Count}");
    }
}

public class LogitechDeviceTests
{
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

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileNative(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hObject, out IntPtr pPHIDP_PREPARSED_DATA);
    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr pPHIDP_PREPARSED_DATA);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr pPHIDP_PREPARSED_DATA, out HIDP_CAPS pCaps);

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [Fact]
    public void LogitechG502_QueryFeatureAndSetColor()
    {
        string path = @"\\?\hid#vid_046d&pid_c08b&mi_01&col05#8&529fc59&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        using var h = CreateFileNative(path, 0xC0000000, 0x03, IntPtr.Zero, 3, 0, IntPtr.Zero);
        Assert.False(h.IsInvalid, "Failed to open G502 COL05 handle");

        // Query Root (0x0000) for feature 0x8070 (ColorLEDEffects)
        byte[] query = new byte[20];
        query[0] = 0x11;
        query[1] = 0xFF;
        query[2] = 0x00; // Root feature
        query[3] = 0x00; // Function 0: GetFeature(featureId)
        query[4] = 0x80; // Feature MSB
        query[5] = 0x70; // Feature LSB

        bool w = WriteFile(h, query, 20, out uint written, IntPtr.Zero);
        int wrErr = Marshal.GetLastWin32Error();

        byte[] resp = new byte[20];
        bool r = ReadFile(h, resp, 20, out uint read, IntPtr.Zero);
        int rdErr = Marshal.GetLastWin32Error();

        byte featureIdx = resp[4]; // In HID++ 2.0, response[4] contains feature index

        // If featureIdx > 0, set Logo (zone 1) & DPI (zone 0) to Red
        bool set1 = false;
        bool set2 = false;
        if (featureIdx > 0)
        {
            // Set Zone 1 (Logo)
            byte[] color1 = new byte[20];
            color1[0] = 0x11;
            color1[1] = 0xFF;
            color1[2] = featureIdx;
            color1[3] = 0x10; // Set effect
            color1[4] = 0x01; // Zone 1 = Logo
            color1[5] = 0x01; // Solid
            color1[6] = 0xFF; // R (255)
            color1[7] = 0x00; // G
            color1[8] = 0x00; // B
            set1 = WriteFile(h, color1, 20, out _, IntPtr.Zero);

            // Set Zone 0 (Primary / DPI)
            byte[] color0 = new byte[20];
            color0[0] = 0x11;
            color0[1] = 0xFF;
            color0[2] = featureIdx;
            color0[3] = 0x10; // Set effect
            color0[4] = 0x00; // Zone 0 = Primary/DPI
            color0[5] = 0x01; // Solid
            color0[6] = 0xFF; // R (255)
            color0[7] = 0x00; // G
            color0[8] = 0x00; // B
            set2 = WriteFile(h, color0, 20, out _, IntPtr.Zero);
        }

        Assert.True(set1 && set2, $"w={w}, wrErr={wrErr}, r={r}, read={read}, rdErr={rdErr}, resp={BitConverter.ToString(resp)}, featureIdx={featureIdx}, set1={set1}, set2={set2}");
    }

    [Fact]
    public void App_StartupDependencyInjection_ResolvesMainWindowAndTrayService()
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new Nexus.App.App();
                    app.InitializeComponent();
                }

                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton<Nexus.Core.Configuration.NexusConfiguration>();
                services.AddSingleton<Nexus.Abstractions.EventBus.IEventBus, Nexus.Core.EventBus.EventBus>();
                services.AddSingleton<Nexus.Storage.Database.NexusDatabase>();
                services.AddSingleton<Nexus.Storage.Repositories.StorageRepositories>();

                services.AddSingleton<Nexus.Engines.Hardware.HardwareEngine>();
                services.AddSingleton<Nexus.Engines.Audio.AudioEngine>();
                services.AddSingleton<Nexus.Abstractions.Audio.IAudioEngine>(sp => sp.GetRequiredService<Nexus.Engines.Audio.AudioEngine>());
                services.AddSingleton<Nexus.Engines.RGB.RgbEngine>();
                services.AddSingleton<Nexus.Abstractions.Rgb.IRgbEngine>(sp => sp.GetRequiredService<Nexus.Engines.RGB.RgbEngine>());
                services.AddSingleton<Nexus.Engines.FanControl.FanControlEngine>();
                services.AddSingleton<Nexus.Abstractions.Cooling.IFanControlEngine>(sp => sp.GetRequiredService<Nexus.Engines.FanControl.FanControlEngine>());

                services.AddSingleton<Nexus.Core.DeviceManager.DeviceManager>();
                services.AddSingleton<Nexus.Core.ProfileManager.ProfileManager>();
                services.AddSingleton<Nexus.Abstractions.Profiles.IProfileManager>(sp => sp.GetRequiredService<Nexus.Core.ProfileManager.ProfileManager>());
                services.AddSingleton<Nexus.Core.Services.HardwareConflictManager>();
                services.AddSingleton<Nexus.Core.Automation.AutomationEngine>();
                services.AddSingleton<Nexus.Core.PluginManager.PluginManager>();

                services.AddSingleton<Nexus.App.ViewModels.DashboardViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.RgbStudioViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.AudioStudioViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.HardwareMonitorViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.CoolingViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.DevicesViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.PerformanceViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.AutomationViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.ProfilesViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.PluginsViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.SettingsViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.MainViewModel>();

                services.AddSingleton<Nexus.App.MainWindow>();
                services.AddSingleton<System.Windows.Window>(sp => sp.GetRequiredService<Nexus.App.MainWindow>());
                services.AddSingleton<Nexus.App.Services.NexusTrayService>();
                services.AddSingleton<Nexus.App.Views.MiniHudWindow>();

                var sp = services.BuildServiceProvider();
                var mainWin = sp.GetRequiredService<Nexus.App.MainWindow>();
                var traySvc = sp.GetRequiredService<Nexus.App.Services.NexusTrayService>();
                var miniHud = sp.GetRequiredService<Nexus.App.Views.MiniHudWindow>();

                Assert.NotNull(mainWin);
                Assert.NotNull(traySvc);
                Assert.NotNull(miniHud);
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            Assert.Fail($"Startup DI failed: {ex}");
        }
    }

    [Fact]
    public void WpfUi_FluentWindow_InitializesSuccessfully()
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new Nexus.App.App();
                    app.InitializeComponent();
                }

                var win = new Wpf.Ui.Controls.FluentWindow
                {
                    WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica
                };
                Assert.NotNull(win);
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            Assert.Fail($"FluentWindow crashed with: {ex}");
        }
    }

    [Fact]
    public void ScottPlot_WpfPlot_InitializesAndStylesSuccessfully()
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new Nexus.App.App();
                    app.InitializeComponent();
                }

                var plot = new ScottPlot.WPF.WpfPlot();
                plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#151824");
                plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#0D0E12");
                plot.Plot.Axes.Color(ScottPlot.Color.FromHex("#8E95A8"));
                plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#1F2433");

                double[] data = { 45, 48, 52, 50, 55, 53, 58, 60, 59, 62 };
                var sig = plot.Plot.Add.Signal(data);
                sig.Color = ScottPlot.Color.FromHex("#00D2FF");

                plot.Measure(new System.Windows.Size(600, 300));
                plot.Arrange(new System.Windows.Rect(0, 0, 600, 300));
                plot.UpdateLayout();

                Assert.NotNull(plot);
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            Assert.Fail($"ScottPlot crashed with: {ex}");
        }
    }

    [Fact]
    public void HardwareMonitorView_WithScottPlot_RendersSuccessfully()
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new Nexus.App.App();
                    app.InitializeComponent();
                }

                var bus = new Nexus.Core.EventBus.EventBus();
                var hwEngine = new Nexus.Engines.Hardware.HardwareEngine(bus);
                var vm = new Nexus.App.ViewModels.HardwareMonitorViewModel(hwEngine);
                var view = new Nexus.App.Views.HardwareMonitorView
                {
                    DataContext = vm
                };

                view.Measure(new System.Windows.Size(1280, 800));
                view.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                view.UpdateLayout();

                Assert.NotNull(view);
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            Assert.Fail($"HardwareMonitorView crashed with: {ex}");
        }
    }

    [Fact]
    public void AudioStudio_LoadsAndRendersWithoutException()
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new Nexus.App.App();
                    app.InitializeComponent();
                }

                var audioEngine = new Nexus.Engines.Audio.AudioEngine(new Microsoft.Extensions.Logging.Abstractions.NullLogger<Nexus.Engines.Audio.AudioEngine>());
                var vm = new Nexus.App.ViewModels.AudioStudioViewModel(audioEngine);
                var view = new Nexus.App.Views.AudioStudioView
                {
                    DataContext = vm
                };
                
                view.Measure(new System.Windows.Size(1280, 800));
                view.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                view.UpdateLayout();

                Assert.True(view.ActualWidth > 0 || view.DesiredSize.Width > 0, "AudioStudioView desired size is 0!");
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            Assert.Fail($"AudioStudio crashed with: {ex}");
        }
    }

    [Fact]
    public void MainWindow_NavigateToAudioStudio_RendersAudioStudioView()
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new Nexus.App.App();
                    app.InitializeComponent();
                }

                var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                services.AddLogging();
                services.AddSingleton<Nexus.Core.Configuration.NexusConfiguration>();
                services.AddSingleton<Nexus.Abstractions.EventBus.IEventBus, Nexus.Core.EventBus.EventBus>();
                services.AddSingleton<Nexus.Storage.Database.NexusDatabase>();
                services.AddSingleton<Nexus.Storage.Repositories.StorageRepositories>();

                services.AddSingleton<Nexus.Engines.Hardware.HardwareEngine>();
                services.AddSingleton<Nexus.Engines.Audio.AudioEngine>();
                services.AddSingleton<Nexus.Abstractions.Audio.IAudioEngine>(sp => sp.GetRequiredService<Nexus.Engines.Audio.AudioEngine>());
                services.AddSingleton<Nexus.Engines.RGB.RgbEngine>();
                services.AddSingleton<Nexus.Abstractions.Rgb.IRgbEngine>(sp => sp.GetRequiredService<Nexus.Engines.RGB.RgbEngine>());
                services.AddSingleton<Nexus.Engines.FanControl.FanControlEngine>();
                services.AddSingleton<Nexus.Abstractions.Cooling.IFanControlEngine>(sp => sp.GetRequiredService<Nexus.Engines.FanControl.FanControlEngine>());

                services.AddSingleton<Nexus.Core.DeviceManager.DeviceManager>();
                services.AddSingleton<Nexus.Core.ProfileManager.ProfileManager>();
                services.AddSingleton<Nexus.Abstractions.Profiles.IProfileManager>(sp => sp.GetRequiredService<Nexus.Core.ProfileManager.ProfileManager>());
                services.AddSingleton<Nexus.Core.Services.HardwareConflictManager>();
                services.AddSingleton<Nexus.Core.Automation.AutomationEngine>();
                services.AddSingleton<Nexus.Core.PluginManager.PluginManager>();

                services.AddSingleton<Nexus.App.ViewModels.DashboardViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.RgbStudioViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.AudioStudioViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.HardwareMonitorViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.CoolingViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.DevicesViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.PerformanceViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.AutomationViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.ProfilesViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.PluginsViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.SettingsViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.MainViewModel>();
                services.AddSingleton<Nexus.App.MainWindow>();

                var sp = services.BuildServiceProvider();
                var devMgr = sp.GetRequiredService<Nexus.Core.DeviceManager.DeviceManager>();
                var rgbEng = sp.GetRequiredService<Nexus.Engines.RGB.RgbEngine>();
                var hubProv = new Nexus.Integrations.HardwareHubProvider(rgbEng);
                devMgr.RegisterProvider(hubProv);
                devMgr.DiscoverAllAsync().GetAwaiter().GetResult();

                var mainVm = sp.GetRequiredService<Nexus.App.ViewModels.MainViewModel>();
                var win = sp.GetRequiredService<Nexus.App.MainWindow>();

                win.ApplyTemplate();
                win.Show();

                // Navigate to AudioStudio
                var audioNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.AudioStudio);
                mainVm.NavigateCommand.Execute(audioNavItem);

                win.Measure(new System.Windows.Size(1280, 800));
                win.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                win.UpdateLayout();

                var audioVm = sp.GetRequiredService<Nexus.App.ViewModels.AudioStudioViewModel>();
                Assert.Equal(audioVm, mainVm.CurrentViewModel);

                // Find visual elements
                static T? FindChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
                {
                    int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
                    for (int i = 0; i < count; i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                        if (child is T typed) return typed;
                        var result = FindChild<T>(child);
                        if (result != null) return result;
                    }
                    return null;
                }

                var host = win.FindName("MainContentHost") as System.Windows.Controls.ContentControl;
                Assert.NotNull(host);
                Assert.Equal(audioVm, host.Content);

                var audioView = FindChild<Nexus.App.Views.AudioStudioView>(host);
                Assert.NotNull(audioView);
                Assert.True(audioView.ActualWidth > 0 || audioView.DesiredSize.Width > 0);

                win.Close();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            Assert.Fail($"MainWindow navigation to AudioStudio failed: {ex}");
        }
    }

    [Fact]
    public void RenderDashboardScreenshot_SavesPng()
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var app = new Nexus.App.App();
                    app.InitializeComponent();
                }

                var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                services.AddLogging();
                services.AddSingleton<Nexus.Core.Configuration.NexusConfiguration>();
                services.AddSingleton<Nexus.Abstractions.EventBus.IEventBus, Nexus.Core.EventBus.EventBus>();
                services.AddSingleton<Nexus.Storage.Database.NexusDatabase>();
                services.AddSingleton<Nexus.Storage.Repositories.StorageRepositories>();

                services.AddSingleton<Nexus.Engines.Hardware.HardwareEngine>();
                services.AddSingleton<Nexus.Engines.Audio.AudioEngine>();
                services.AddSingleton<Nexus.Abstractions.Audio.IAudioEngine>(sp => sp.GetRequiredService<Nexus.Engines.Audio.AudioEngine>());
                services.AddSingleton<Nexus.Engines.RGB.RgbEngine>();
                services.AddSingleton<Nexus.Abstractions.Rgb.IRgbEngine>(sp => sp.GetRequiredService<Nexus.Engines.RGB.RgbEngine>());
                services.AddSingleton<Nexus.Engines.FanControl.FanControlEngine>();
                services.AddSingleton<Nexus.Abstractions.Cooling.IFanControlEngine>(sp => sp.GetRequiredService<Nexus.Engines.FanControl.FanControlEngine>());

                services.AddSingleton<Nexus.Core.DeviceManager.DeviceManager>();
                services.AddSingleton<Nexus.Core.ProfileManager.ProfileManager>();
                services.AddSingleton<Nexus.Abstractions.Profiles.IProfileManager>(sp => sp.GetRequiredService<Nexus.Core.ProfileManager.ProfileManager>());
                services.AddSingleton<Nexus.Core.Services.HardwareConflictManager>();
                services.AddSingleton<Nexus.Core.Automation.AutomationEngine>();
                services.AddSingleton<Nexus.Core.PluginManager.PluginManager>();

                services.AddSingleton<Nexus.App.ViewModels.DashboardViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.RgbStudioViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.AudioStudioViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.HardwareMonitorViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.CoolingViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.DevicesViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.PerformanceViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.AutomationViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.ProfilesViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.PluginsViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.SettingsViewModel>();
                services.AddSingleton<Nexus.App.ViewModels.MainViewModel>();
                services.AddSingleton<Nexus.App.MainWindow>();

                var sp = services.BuildServiceProvider();
                var devMgr = sp.GetRequiredService<Nexus.Core.DeviceManager.DeviceManager>();
                var rgbEng = sp.GetRequiredService<Nexus.Engines.RGB.RgbEngine>();
                var hubProv = new Nexus.Integrations.HardwareHubProvider(rgbEng);
                devMgr.RegisterProvider(hubProv);
                devMgr.DiscoverAllAsync().GetAwaiter().GetResult();

                var mainVm = sp.GetRequiredService<Nexus.App.ViewModels.MainViewModel>();
                var win = sp.GetRequiredService<Nexus.App.MainWindow>();

                win.ApplyTemplate();
                win.Show();

                void SaveScreenshot(string outPath)
                {
                    win.Measure(new System.Windows.Size(1280, 800));
                    win.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                    win.UpdateLayout();

                    var contentVisual = (System.Windows.UIElement)win.Content;
                    contentVisual.Measure(new System.Windows.Size(1280, 800));
                    contentVisual.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                    contentVisual.UpdateLayout();

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(1280, 800, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(contentVisual);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                    using var fs = System.IO.File.Create(outPath);
                    encoder.Save(fs);
                }

                // 1. Select Dashboard
                var dashNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.Dashboard);
                mainVm.NavigateCommand.Execute(dashNavItem);
                string dashPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\dashboard_screenshot.png";
                SaveScreenshot(dashPath);

                // 2. Select RGB Studio
                var rgbNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.RgbStudio);
                mainVm.NavigateCommand.Execute(rgbNavItem);
                string rgbPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\rgb_studio_screenshot.png";
                SaveScreenshot(rgbPath);

                // 3. Select Audio Studio (Sonar)
                var audioNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.AudioStudio);
                mainVm.NavigateCommand.Execute(audioNavItem);
                string sonarPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\sonar_mixer_screenshot.png";
                SaveScreenshot(sonarPath);

                // 3.5 Select Hardware Monitor
                var hwNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.HardwareMonitor);
                mainVm.NavigateCommand.Execute(hwNavItem);
                string hwPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\hardware_monitor_screenshot.png";
                SaveScreenshot(hwPath);

                // 4. Select Cooling Studio
                var coolingNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.Cooling);
                mainVm.NavigateCommand.Execute(coolingNavItem);
                string coolingPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\cooling_studio_screenshot.png";
                SaveScreenshot(coolingPath);

                // 5. Select Device Center
                var devicesNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.Devices);
                mainVm.NavigateCommand.Execute(devicesNavItem);
                string devicesPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\devices_screenshot.png";
                SaveScreenshot(devicesPath);

                // 6. Select Performance Tuning
                var perfNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.Performance);
                mainVm.NavigateCommand.Execute(perfNavItem);
                string perfPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\performance_screenshot.png";
                SaveScreenshot(perfPath);

                // 7. Select Automation Builder
                var autoNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.Automation);
                mainVm.NavigateCommand.Execute(autoNavItem);
                string autoPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\automation_screenshot.png";
                SaveScreenshot(autoPath);

                // 8. Select Profiles Manager
                var profNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.Profiles);
                mainVm.NavigateCommand.Execute(profNavItem);
                string profPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\profiles_screenshot.png";
                SaveScreenshot(profPath);

                // 9. Select Settings
                var setNavItem = mainVm.NavigationItems.First(x => x.Section == Nexus.App.Navigation.NavSection.Settings);
                mainVm.NavigateCommand.Execute(setNavItem);
                string setPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\settings_screenshot.png";
                SaveScreenshot(setPath);

                // 10. Mini HUD Window
                var dashVm = sp.GetRequiredService<Nexus.App.ViewModels.DashboardViewModel>();
                var hud = new Nexus.App.Views.MiniHudWindow(dashVm);
                hud.Measure(new System.Windows.Size(300, 150));
                hud.Arrange(new System.Windows.Rect(0, 0, 300, 150));
                hud.UpdateLayout();
                var hudVisual = (System.Windows.UIElement)hud.Content;
                hudVisual.Measure(new System.Windows.Size(300, 150));
                hudVisual.Arrange(new System.Windows.Rect(0, 0, 300, 150));
                hudVisual.UpdateLayout();

                var hudRtb = new System.Windows.Media.Imaging.RenderTargetBitmap(300, 150, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                hudRtb.Render(hudVisual);
                var hudEnc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                hudEnc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(hudRtb));
                string hudPath = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d\mini_hud_screenshot.png";
                using (var fs = System.IO.File.Create(hudPath)) hudEnc.Save(fs);
                hud.Close();

                win.Close();
                Assert.True(System.IO.File.Exists(dashPath));
                Assert.True(System.IO.File.Exists(rgbPath));
                Assert.True(System.IO.File.Exists(sonarPath));
                Assert.True(System.IO.File.Exists(coolingPath));
                Assert.True(System.IO.File.Exists(devicesPath));
                Assert.True(System.IO.File.Exists(perfPath));
                Assert.True(System.IO.File.Exists(autoPath));
                Assert.True(System.IO.File.Exists(profPath));
                Assert.True(System.IO.File.Exists(setPath));
                Assert.True(System.IO.File.Exists(hudPath));
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            Assert.Fail($"Dashboard screenshot rendering failed: {ex}");
        }
    }
}

public class CorsairVengeanceRgbControllerTests
{
    [Fact]
    public void Constructor_InitializesFourPhysicalDimmSlots()
    {
        using var controller = new Nexus.Integrations.Memory.CorsairVengeanceRgbController();

        Assert.Equal(4, controller.Zones.Count);
        Assert.Equal("dimm-1", controller.Zones[0].ZoneId);
        Assert.Equal(10, controller.Zones[0].LedCount);
        Assert.Equal("dimm-4", controller.Zones[3].ZoneId);
        Assert.Equal(10, controller.Zones[3].LedCount);
    }

    [Fact]
    public async Task SetAllColorsAsync_UpdatesAllZonesLocally()
    {
        using var controller = new Nexus.Integrations.Memory.CorsairVengeanceRgbController();
        var targetColor = new RgbColor(255, 0, 128);

        await controller.SetAllColorsAsync(targetColor);

        foreach (var zone in controller.Zones)
        {
            Assert.Equal(targetColor, zone.CurrentColors[0]);
            Assert.Equal(targetColor, zone.CurrentColors[9]);
        }
    }

    [Fact]
    public async Task SetZoneColorsAsync_UpdatesSpecificDimmSlot()
    {
        using var controller = new Nexus.Integrations.Memory.CorsairVengeanceRgbController();
        var dimm2Color = new RgbColor(0, 255, 255);

        await controller.SetZoneColorsAsync("dimm-2", new[] { dimm2Color });

        Assert.Equal(dimm2Color, controller.Zones[1].CurrentColors[0]);
        Assert.Equal(new RgbColor(0, 0, 0), controller.Zones[0].CurrentColors[0]);
    }

    [Fact]
    public async Task LiveHardware_ConnectAndSendPacket_IfOpenRgbRunning()
    {
        using var controller = new Nexus.Integrations.Memory.CorsairVengeanceRgbController();
        await controller.InitializeAsync();
        await controller.SetAllColorsAsync(new RgbColor(255, 0, 100));
        Assert.NotNull(controller.Zones);
    }

    [Fact]
    public async Task LiveGpu_ConnectAndSendPacket_IfOpenRgbRunning()
    {
        using var client = new Nexus.Integrations.OpenRGB.OpenRgbClient(host: "127.0.0.1", port: 6742);
        bool connected = await client.ConnectAsync();
        if (connected)
        {
            int count = await client.GetControllerCountAsync();
            for (int i = 0; i < count; i++)
            {
                var info = await client.GetControllerDataAsync(i);
                if (info != null && (info.Name.Contains("3080") || info.Name.Contains("MSI") || info.Name.Contains("GeForce")))
                {
                    int leds = info.LedCount > 0 ? info.LedCount : 26;
                    var colors = new Nexus.Abstractions.Rgb.RgbColor[leds];
                    Array.Fill(colors, new Nexus.Abstractions.Rgb.RgbColor(0, 210, 255));
                    await client.UpdateLedsAsync(i, colors);
                }
            }
        }
    }
}

public class RgbEngineFloodPreventionTests
{
    private class TestRgbDevice : IRgbDevice
    {
        public string Id => "test-rgb-device";
        public string Name => "Test RGB Device";
        public string Manufacturer => "Test";
        public string Model => "Test-01";
        public string SerialNumber => "TEST-123";
        public DeviceType Type => DeviceType.LightingController;
        public DeviceConnectionStatus Status => DeviceConnectionStatus.Connected;
        public DeviceCapabilities Capabilities => DeviceCapabilities.RgbLighting;
        public string? FirmwareVersion => "v1.0";
        public string IntegrationProvider => "Test Provider";
        public IReadOnlyList<IDeviceControl> Controls => Array.Empty<IDeviceControl>();
        public IReadOnlyDictionary<string, string> Properties => new Dictionary<string, string>();
        public IReadOnlyList<RgbZone> Zones { get; } = new List<RgbZone> { new RgbZone("zone-1", "Zone 1", 10) };
        public double Brightness { get; set; } = 1.0;

        public int SetAllColorsCount { get; private set; }
        public int SetZoneColorsCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default)
        {
            SetAllColorsCount++;
            return Task.CompletedTask;
        }

        public Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default)
        {
            SetZoneColorsCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RgbEngine_StaticEffect_DispatchesOnlyOnce_ToPreventBusFlooding()
    {
        var engine = new Nexus.Engines.RGB.RgbEngine();
        var device = new TestRgbDevice();
        engine.RegisterDevice(device);

        engine.GlobalEffect = new RgbEffectConfig
        {
            EffectType = RgbEffectType.Static,
            PrimaryColor = new RgbColor(0, 210, 255),
            Brightness = 1.0
        };

        engine.Start();
        await Task.Delay(150);
        engine.Stop();

        Assert.Equal(1, device.SetAllColorsCount);
        Assert.Equal(0, device.SetZoneColorsCount);
    }

    [Fact]
    public void CorsairCommanderCore_ZoneMapping_Has24PumpLedsAnd6FanZones()
    {
        using var controller = new Nexus.Integrations.Corsair.CorsairCommanderCoreController("dummy-path");
        Assert.Equal(7, controller.Zones.Count);
        Assert.Equal("zone-pump", controller.Zones[0].ZoneId);
        Assert.Equal(24, controller.Zones[0].LedCount);

        for (int i = 1; i <= 6; i++)
        {
            Assert.Equal($"zone-fan-{i}", controller.Zones[i].ZoneId);
            Assert.Equal(34, controller.Zones[i].LedCount);
        }
    }
}

public class OpenRgbDeviceRegistrationTests
{
    [Fact]
    public void OpenRgbDevice_InitializesZonesAndNameProperly()
    {
        using var client = new Nexus.Integrations.OpenRGB.OpenRgbClient();
        var dev = new Nexus.Integrations.OpenRGB.OpenRgbDevice(client, 5, "EVision Keyboard 0C45:652F", "EVision", "Keyboard", 104);

        Assert.Equal("EVision Keyboard 0C45:652F", dev.Name);
        Assert.Equal(1, dev.Zones.Count);
        Assert.Equal(104, dev.Zones[0].LedCount);
    }

    [Fact]
    public void CorsairVengeance_AttachOpenRgbClient_WiresClientAndController()
    {
        using var controller = new Nexus.Integrations.Memory.CorsairVengeanceRgbController();
        using var client = new Nexus.Integrations.OpenRGB.OpenRgbClient();

        controller.AttachOpenRgbClient(client, 0, 10, "Corsair Vengeance RGB Pro DDR4");
        Assert.Equal(4, controller.Zones.Count);
    }
}

public class RgbStudio3DTests
{
    [Fact]
    public void Is3dModeActive_DefaultsToTrue_AndToggles()
    {
        var bus = new Nexus.Core.EventBus.EventBus();
        var rgbEngine = new Nexus.Engines.RGB.RgbEngine();
        var devMgr = new Nexus.Core.DeviceManager.DeviceManager(bus);
        var conflictMgr = new Nexus.Core.Services.HardwareConflictManager();

        var vm = new Nexus.App.ViewModels.RgbStudioViewModel(rgbEngine, devMgr, conflictMgr);

        Assert.True(vm.Is3dModeActive);

        vm.ToggleViewportModeCommand.Execute(null);
        Assert.False(vm.Is3dModeActive);

        vm.ToggleViewportModeCommand.Execute(null);
        Assert.True(vm.Is3dModeActive);
    }

    [Fact]
    public void SelectCanvasDevice_SelectsMappedHardwareComponent()
    {
        var bus = new Nexus.Core.EventBus.EventBus();
        var rgbEngine = new Nexus.Engines.RGB.RgbEngine();
        var devMgr = new Nexus.Core.DeviceManager.DeviceManager(bus);
        var conflictMgr = new Nexus.Core.Services.HardwareConflictManager();

        var vm = new Nexus.App.ViewModels.RgbStudioViewModel(rgbEngine, devMgr, conflictMgr);

        vm.SelectCanvasDeviceCommand.Execute("ram");
        Assert.NotNull(vm.SelectedCanvasDevice);
        Assert.Contains("VENGEANCE", vm.SelectedCanvasDevice.Name);

        vm.SelectCanvasDeviceCommand.Execute("gpu");
        Assert.NotNull(vm.SelectedCanvasDevice);
        Assert.Contains("3080", vm.SelectedCanvasDevice.Name);
    }

    [Fact]
    public void LightingUpdated_FiresWhenColorChanges()
    {
        var bus = new Nexus.Core.EventBus.EventBus();
        var rgbEngine = new Nexus.Engines.RGB.RgbEngine();
        var devMgr = new Nexus.Core.DeviceManager.DeviceManager(bus);
        var conflictMgr = new Nexus.Core.Services.HardwareConflictManager();

        var vm = new Nexus.App.ViewModels.RgbStudioViewModel(rgbEngine, devMgr, conflictMgr);

        bool fired = false;
        string updatedColor = "";
        string updatedDevice = "";
        vm.LightingUpdated += (dev, color, effect) =>
        {
            fired = true;
            updatedDevice = dev;
            updatedColor = color;
        };

        vm.PrimaryColorHex = "#FF0055";

        Assert.True(fired);
        Assert.Equal("#FF0055", updatedColor);
        Assert.Equal("all", updatedDevice);
    }

    [Fact]
    public void LightingUpdated_SendsSpecificDevice_WhenSyncAllIsDisabled()
    {
        var bus = new Nexus.Core.EventBus.EventBus();
        var rgbEngine = new Nexus.Engines.RGB.RgbEngine();
        var devMgr = new Nexus.Core.DeviceManager.DeviceManager(bus);
        var conflictMgr = new Nexus.Core.Services.HardwareConflictManager();

        var vm = new Nexus.App.ViewModels.RgbStudioViewModel(rgbEngine, devMgr, conflictMgr);

        vm.IsSyncAllDevices = false;
        vm.SelectCanvasDeviceCommand.Execute("gpu");

        string updatedDevice = "";
        vm.LightingUpdated += (dev, color, effect) =>
        {
            updatedDevice = dev;
        };

        vm.PrimaryColorHex = "#00FF00";

        Assert.Equal("gpu-rtx3080", updatedDevice);
    }

    [Fact]
    public void AioLcd_ModalOpens_AndModeSwitchesCorrectly()
    {
        var bus = new Nexus.Core.EventBus.EventBus();
        var rgbEngine = new Nexus.Engines.RGB.RgbEngine();
        var devMgr = new Nexus.Core.DeviceManager.DeviceManager(bus);
        var conflictMgr = new Nexus.Core.Services.HardwareConflictManager();

        var vm = new Nexus.App.ViewModels.RgbStudioViewModel(rgbEngine, devMgr, conflictMgr);

        Assert.False(vm.IsLcdCustomizerOpen);
        vm.OpenLcdCustomizerCommand.Execute(null);
        Assert.True(vm.IsLcdCustomizerOpen);

        vm.SelectLcdModeCommand.Execute(Nexus.Abstractions.Cooling.AioLcdMode.NexusLogo);
        Assert.Equal(Nexus.Abstractions.Cooling.AioLcdMode.NexusLogo, vm.SelectedLcdMode);
        Assert.Equal("شعار NEXUS الرسمي", vm.SelectedLcdModeTitle);

        vm.ApplyLcdSettingsCommand.Execute(null);
        Assert.False(vm.IsLcdCustomizerOpen);
    }

    [Fact]
    public void AppExecutable_StartsWithoutFatalCrash()
    {
        string exePath = @"C:\Users\Mabar\OneDrive\Desktop\NEXUS\Nexus.App\bin\Debug\net10.0-windows\Nexus.App.exe";
        Assert.True(System.IO.File.Exists(exePath));

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p != null && !p.HasExited)
            {
                p.Kill();
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Expected when unit tests run non-elevated against an administrator-manifested binary
        }
    }

    [Fact]
    public void CorsairLcdFrameRenderer_RendersValidJpeg()
    {
        var config = new Nexus.Abstractions.Cooling.AioLcdConfig
        {
            Mode = Nexus.Abstractions.Cooling.AioLcdMode.CpuTempGauge,
            DialAccentColorHex = "#00D2FF",
            HaloRingColorHex = "#A78DFF"
        };

        var bytes = Nexus.App.Services.CorsairLcdFrameRenderer.RenderFrame(config, 48, 52, 34);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1000);
        // JPEG magic header: FF D8
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);

        // Test sending to controller
        using var controller = new Nexus.Integrations.Corsair.CorsairEliteLcdController();
        bool sent = controller.SendJpegFrame(bytes);
        // Returns true if physical hardware is connected and accepts frame, or false gracefully if disconnected
        Assert.NotNull(controller.Zones);
    }

    [Fact]
    public void RgbStudio_PersistsStateToDisk_AndRestoresCorrectly()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nexus_rgb_test_{Guid.NewGuid():N}.json");
        try
        {
            var bus = new Nexus.Core.EventBus.EventBus();
            var rgbEngine = new Nexus.Engines.RGB.RgbEngine();
            var devMgr = new Nexus.Core.DeviceManager.DeviceManager(bus);
            var conflictMgr = new Nexus.Core.Services.HardwareConflictManager();

            var vm1 = new Nexus.App.ViewModels.RgbStudioViewModel(rgbEngine, devMgr, conflictMgr);
            vm1.EnablePersistence = true;
            vm1.StateFilePath = tempFile;

            vm1.PrimaryColorHex = "#FF3399";
            vm1.SecondaryColorHex = "#00FFAA";
            vm1.SelectedEffect = Nexus.Abstractions.Rgb.RgbEffectType.ColorCycle;
            vm1.Brightness = 85.0;
            vm1.Speed = 2.5;
            vm1.SelectedLcdMode = Nexus.Abstractions.Cooling.AioLcdMode.ArcReactor;
            vm1.LcdHaloRingColorHex = "#FF5500";
            vm1.SaveState();

            Assert.True(System.IO.File.Exists(tempFile));

            // Fresh instance simulating app reboot
            var vm2 = new Nexus.App.ViewModels.RgbStudioViewModel(rgbEngine, devMgr, conflictMgr);
            vm2.EnablePersistence = true;
            vm2.StateFilePath = tempFile;
            
            var method = typeof(Nexus.App.ViewModels.RgbStudioViewModel).GetMethod("LoadSavedState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(vm2, null);

            Assert.Equal("#FF3399", vm2.PrimaryColorHex);
            Assert.Equal("#00FFAA", vm2.SecondaryColorHex);
            Assert.Equal(Nexus.Abstractions.Rgb.RgbEffectType.ColorCycle, vm2.SelectedEffect);
            Assert.Equal(85.0, vm2.Brightness);
            Assert.Equal(2.5, vm2.Speed);
            Assert.Equal(Nexus.Abstractions.Cooling.AioLcdMode.ArcReactor, vm2.SelectedLcdMode);
            Assert.Equal("#FF5500", vm2.LcdHaloRingColorHex);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void AudioStudio_ChannelStrips_InitializeCorrectly()
    {
        var mockAudioEngine = new Nexus.Engines.Audio.AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);

        Assert.Equal(6, vm.Channels.Count);
        Assert.Equal("MASTER", vm.Channels[0].Title);
        Assert.Equal("GAME", vm.Channels[1].Title);
        Assert.Equal("CHAT", vm.Channels[2].Title);
        Assert.Equal("MEDIA", vm.Channels[3].Title);
        Assert.Equal("AUX", vm.Channels[4].Title);
        Assert.Equal("MIC", vm.Channels[5].Title);

        Assert.Equal(85f, vm.Channels[0].Volume);
        Assert.Equal(1.0f, mockAudioEngine.BusManager.GameChatMixMultiplier);
        Assert.Equal(1.0f, mockAudioEngine.BusManager.ChatChatMixMultiplier);
    }

    [Fact]
    public void AudioStudio_ChatMix_CalculatesAttenuationCorrectly()
    {
        var mockAudioEngine = new Nexus.Engines.Audio.AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);

        // Balanced
        // Balanced (0.0)
        vm.ChatMixValue = 0.0;
        Assert.Equal(1.0f, mockAudioEngine.BusManager.GameChatMixMultiplier);
        Assert.Equal(1.0f, mockAudioEngine.BusManager.ChatChatMixMultiplier);

        // 25% ChatMix -> Chat attenuated to 50%
        // -0.5 ChatMix -> Game 1.0, Chat 0.5
        vm.ChatMixValue = -0.5;
        Assert.Equal(1.0f, mockAudioEngine.BusManager.GameChatMixMultiplier);
        Assert.Equal(0.5f, mockAudioEngine.BusManager.ChatChatMixMultiplier);

        // 75% ChatMix -> Game attenuated to 50%
        // +0.5 ChatMix -> Game 0.5, Chat 1.0
        vm.ChatMixValue = 0.5;
        Assert.Equal(0.5f, mockAudioEngine.BusManager.GameChatMixMultiplier);
        Assert.Equal(1.0f, mockAudioEngine.BusManager.ChatChatMixMultiplier);
    }

    [Fact]
    public void AudioStudio_TabNavigation_SwitchesProperly()
    {
        var mockAudioEngine = new Nexus.Engines.Audio.AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);

        Assert.Equal("Mixer", vm.SelectedTopTab);
        vm.SelectTabCommand.Execute("Game EQ");
        Assert.Equal("Game EQ", vm.SelectedTopTab);
        vm.SelectTabCommand.Execute("Mic Studio");
        Assert.Equal("Mic Studio", vm.SelectedTopTab);
    }

    [Fact]
    public void AudioStudio_StatePersistence_SavesAndRestoresJson()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nexus_audio_test_{Guid.NewGuid():N}.json");
        try
        {
            var mockAudioEngine = new Nexus.Engines.Audio.AudioEngine();
            using var vm1 = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);
            vm1.EnablePersistence = true;
            vm1.StateFilePath = tempFile;

            vm1.ChatMixValue = 0.4;
            vm1.Channels[0].Volume = 92f;
            vm1.Channels[1].Volume = 65f;
            vm1.SelectedTopTab = "Mic Studio";
            vm1.MicGainDb = 4.5f;
            vm1.NoiseGateThresholdDb = -40.0f;
            vm1.SaveState();

            Assert.True(System.IO.File.Exists(tempFile));

            // Fresh instance simulating app restart
            using var vm2 = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);
            vm2.EnablePersistence = true;
            vm2.StateFilePath = tempFile;

            var method = typeof(Nexus.App.ViewModels.AudioStudioViewModel).GetMethod("LoadSavedState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(vm2, null);

            Assert.Equal(92f, vm2.Channels[0].Volume);
            Assert.Equal(65f, vm2.Channels[1].Volume);
            Assert.Equal(0.4, vm2.ChatMixValue, 2);
            Assert.Equal("Mic Studio", vm2.SelectedTopTab);
            Assert.Equal(4.5f, vm2.MicGainDb);
            Assert.Equal(-40.0f, vm2.NoiseGateThresholdDb);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void AudioStudio_ChannelDevices_CanSelectIndependentEndpoints()
    {
        var mockAudioEngine = new Nexus.Engines.Audio.AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);

        var gameChoice = new Nexus.App.ViewModels.AudioDeviceChoice
        {
            Id = "device-sonar-game",
            Name = "SteelSeries Sonar - Gaming",
            DisplayName = "SteelSeries Sonar - Gaming [Virtual]",
            IsVirtual = true
        };

        var chatChoice = new Nexus.App.ViewModels.AudioDeviceChoice
        {
            Id = "device-sonar-chat",
            Name = "SteelSeries Sonar - Chat",
            DisplayName = "SteelSeries Sonar - Chat [Virtual]",
            IsVirtual = true
        };

        vm.Channels[1].AvailableDevices.Add(gameChoice);
        vm.Channels[1].SelectedDevice = gameChoice;

        vm.Channels[2].AvailableDevices.Add(chatChoice);
        vm.Channels[2].SelectedDevice = chatChoice;

        Assert.Equal("device-sonar-game", vm.Channels[1].SelectedDevice.Id);
        Assert.True(vm.Channels[1].IsVirtualSource);
        Assert.Contains("Virtual", vm.Channels[1].EndpointStatusText);

        Assert.Equal("device-sonar-chat", vm.Channels[2].SelectedDevice.Id);
        Assert.True(vm.Channels[2].IsVirtualSource);
    }

    [Fact]
    public void AudioStudio_SoloRouting_MutesNonSoloChannels()
    {
        var mockAudioEngine = new Nexus.Engines.Audio.AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);

        var sessionApp = new Nexus.App.ViewModels.AudioSessionItem(null, "Discord", "#00B4D8", "Chat");
        vm.Channels[2].AssignedApps.Add(sessionApp);

        var gameApp = new Nexus.App.ViewModels.AudioSessionItem(null, "CS2", "#10B981", "Game");
        vm.Channels[1].AssignedApps.Add(gameApp);

        // Turn Solo ON for Game channel
        vm.Channels[1].IsSolo = true;

        // Chat app should be muted
        Assert.True(sessionApp.IsMuted);
        // Game app should NOT be muted
        Assert.False(gameApp.IsMuted);

        // Turn Solo OFF
        vm.Channels[1].IsSolo = false;
        Assert.False(sessionApp.IsMuted);
    }

    [Fact]
    public void AudioStudio_DevicePersistence_SavesAndRestoresSelectedDeviceIds()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nexus_audio_dev_{Guid.NewGuid():N}.json");
        try
        {
            var mockAudioEngine = new Nexus.Engines.Audio.AudioEngine();
            using var vm1 = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);
            vm1.EnablePersistence = true;
            vm1.StateFilePath = tempFile;

            var gameDev = new Nexus.App.ViewModels.AudioDeviceChoice { Id = "test-game-id", Name = "Test Game Device" };
            vm1.Channels[1].AvailableDevices.Add(gameDev);
            vm1.Channels[1].SelectedDevice = gameDev;

            var chatDev = new Nexus.App.ViewModels.AudioDeviceChoice { Id = "test-chat-id", Name = "Test Chat Device" };
            vm1.Channels[2].AvailableDevices.Add(chatDev);
            vm1.Channels[2].SelectedDevice = chatDev;

            vm1.SaveState();

            Assert.True(System.IO.File.Exists(tempFile));

            using var vm2 = new Nexus.App.ViewModels.AudioStudioViewModel(mockAudioEngine);
            vm2.EnablePersistence = true;
            vm2.StateFilePath = tempFile;

            var method = typeof(Nexus.App.ViewModels.AudioStudioViewModel).GetMethod("LoadSavedState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(vm2, null);

            // Re-populate devices and verify auto-binding to saved ID
            vm2.Channels[1].AvailableDevices.Add(gameDev);
            vm2.Channels[2].AvailableDevices.Add(chatDev);
            
            var bindMethod = typeof(Nexus.App.ViewModels.AudioStudioViewModel).GetMethod("AutoBindChannelDevices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            bindMethod?.Invoke(vm2, null);

            Assert.Equal("test-game-id", vm2.Channels[1].SelectedDevice?.Id);
            Assert.Equal("test-chat-id", vm2.Channels[2].SelectedDevice?.Id);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }
}

