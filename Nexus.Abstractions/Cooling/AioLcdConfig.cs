namespace Nexus.Abstractions.Cooling;

public enum AioLcdMode
{
    CpuTempGauge,
    GpuTempGauge,
    CoolantTempGauge,
    NexusLogo,
    DigitalClock,
    CustomImage,
    CustomGif,
    ArcReactor
}

public class AioLcdConfig
{
    public AioLcdMode Mode { get; set; } = AioLcdMode.CpuTempGauge;
    public string CustomImagePath { get; set; } = string.Empty;
    public string CustomGifPath { get; set; } = string.Empty;
    public double Brightness { get; set; } = 1.0;
    public string HaloRingColorHex { get; set; } = "#00D2FF";
    public string DialAccentColorHex { get; set; } = "#00D2FF";
    public bool ShowTempUnits { get; set; } = true;
}
