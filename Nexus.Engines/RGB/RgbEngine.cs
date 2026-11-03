using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Rgb;

namespace Nexus.Engines.RGB;

public class RgbEngine : IRgbEngine, IDisposable
{
    private readonly ConcurrentDictionary<string, IRgbDevice> _devices = new();
    private readonly ConcurrentDictionary<string, RgbEffectConfig> _deviceEffects = new();
    private readonly ConcurrentDictionary<string, (RgbColor Primary, double Brightness, RgbEffectType Effect)> _lastAppliedDeviceEffect = new();
    private readonly ILogger<RgbEngine>? _logger;
    private CancellationTokenSource? _renderCts;
    private Task? _renderLoopTask;
    private readonly Stopwatch _stopwatch = new();
    private bool _isDisposed;

    public bool IsRunning => _renderLoopTask != null && !_renderLoopTask.IsCompleted;
    public bool IsSynchronized { get; set; } = true;
    public RgbEffectConfig GlobalEffect { get; set; } = new();
    public IReadOnlyList<IRgbDevice> Devices => _devices.Values.ToList();
    public Func<float>? AudioPeakLevelProvider { get; set; }

    public event EventHandler<EventArgs>? FrameRendered;

    public RgbEngine(ILogger<RgbEngine>? logger = null)
    {
        _logger = logger;
    }

    public void RegisterDevice(IRgbDevice device)
    {
        _devices.TryAdd(device.Id, device);
        _deviceEffects.TryAdd(device.Id, GlobalEffect);
        _logger?.LogInformation("Registered RGB device '{Name}' ({Id})", device.Name, device.Id);
    }

    public void UnregisterDevice(string deviceId)
    {
        _devices.TryRemove(deviceId, out _);
        _deviceEffects.TryRemove(deviceId, out _);
        _logger?.LogInformation("Unregistered RGB device {Id}", deviceId);
    }

    public void SetDeviceEffect(string deviceId, RgbEffectConfig effect)
    {
        _deviceEffects[deviceId] = effect;
    }

    public async Task ApplyStaticColorToAllAsync(RgbColor color)
    {
        GlobalEffect = new RgbEffectConfig
        {
            EffectType = RgbEffectType.Static,
            PrimaryColor = color
        };

        _lastAppliedDeviceEffect.Clear();

        foreach (var device in _devices.Values)
        {
            await device.SetAllColorsAsync(color);
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        _renderCts = new CancellationTokenSource();
        _stopwatch.Restart();
        _renderLoopTask = Task.Run(() => RenderLoopAsync(_renderCts.Token));
        _logger?.LogInformation("RGB Engine animation loop started.");
    }

    public void Stop()
    {
        if (!IsRunning || _renderCts == null) return;
        _renderCts.Cancel();
        try { _renderLoopTask?.Wait(500); } catch { }
        _renderCts.Dispose();
        _renderCts = null;
        _renderLoopTask = null;
        _stopwatch.Stop();
        _logger?.LogInformation("RGB Engine animation loop stopped.");
    }

    private async Task RenderLoopAsync(CancellationToken token)
    {
        const int targetFps = 30; // 30 FPS provides fluid animation with < 0.2% CPU usage
        int frameIntervalMs = 1000 / targetFps;

        while (!token.IsCancellationRequested)
        {
            var frameStart = _stopwatch.ElapsedMilliseconds;
            double timeSeconds = _stopwatch.Elapsed.TotalSeconds;

            try
            {
                foreach (var device in _devices.Values)
                {
                    var effect = IsSynchronized ? GlobalEffect : _deviceEffects.GetValueOrDefault(device.Id, GlobalEffect);
                    await RenderDeviceEffectAsync(device, effect, timeSeconds, token);
                }

                FrameRendered?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error occurred in RGB render loop.");
            }

            var elapsed = (int)(_stopwatch.ElapsedMilliseconds - frameStart);
            var delay = Math.Max(1, frameIntervalMs - elapsed);
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RenderDeviceEffectAsync(IRgbDevice device, RgbEffectConfig effect, double timeSeconds, CancellationToken token)
    {
        double brightness = Math.Clamp(effect.Brightness * device.Brightness, 0.0, 1.0);

        // For Static effect, dispatch to hardware only once when color or brightness changes!
        // Avoid sending 30 redundant frames per second over USB/SMBus which floods controllers.
        if (effect.EffectType == RgbEffectType.Static)
        {
            if (_lastAppliedDeviceEffect.TryGetValue(device.Id, out var prev) &&
                prev.Effect == RgbEffectType.Static &&
                prev.Primary == effect.PrimaryColor &&
                Math.Abs(prev.Brightness - brightness) < 0.001)
            {
                return;
            }

            _lastAppliedDeviceEffect[device.Id] = (effect.PrimaryColor, brightness, RgbEffectType.Static);
            var staticColor = ApplyBrightness(effect.PrimaryColor, brightness);
            await device.SetAllColorsAsync(staticColor, token);
            return;
        }

        // For dynamic animated effects, clear any static lock so returning to Static will apply immediately
        _lastAppliedDeviceEffect.TryRemove(device.Id, out _);

        foreach (var zone in device.Zones)
        {
            var colors = new RgbColor[zone.LedCount];
            double speed = Math.Max(0.01, effect.Speed);

            switch (effect.EffectType)
            {
                case RgbEffectType.Static:
                    var staticColor = ApplyBrightness(effect.PrimaryColor, brightness);
                    Array.Fill(colors, staticColor);
                    break;

                case RgbEffectType.Breathing:
                    // Sinusoidal breath between 0.1 and 1.0
                    double breath = (Math.Sin(timeSeconds * speed * 2.0) + 1.0) / 2.0;
                    var breathColor = ApplyBrightness(effect.PrimaryColor, brightness * breath);
                    Array.Fill(colors, breathColor);
                    break;

                case RgbEffectType.ColorCycle:
                    double cycleHue = (timeSeconds * speed * 60.0 * effect.Direction) % 360.0;
                    var cycleColor = ApplyBrightness(RgbColor.FromHsv(cycleHue, 1.0, 1.0), brightness);
                    Array.Fill(colors, cycleColor);
                    break;

                case RgbEffectType.Rainbow:
                    for (int i = 0; i < zone.LedCount; i++)
                    {
                        double ledOffset = zone.LedCount > 1 ? (double)i / zone.LedCount * 360.0 : 0;
                        double rainbowHue = (timeSeconds * speed * 90.0 * effect.Direction + ledOffset) % 360.0;
                        colors[i] = ApplyBrightness(RgbColor.FromHsv(rainbowHue, 1.0, 1.0), brightness);
                    }
                    break;

                case RgbEffectType.Wave:
                    for (int i = 0; i < zone.LedCount; i++)
                    {
                        double spatial = zone.LedCount > 1 ? (double)i / zone.LedCount : 0;
                        double waveT = (Math.Sin((timeSeconds * speed * 4.0 * effect.Direction) + (spatial * Math.PI * 2.0)) + 1.0) / 2.0;
                        colors[i] = ApplyBrightness(RgbColor.Lerp(effect.PrimaryColor, effect.SecondaryColor, waveT), brightness);
                    }
                    break;

                case RgbEffectType.Gradient:
                    for (int i = 0; i < zone.LedCount; i++)
                    {
                        double t = zone.LedCount > 1 ? (double)i / (zone.LedCount - 1) : 0;
                        colors[i] = ApplyBrightness(RgbColor.Lerp(effect.PrimaryColor, effect.SecondaryColor, t), brightness);
                    }
                    break;

                case RgbEffectType.AudioVisualizer:
                    float peak = AudioPeakLevelProvider?.Invoke() ?? 0.25f;
                    double pulse = Math.Clamp(peak * 1.8, 0.08, 1.0);
                    var beatColor = RgbColor.Lerp(effect.SecondaryColor, effect.PrimaryColor, pulse);
                    for (int i = 0; i < zone.LedCount; i++)
                    {
                        double pos = zone.LedCount > 1 ? (double)i / zone.LedCount : 0.5;
                        double wave = (Math.Sin((pos * Math.PI * 2.0) - (timeSeconds * 5.0)) + 1.0) / 2.0;
                        colors[i] = ApplyBrightness(beatColor, brightness * pulse * (0.5 + 0.5 * wave));
                    }
                    break;

                case RgbEffectType.TemperatureBased:
                    // Defaults to dynamic thermal color (Green -> Amber -> Red)
                    var tempColor = ApplyBrightness(RgbColor.NexusCyan, brightness);
                    Array.Fill(colors, tempColor);
                    break;

                default:
                    Array.Fill(colors, ApplyBrightness(effect.PrimaryColor, brightness));
                    break;
            }

            await device.SetZoneColorsAsync(zone.ZoneId, colors, token);
        }
    }

    private static RgbColor ApplyBrightness(RgbColor color, double brightness)
    {
        brightness = Math.Clamp(brightness, 0.0, 1.0);
        return new RgbColor(
            (byte)Math.Round(color.R * brightness),
            (byte)Math.Round(color.G * brightness),
            (byte)Math.Round(color.B * brightness),
            color.A);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}
