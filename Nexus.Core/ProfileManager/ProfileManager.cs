using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.EventBus;
using Nexus.Abstractions.Profiles;
using Nexus.Abstractions.Rgb;

namespace Nexus.Core.ProfileManager;

public class ProfileManager : IProfileManager
{
    private readonly List<ProfileData> _profiles = new();
    private readonly object _lock = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<ProfileManager>? _logger;
    private ProfileData _activeProfile;

    public IReadOnlyList<ProfileData> Profiles
    {
        get
        {
            lock (_lock) return _profiles.ToList();
        }
    }

    public ProfileData ActiveProfile
    {
        get
        {
            lock (_lock) return _activeProfile;
        }
    }

    public event EventHandler<ProfileData>? ProfileChanged;

    public ProfileManager(IEventBus eventBus, ILogger<ProfileManager>? logger = null)
    {
        _eventBus = eventBus;
        _logger = logger;

        // Initialize built-in default profiles
        var silent = new ProfileData
        {
            Id = "silent-preset",
            Name = "Silent",
            Description = "Optimized for quiet acoustics and energy efficiency.",
            Mode = PerformanceMode.Silent,
            IsBuiltIn = true,
            RgbConfig = new RgbEffectConfig
            {
                EffectType = RgbEffectType.Breathing,
                Speed = 0.5,
                Brightness = 0.3,
                PrimaryColor = new RgbColor(0, 120, 215)
            },
            FanCurves = new List<FanCurve>
            {
                new()
                {
                    Name = "Silent Curve",
                    MinFanSpeedPercent = 20,
                    Points = new List<FanCurvePoint>
                    {
                        new(30, 20), new(55, 30), new(70, 50), new(80, 80), new(85, 100)
                    }
                }
            }
        };

        var balanced = new ProfileData
        {
            Id = "balanced-preset",
            Name = "Balanced",
            Description = "Standard balance of performance and cooling.",
            Mode = PerformanceMode.Balanced,
            IsBuiltIn = true,
            IsActive = true,
            RgbConfig = new RgbEffectConfig
            {
                EffectType = RgbEffectType.Rainbow,
                Speed = 1.0,
                Brightness = 0.8
            },
            FanCurves = new List<FanCurve>
            {
                new()
                {
                    Name = "Balanced Curve",
                    MinFanSpeedPercent = 30,
                    Points = new List<FanCurvePoint>
                    {
                        new(30, 30), new(50, 45), new(65, 65), new(75, 85), new(85, 100)
                    }
                }
            }
        };

        var performance = new ProfileData
        {
            Id = "performance-preset",
            Name = "Performance",
            Description = "Aggressive fan profiles and high responsiveness.",
            Mode = PerformanceMode.Performance,
            IsBuiltIn = true,
            RgbConfig = new RgbEffectConfig
            {
                EffectType = RgbEffectType.ColorCycle,
                Speed = 1.5,
                Brightness = 1.0,
                PrimaryColor = new RgbColor(255, 69, 0)
            },
            FanCurves = new List<FanCurve>
            {
                new()
                {
                    Name = "Aggressive Curve",
                    MinFanSpeedPercent = 40,
                    Points = new List<FanCurvePoint>
                    {
                        new(30, 40), new(45, 60), new(60, 80), new(75, 95), new(80, 100)
                    }
                }
            }
        };

        var gaming = new ProfileData
        {
            Id = "gaming-preset",
            Name = "Gaming",
            Description = "Tailored for heavy GPU/CPU gaming sessions.",
            Mode = PerformanceMode.Gaming,
            IsBuiltIn = true,
            EqualizerPresetName = "Gaming",
            RgbConfig = new RgbEffectConfig
            {
                EffectType = RgbEffectType.Wave,
                Speed = 1.8,
                Brightness = 1.0,
                PrimaryColor = RgbColor.NexusCyan,
                SecondaryColor = RgbColor.NexusPurple
            },
            FanCurves = new List<FanCurve>
            {
                new()
                {
                    Name = "Gaming Curve",
                    MinFanSpeedPercent = 45,
                    Points = new List<FanCurvePoint>
                    {
                        new(30, 45), new(50, 65), new(65, 85), new(75, 100)
                    }
                }
            }
        };

        _profiles.Add(silent);
        _profiles.Add(balanced);
        _profiles.Add(performance);
        _profiles.Add(gaming);

        _activeProfile = balanced;
    }

    public Task<ProfileData> CreateProfileAsync(string name, PerformanceMode mode)
    {
        var profile = new ProfileData
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Mode = mode,
            IsBuiltIn = false
        };

        lock (_lock)
        {
            _profiles.Add(profile);
        }

        _logger?.LogInformation("Created custom profile '{Name}' ({Id})", name, profile.Id);
        return Task.FromResult(profile);
    }

    public async Task ActivateProfileAsync(string profileId)
    {
        ProfileData? target = null;
        lock (_lock)
        {
            target = _profiles.FirstOrDefault(p => p.Id == profileId);
            if (target == null) return;

            foreach (var p in _profiles) p.IsActive = false;
            target.IsActive = true;
            _activeProfile = target;
        }

        _logger?.LogInformation("Activated profile '{Name}'", target.Name);
        ProfileChanged?.Invoke(this, target);
        await _eventBus.PublishAsync(target);
    }

    public Task UpdateProfileAsync(ProfileData profile)
    {
        lock (_lock)
        {
            var idx = _profiles.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0)
            {
                _profiles[idx] = profile;
                if (_activeProfile.Id == profile.Id)
                {
                    _activeProfile = profile;
                    ProfileChanged?.Invoke(this, profile);
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task DeleteProfileAsync(string profileId)
    {
        lock (_lock)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == profileId);
            if (p != null && !p.IsBuiltIn)
            {
                _profiles.Remove(p);
                if (_activeProfile.Id == profileId)
                {
                    _activeProfile = _profiles[0];
                    _activeProfile.IsActive = true;
                    ProfileChanged?.Invoke(this, _activeProfile);
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task<ProfileData> DuplicateProfileAsync(string profileId)
    {
        lock (_lock)
        {
            var source = _profiles.FirstOrDefault(p => p.Id == profileId) ?? _activeProfile;
            var json = JsonSerializer.Serialize(source);
            var copy = JsonSerializer.Deserialize<ProfileData>(json)!;
            copy.Id = Guid.NewGuid().ToString();
            copy.Name = $"{source.Name} (Copy)";
            copy.IsBuiltIn = false;
            copy.IsActive = false;
            _profiles.Add(copy);
            return Task.FromResult(copy);
        }
    }

    public Task<string> ExportProfileJsonAsync(string profileId)
    {
        lock (_lock)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == profileId) ?? _activeProfile;
            var json = JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true });
            return Task.FromResult(json);
        }
    }

    public Task<ProfileData> ImportProfileJsonAsync(string json)
    {
        var profile = JsonSerializer.Deserialize<ProfileData>(json);
        if (profile == null) throw new InvalidOperationException("Invalid profile JSON.");

        profile.Id = Guid.NewGuid().ToString();
        profile.IsBuiltIn = false;
        profile.IsActive = false;

        lock (_lock)
        {
            _profiles.Add(profile);
        }

        return Task.FromResult(profile);
    }
}
