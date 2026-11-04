using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Nexus.App.ViewModels;

namespace Nexus.App.Views;

public partial class RgbStudioView : UserControl
{
    private static Microsoft.Web.WebView2.Wpf.WebView2? _sharedRigWebView;
    private static bool _isSharedWebViewInitialized;
    private static RgbStudioView? _currentActiveView;

    private RgbStudioViewModel? _vm;

    public RgbStudioView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && _isSharedWebViewInitialized)
        {
            PushCurrentLighting();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RgbStudioViewModel oldVm)
        {
            oldVm.LightingUpdated -= OnLightingUpdated;
            oldVm.AioLcdUpdated -= OnAioLcdUpdated;
        }

        if (e.NewValue is RgbStudioViewModel newVm)
        {
            _vm = newVm;
            _vm.LightingUpdated += OnLightingUpdated;
            _vm.AioLcdUpdated += OnAioLcdUpdated;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _currentActiveView = this;

        if (_vm == null && DataContext is RgbStudioViewModel vm)
        {
            _vm = vm;
            _vm.LightingUpdated += OnLightingUpdated;
            _vm.AioLcdUpdated += OnAioLcdUpdated;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        await Initialize3DViewportAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.LightingUpdated -= OnLightingUpdated;
            _vm.AioLcdUpdated -= OnAioLcdUpdated;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        // Safely detach shared WebView2 from this view without disposing it
        if (_sharedRigWebView != null && WebViewHostBorder.Child == _sharedRigWebView)
        {
            WebViewHostBorder.Child = null;
        }

        if (_currentActiveView == this)
        {
            _currentActiveView = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RgbStudioViewModel.Is3dModeActive))
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (_sharedRigWebView?.CoreWebView2 != null && _isSharedWebViewInitialized)
                {
                    string mode = _vm?.Is3dModeActive == true ? "3d" : "2d";
                    await _sharedRigWebView.CoreWebView2.ExecuteScriptAsync($"if (window.switchRigMode) window.switchRigMode('{mode}');");
                }
            });
        }
    }

    private async Task Initialize3DViewportAsync()
    {
        if (WebViewHostBorder == null) return;

        try
        {
            if (_sharedRigWebView == null)
            {
                _sharedRigWebView = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(13, 17, 26)
                };
            }

            // Detach from previous parent if any
            if (_sharedRigWebView.Parent is Border oldParent)
            {
                oldParent.Child = null;
            }

            // Attach to current view host
            WebViewHostBorder.Child = _sharedRigWebView;

            if (_isSharedWebViewInitialized)
            {
                PushCurrentLighting();
                return;
            }

            string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "3d");
            if (!Directory.Exists(assetsDir))
            {
                assetsDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "3d");
            }
            if (!Directory.Exists(assetsDir))
            {
                string solutionDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
                string fallback = Path.Combine(solutionDir, "Nexus.App", "Assets", "3d");
                if (Directory.Exists(fallback)) assetsDir = fallback;
            }

            if (!Directory.Exists(assetsDir))
            {
                Serilog.Log.Error("3D Viewport assets directory not found! Checked: {Path}", assetsDir);
                return;
            }

            Serilog.Log.Information("Initializing 3D Viewport using assets from: {Path}", assetsDir);

            // Reuse prewarmed environment from App startup if ready, or initialize now
            CoreWebView2Environment env;
            if (App.PrewarmedWebView2Environment != null)
            {
                env = await App.PrewarmedWebView2Environment;
            }
            else
            {
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "WebView2");
                env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            }

            await _sharedRigWebView.EnsureCoreWebView2Async(env);

            _sharedRigWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _sharedRigWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _sharedRigWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            // Map local directory to virtual host name to enable glTF binary fetch without CORS blocks
            _sharedRigWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "nexus3d.local",
                assetsDir,
                CoreWebView2HostResourceAccessKind.Allow);

            _sharedRigWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _sharedRigWebView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                if (!args.IsSuccess)
                {
                    Serilog.Log.Error("WebView2 3D Viewport navigation failed: {Status} (HTTP {Http})", args.WebErrorStatus, args.HttpStatusCode);
                }
                else
                {
                    Serilog.Log.Information("WebView2 3D Viewport navigation succeeded.");
                    _isSharedWebViewInitialized = true;
                    PushCurrentLighting();
                }
            };

            _sharedRigWebView.CoreWebView2.Navigate("https://nexus3d.local/pc_viewer.html");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to initialize 3D Viewport in RGB Studio.");
            if (_vm != null)
            {
                _vm.Is3dModeActive = false;
            }
        }
    }

    private void PushCurrentLighting()
    {
        if (_vm == null || _sharedRigWebView?.CoreWebView2 == null) return;
        string target = _vm.IsSyncAllDevices ? "all" : (_vm.SelectedCanvasDevice?.DeviceId ?? "all");
        OnLightingUpdated(target, _vm.PrimaryColorHex, _vm.SelectedEffect.ToString());
        _vm.ApplyLcdSettings();
    }

    private void OnAioLcdUpdated(Nexus.Abstractions.Cooling.AioLcdConfig config, int tempValue)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            if (_sharedRigWebView?.CoreWebView2 != null && _isSharedWebViewInitialized)
            {
                try
                {
                    string mode = config.Mode.ToString();
                    string dialAccent = config.DialAccentColorHex ?? "#00D2FF";
                    string haloColor = config.HaloRingColorHex ?? "#00D2FF";
                    string customImg = (config.CustomImagePath ?? "").Replace("\\", "/");
                    await _sharedRigWebView.CoreWebView2.ExecuteScriptAsync(
                        $"if (window.updateAioLcd) window.updateAioLcd('{mode}', {tempValue}, '{dialAccent}', '{customImg}', '{haloColor}');");
                }
                catch { }
            }
        });
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                string msgType = typeProp.GetString() ?? "";

                if (msgType == "device_selected" && doc.RootElement.TryGetProperty("deviceId", out var idProp))
                {
                    string devId = idProp.GetString() ?? "";
                    Dispatcher.Invoke(() =>
                    {
                        _vm?.SelectCanvasDeviceCommand.Execute(devId);
                    });
                }
                else if (msgType == "model_ready")
                {
                    _isSharedWebViewInitialized = true;
                    Dispatcher.Invoke(() =>
                    {
                        PushCurrentLighting();
                    });
                }
                else if (msgType == "js_error" && doc.RootElement.TryGetProperty("message", out var errProp))
                {
                    Serilog.Log.Error("3D Viewport JavaScript Error: {Message}", errProp.GetString());
                }
            }
        }
        catch { }
    }

    private void OnLightingUpdated(string deviceKey, string hexColor, string effectName)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            if (_sharedRigWebView?.CoreWebView2 != null && _isSharedWebViewInitialized)
            {
                try
                {
                    if (string.Equals(deviceKey, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        await _sharedRigWebView.CoreWebView2.ExecuteScriptAsync($"window.updateAllColors('{hexColor}', '{effectName}');");
                    }
                    else
                    {
                        await _sharedRigWebView.CoreWebView2.ExecuteScriptAsync($"window.updateZoneColor('{deviceKey}', '{hexColor}', '{effectName}');");
                    }
                }
                catch { }
            }
        });
    }
}
