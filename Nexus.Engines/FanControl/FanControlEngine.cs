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

    public IReadOnlyList<IFanController> FanControllers => _controllers.Values.ToList();
    public IReadOnlyList<FanCurve> SavedCurves => _curves.Values.ToList();
    public bool IsSafetyWatchdogActive { get; private set; } = true;

    public FanControlEngine(IEventBus eventBus, ILogger<FanControlEngine>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;

        // Default Curves
        var defaultBalanced = new FanCurve
        {
            Id = "curve-balanced",
            Name = "Silent to Performance",
            TargetSensorId = "CPU",
            MinFanSpeedPercent = 25,
            EmergencyTempCelsius = 85,
            Points = new List<FanCurvePoint>
            {
                new(30, 25),
                new(50, 40),
                new(65, 60),
                new(75, 80),
                new(85, 100)
            }
        };

        var quietCurve = new FanCurve
        {
            Id = "curve-quiet",
            Name = "Acoustic Priority",
            TargetSensorId = "CPU",
            MinFanSpeedPercent = 20,
            EmergencyTempCelsius = 85,
            Points = new List<FanCurvePoint>
            {
                new(30, 20),
                new(60, 30),
                new(75, 55),
                new(82, 80),
                new(85, 100)
            }
        };

        _curves.TryAdd(defaultBalanced.Id, defaultBalanced);
        _curves.TryAdd(quietCurve.Id, quietCurve);

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

        foreach (var controller in _controllers.Values)
        {
            if (!controller.SupportsDirectControl) continue;

            double targetTemp = snapshot.CpuTemperatureCelsius ?? 45.0;
            if (controller.ActiveCurve.TargetSensorId.Equals("GPU", StringComparison.OrdinalIgnoreCase) && snapshot.GpuTemperatureCelsius.HasValue)
            {
                targetTemp = snapshot.GpuTemperatureCelsius.Value;
            }

            double speedPercent = controller.ActiveCurve.Evaluate(targetTemp);
            try
            {
                await controller.SetSpeedPercentAsync(speedPercent);
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
