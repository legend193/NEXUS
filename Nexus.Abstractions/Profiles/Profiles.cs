using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.Rgb;

namespace Nexus.Abstractions.Profiles;

public enum PerformanceMode
{
    Silent,
    Balanced,
    Performance,
    Gaming,
    Custom
}

public class ProfileData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Profile";
    public string Description { get; set; } = string.Empty;
    public PerformanceMode Mode { get; set; } = PerformanceMode.Balanced;
    public bool IsActive { get; set; }
    public bool IsBuiltIn { get; set; }

    // RGB Configuration
    public RgbEffectConfig RgbConfig { get; set; } = new();

    // Audio Configuration
    public string? PreferredPlaybackDeviceId { get; set; }
    public string? PreferredRecordingDeviceId { get; set; }
    public string EqualizerPresetName { get; set; } = "Flat";

    // Fan curves
    public List<FanCurve> FanCurves { get; set; } = new();

    // Triggers / Linked Applications
    public List<string> TriggerProcessNames { get; set; } = new();
}

public interface IProfileManager
{
    IReadOnlyList<ProfileData> Profiles { get; }
    ProfileData ActiveProfile { get; }

    event EventHandler<ProfileData>? ProfileChanged;

    Task<ProfileData> CreateProfileAsync(string name, PerformanceMode mode);
    Task ActivateProfileAsync(string profileId);
    Task UpdateProfileAsync(ProfileData profile);
    Task DeleteProfileAsync(string profileId);
    Task<ProfileData> DuplicateProfileAsync(string profileId);
    Task<string> ExportProfileJsonAsync(string profileId);
    Task<ProfileData> ImportProfileJsonAsync(string json);
}
