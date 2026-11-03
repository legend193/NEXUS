namespace Nexus.Abstractions.Cooling;

public class FanCurvePoint : IComparable<FanCurvePoint>
{
    public double TemperatureCelsius { get; set; }
    public double FanSpeedPercent { get; set; }

    public FanCurvePoint() { }

    public FanCurvePoint(double temp, double speed)
    {
        TemperatureCelsius = Math.Clamp(temp, 0, 100);
        FanSpeedPercent = Math.Clamp(speed, 0, 100);
    }

    public int CompareTo(FanCurvePoint? other) => other == null ? 1 : TemperatureCelsius.CompareTo(other.TemperatureCelsius);
}

public class FanCurve
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Custom Curve";
    public string TargetSensorId { get; set; } = "CPU"; // "CPU", "GPU", etc.
    public double MinFanSpeedPercent { get; set; } = 25.0; // Safety floor
    public double MaxFanSpeedPercent { get; set; } = 100.0;
    public double EmergencyTempCelsius { get; set; } = 85.0; // Force 100% fan speed
    public List<FanCurvePoint> Points { get; set; } = new()
    {
        new(30, 25),
        new(50, 40),
        new(65, 65),
        new(75, 85),
        new(85, 100)
    };

    public double Evaluate(double temperatureCelsius)
    {
        // Strict safety override
        if (temperatureCelsius >= EmergencyTempCelsius)
        {
            return 100.0;
        }

        if (Points.Count == 0)
        {
            return Math.Clamp(50.0, MinFanSpeedPercent, MaxFanSpeedPercent);
        }

        var sorted = Points.OrderBy(p => p.TemperatureCelsius).ToList();

        if (temperatureCelsius <= sorted[0].TemperatureCelsius)
        {
            return Math.Clamp(sorted[0].FanSpeedPercent, MinFanSpeedPercent, MaxFanSpeedPercent);
        }

        if (temperatureCelsius >= sorted[^1].TemperatureCelsius)
        {
            return Math.Clamp(sorted[^1].FanSpeedPercent, MinFanSpeedPercent, MaxFanSpeedPercent);
        }

        // Linear interpolation between the two surrounding points
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var p1 = sorted[i];
            var p2 = sorted[i + 1];

            if (temperatureCelsius >= p1.TemperatureCelsius && temperatureCelsius <= p2.TemperatureCelsius)
            {
                double span = p2.TemperatureCelsius - p1.TemperatureCelsius;
                if (span <= 0) return p1.FanSpeedPercent;
                double factor = (temperatureCelsius - p1.TemperatureCelsius) / span;
                double calculated = p1.FanSpeedPercent + factor * (p2.FanSpeedPercent - p1.FanSpeedPercent);
                return Math.Clamp(calculated, MinFanSpeedPercent, MaxFanSpeedPercent);
            }
        }

        return Math.Clamp(sorted[^1].FanSpeedPercent, MinFanSpeedPercent, MaxFanSpeedPercent);
    }
}

public interface IFanController
{
    string FanId { get; }
    string Name { get; }
    int? CurrentRpm { get; }
    double? CurrentPercentage { get; }
    bool SupportsDirectControl { get; }
    string ControlSource { get; } // "Hardware", "NexusCurve", "ManualFixed"
    FanCurve ActiveCurve { get; set; }
    Task<bool> SetSpeedPercentAsync(double percent, CancellationToken cancellationToken = default);
    Task RestoreHardwareControlAsync(CancellationToken cancellationToken = default);
}

public interface IFanControlEngine
{
    IReadOnlyList<IFanController> FanControllers { get; }
    IReadOnlyList<FanCurve> SavedCurves { get; }
    bool IsSafetyWatchdogActive { get; }

    void RegisterController(IFanController controller);
    void UnregisterController(string fanId);
    void SaveCurve(FanCurve curve);
    void DeleteCurve(string curveId);
    Task ApplyCurveToFanAsync(string fanId, string curveId);
    Task SetManualFanSpeedAsync(string fanId, double percent);
    Task ResetAllToSafeDefaultsAsync();
}
