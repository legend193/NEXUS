using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.EventBus;
using Nexus.Abstractions.Sensors;

namespace Nexus.Engines.FanControl;

public class FanControlEngine : IFanControlEngine, IDisposable
{
    private readonly ConcurrentDictionary<string, IFanController> _controllers = new();
    private readonly ConcurrentDictionary<string, FanCurve> _curves = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<FanControlEngine>? _logger;
    private IDisposable? _telemetrySub;
    private bool _isDisposed;

    private readonly ConcurrentDictionary<string, double> _smoothedTemperatures = new();
    private readonly ConcurrentDictionary<string, double> _currentAppliedSpeeds = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes = new();

    public IReadOnlyList<IFanController> FanControllers => _controllers.Values.ToList();
    public IReadOnlyList<FanCurve> SavedCurves => _curves.Values.ToList();
    public bool IsSafetyWatchdogActive { get; private set; } = true;

    public FanControlEngine(IEventBus eventBus, ILogger<FanControlEngine>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;

        // Default Presets
        var defaultBalanced = new FanCurve
        {
            Id = "curve-balanced",
            Name = "Dynamic Balanced",
            TargetSensorId = "CPU",
            SensorSource = SensorSourceType.Cpu,
            MinFanSpeedPercent = 25,
            EmergencyTempCelsius = 85,
            Smoothing = SmoothingSpeed.Smooth,
            Points = new List<FanCurvePoint>
            {
                new(20, 25),
                new(40, 35),
                new(60, 55),
                new(75, 80),
                new(85, 100)
            }
        };

        var quietCurve = new FanCurve
        {
            Id = "curve-quiet",
            Name = "Acoustic Silent",
            TargetSensorId = "CPU",
            SensorSource = SensorSourceType.Cpu,
            MinFanSpeedPercent = 20,
            EmergencyTempCelsius = 85,
            Smoothing = SmoothingSpeed.Smooth,
            Points = new List<FanCurvePoint>
            {
                new(20, 20),
                new(45, 25),
                new(65, 40),
                new(78, 65),
                new(85, 100)
            }
        };

        var performanceCurve = new FanCurve
        {
            Id = "curve-performance",
            Name = "Performance Airflow",
            TargetSensorId = "Max",
            SensorSource = SensorSourceType.MaxCpuGpu,
            MinFanSpeedPercent = 40,
            EmergencyTempCelsius = 85,
            Smoothing = SmoothingSpeed.Balanced,
            Points = new List<FanCurvePoint>
            {
                new(20, 40),
                new(40, 55),
                new(60, 75),
                new(75, 90),
                new(85, 100)
            }
        };

        _curves.TryAdd(defaultBalanced.Id, defaultBalanced);
        _curves.TryAdd(quietCurve.Id, quietCurve);
        _curves.TryAdd(performanceCurve.Id, performanceCurve);

        _telemetrySub = _eventBus.Subscribe<SystemTelemetrySnapshot>(OnTelemetryReceived);
    }

    public void RegisterController(IFanController controller)
    {
        _controllers.TryAdd(controller.FanId, controller);
        _logger?.LogInformation("Registered Fan Controller '{Name}' ({Id})", controller.Name, controller.FanId);
    }

    public void UnregisterController(string fanId)
    {
        _controllers.TryRemove(fanId, out _);
        _logger?.LogInformation("Unregistered Fan Controller {Id}", fanId);
    }

    public void SaveCurve(FanCurve curve)
    {
        _curves.AddOrUpdate(curve.Id, curve, (_, _) => curve);
    }

    public void DeleteCurve(string curveId)
    {
        _curves.TryRemove(curveId, out _);
    }

    public async Task ApplyCurveToFanAsync(string fanId, string curveId)
    {
        if (_controllers.TryGetValue(fanId, out var controller) && _curves.TryGetValue(curveId, out var curve))
        {
            controller.ActiveCurve = curve;
            _logger?.LogInformation("Applied curve '{CurveName}' to fan '{FanName}'", curve.Name, controller.Name);
        }
        await Task.CompletedTask;
    }

    public async Task SetManualFanSpeedAsync(string fanId, double percent)
    {
        if (_controllers.TryGetValue(fanId, out var controller))
        {
            // Safety limit clamp
            percent = Math.Clamp(percent, 20.0, 100.0);
            await controller.SetSpeedPercentAsync(percent);
            _logger?.LogInformation("Manual fan speed for '{FanName}' set to {Percent}%", controller.Name, percent);
        }
    }

    public async Task ResetAllToSafeDefaultsAsync()
    {
        _logger?.LogWarning("Resetting all fans to safe hardware default control.");
        foreach (var controller in _controllers.Values)
        {
            try
            {
                await controller.RestoreHardwareControlAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to restore hardware control for {FanName}", controller.Name);
            }
        }
    }

    private async Task OnTelemetryReceived(SystemTelemetrySnapshot snapshot)
    {
        if (!IsSafetyWatchdogActive) return;

        double cpuTemp = snapshot.CpuTemperatureCelsius ?? 45.0;
        double gpuTemp = snapshot.GpuTemperatureCelsius ?? 50.0;
        double coolantTemp = Math.Max(30.0, (cpuTemp * 0.35) + 16.0); // Liquid temperature changes slowly with ambient base

        DateTime now = DateTime.UtcNow;

        foreach (var controller in _controllers.Values)
        {
            if (!controller.SupportsDirectControl) continue;

            var curve = controller.ActiveCurve;
            double rawTemp = curve.SensorSource switch
            {
                SensorSourceType.Gpu => gpuTemp,
                SensorSourceType.Coolant => coolantTemp,
                SensorSourceType.MaxCpuGpu => Math.Max(cpuTemp, gpuTemp),
                _ => cpuTemp
            };

            // Emergency thermal override (85°C watchdog)
            if (rawTemp >= curve.EmergencyTempCelsius)
            {
                _smoothedTemperatures[controller.FanId] = rawTemp;
                _currentAppliedSpeeds[controller.FanId] = 100.0;
                await controller.SetSpeedPercentAsync(100.0);
                continue;
            }

            // Exponential Moving Average (EMA) thermal filter to eliminate fan surging ("التبريد يكبس")
            double alpha = curve.Smoothing switch
            {
                SmoothingSpeed.Smooth => 0.18,   // Highly filtered, resilient to transient spikes
                SmoothingSpeed.Balanced => 0.40, // Responsive yet smooth
                _ => 1.0                         // Direct (no filter)
            };

            double prevSmoothed = _smoothedTemperatures.GetOrAdd(controller.FanId, rawTemp);
            double smoothed = (alpha * rawTemp) + ((1.0 - alpha) * prevSmoothed);
            _smoothedTemperatures[controller.FanId] = smoothed;

            // Evaluate target speed from smoothed temperature
            double targetSpeed = curve.Evaluate(smoothed);

            // Progressive step-smoothing rate limit
            double prevSpeed = _currentAppliedSpeeds.GetOrAdd(controller.FanId, targetSpeed);
            DateTime lastTime = _lastUpdateTimes.GetOrAdd(controller.FanId, now);
            double dt = Math.Clamp((now - lastTime).TotalSeconds, 0.1, 2.0);
            _lastUpdateTimes[controller.FanId] = now;

            double maxStepUp = 30.0 * dt;   // Max 30% speed increase per second
            double maxStepDown = 15.0 * dt; // Max 15% speed decrease per second (gradual quiet spin-down)

            double finalSpeed;
            if (targetSpeed > prevSpeed)
            {
                finalSpeed = Math.Min(targetSpeed, prevSpeed + maxStepUp);
            }
            else
            {
                finalSpeed = Math.Max(targetSpeed, prevSpeed - maxStepDown);
            }

            _currentAppliedSpeeds[controller.FanId] = finalSpeed;

            try
            {
                await controller.SetSpeedPercentAsync(finalSpeed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to update fan speed on controller {FanName}", controller.Name);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _telemetrySub?.Dispose();
        // Crucial safety requirement: restore safe default hardware fan control on disposal/shutdown!
        _ = ResetAllToSafeDefaultsAsync();
    }
}
