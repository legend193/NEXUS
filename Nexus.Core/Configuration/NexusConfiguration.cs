namespace Nexus.Core.Configuration;

public class NexusConfiguration
{
    public string AccentColorHex { get; set; } = "#00D2FF"; // Nexus Cyan
    public bool DarkMode { get; set; } = true;
    public int HardwarePollIntervalMs { get; set; } = 1000;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToSystemTray { get; set; } = true;
    public bool EnableSafetyWatchdog { get; set; } = true;
    public double EmergencyThermalLimitCelsius { get; set; } = 85.0;
    public string ActiveProfileId { get; set; } = string.Empty;
    public bool HardwareAccelerationEnabled { get; set; } = true;
    public string Language { get; set; } = "en-US";
}
