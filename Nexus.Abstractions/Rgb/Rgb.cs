using System.Text.Json.Serialization;
using Nexus.Abstractions.Devices;

namespace Nexus.Abstractions.Rgb;

public readonly record struct RgbColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);
    public static readonly RgbColor Red = new(255, 0, 0);
    public static readonly RgbColor Green = new(0, 255, 0);
    public static readonly RgbColor Blue = new(0, 0, 255);
    public static readonly RgbColor Cyan = new(0, 255, 255);
    public static readonly RgbColor Magenta = new(255, 0, 255);
    public static readonly RgbColor Yellow = new(255, 255, 0);
    public static readonly RgbColor NexusCyan = new(0, 210, 255);
    public static readonly RgbColor NexusPurple = new(138, 43, 226);

    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public static RgbColor FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return White;
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            return new RgbColor(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        if (hex.Length == 8)
        {
            return new RgbColor(
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16),
                Convert.ToByte(hex.Substring(0, 2), 16));
        }
        return White;
    }

    public static RgbColor FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        double c = value * saturation;
        double x = c * (1 - Math.Abs((hue / 60.0 % 2) - 1));
        double m = value - c;

        double r1 = 0, g1 = 0, b1 = 0;
        if (hue < 60) { r1 = c; g1 = x; }
        else if (hue < 120) { r1 = x; g1 = c; }
        else if (hue < 180) { g1 = c; b1 = x; }
        else if (hue < 240) { g1 = x; b1 = c; }
        else if (hue < 300) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        return new RgbColor(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    public static RgbColor Lerp(RgbColor c1, RgbColor c2, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte r = (byte)(c1.R + (c2.R - c1.R) * t);
        byte g = (byte)(c1.G + (c2.G - c1.G) * t);
        byte b = (byte)(c1.B + (c2.B - c1.B) * t);
        byte a = (byte)(c1.A + (c2.A - c1.A) * t);
        return new RgbColor(r, g, b, a);
    }
}

public enum RgbEffectType
{
    Static,
    Breathing,
    Rainbow,
    ColorCycle,
    Wave,
    Gradient,
    Reactive,
    AudioVisualizer,
    TemperatureBased,
    CustomAnimation
}

public class RgbEffectConfig
{
    public RgbEffectType EffectType { get; set; } = RgbEffectType.Rainbow;
    public double Speed { get; set; } = 1.0; // 0.1x to 5.0x
    public double Brightness { get; set; } = 1.0; // 0.0 to 1.0
    public RgbColor PrimaryColor { get; set; } = RgbColor.NexusCyan;
    public RgbColor SecondaryColor { get; set; } = RgbColor.NexusPurple;
    public List<RgbColor> GradientStops { get; set; } = new() { RgbColor.NexusCyan, RgbColor.NexusPurple };
    public int Direction { get; set; } = 1; // 1 = Forward/Clockwise, -1 = Reverse
    public double Intensity { get; set; } = 1.0;
}

public class RgbZone
{
    public string ZoneId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int LedCount { get; set; }
    public RgbColor[] CurrentColors { get; set; } = Array.Empty<RgbColor>();

    public RgbZone(string zoneId, string name, int ledCount)
    {
        ZoneId = zoneId;
        Name = name;
        // Hard sanity limit: PC RGB devices never exceed 512 LEDs. Prevents catastrophic allocations.
        LedCount = Math.Clamp(ledCount, 1, 512);
        CurrentColors = new RgbColor[LedCount];
        Array.Fill(CurrentColors, RgbColor.Black);
    }
}

public interface IRgbDevice : IDevice
{
    IReadOnlyList<RgbZone> Zones { get; }
    double Brightness { get; set; }
    Task SetZoneColorsAsync(string zoneId, ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken = default);
    Task SetAllColorsAsync(RgbColor color, CancellationToken cancellationToken = default);
}

public interface IRgbEngine
{
    bool IsRunning { get; }
    bool IsSynchronized { get; set; }
    RgbEffectConfig GlobalEffect { get; set; }
    IReadOnlyList<IRgbDevice> Devices { get; }

    event EventHandler<EventArgs>? FrameRendered;

    void RegisterDevice(IRgbDevice device);
    void UnregisterDevice(string deviceId);
    void SetDeviceEffect(string deviceId, RgbEffectConfig effect);
    Task ApplyStaticColorToAllAsync(RgbColor color);
    void Start();
    void Stop();
}
