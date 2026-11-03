using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexus.Abstractions.Profiles;
using Nexus.Core.ProfileManager;

namespace Nexus.App.ViewModels;

public class PerformanceModeCard
{
    public PerformanceMode Mode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AcousticsLevel { get; set; } = string.Empty;
    public string ThermalResponse { get; set; } = string.Empty;
    public string TargetProfileId { get; set; } = string.Empty;
}

public class PerformanceViewModel : ViewModelBase
{
    private readonly ProfileManager _profileManager;
    private PerformanceMode _currentMode = PerformanceMode.Balanced;

    // GPU Tuning Envelope
    private double _powerLimitPercent = 100.0;
    private double _tempTargetCelsius = 83.0;
    private int _coreClockOffsetMhz = 0;
    private int _memoryClockOffsetMhz = 0;
    private bool _isPboUnlocked = false;

    public ObservableCollection<PerformanceModeCard> Modes { get; } = new();

    public PerformanceMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(IsGamingActive));
                OnPropertyChanged(nameof(IsSilentActive));
                OnPropertyChanged(nameof(IsProductivityActive));
                OnPropertyChanged(nameof(IsNightActive));
            }
        }
    }

    private bool _isNightMode;

    public bool IsGamingActive => CurrentMode == PerformanceMode.Gaming || CurrentMode == PerformanceMode.Performance;
    public bool IsSilentActive => CurrentMode == PerformanceMode.Silent && !_isNightMode;
    public bool IsProductivityActive => CurrentMode == PerformanceMode.Balanced;
    public bool IsNightActive
    {
        get => _isNightMode;
        set
        {
            if (SetProperty(ref _isNightMode, value) && value)
            {
                CurrentMode = PerformanceMode.Silent;
            }
        }
    }

    private bool _isRgbGroupEnabled = true;
    public bool IsRgbGroupEnabled
    {
        get => _isRgbGroupEnabled;
        set => SetProperty(ref _isRgbGroupEnabled, value);
    }

    private bool _isAudioGroupEnabled = true;
    public bool IsAudioGroupEnabled
    {
        get => _isAudioGroupEnabled;
        set => SetProperty(ref _isAudioGroupEnabled, value);
    }

    private bool _isFanGroupEnabled = true;
    public bool IsFanGroupEnabled
    {
        get => _isFanGroupEnabled;
        set => SetProperty(ref _isFanGroupEnabled, value);
    }

    private bool _isLimitsGroupEnabled = true;
    public bool IsLimitsGroupEnabled
    {
        get => _isLimitsGroupEnabled;
        set => SetProperty(ref _isLimitsGroupEnabled, value);
    }

    public double PowerLimitPercent
    {
        get => _powerLimitPercent;
        set => SetProperty(ref _powerLimitPercent, Math.Round(value, 0));
    }

    public double TempTargetCelsius
    {
        get => _tempTargetCelsius;
        set => SetProperty(ref _tempTargetCelsius, Math.Round(value, 0));
    }

    public int CoreClockOffsetMhz
    {
        get => _coreClockOffsetMhz;
        set => SetProperty(ref _coreClockOffsetMhz, value);
    }

    public int MemoryClockOffsetMhz
    {
        get => _memoryClockOffsetMhz;
        set => SetProperty(ref _memoryClockOffsetMhz, value);
    }

    public bool IsPboUnlocked
    {
        get => _isPboUnlocked;
        set => SetProperty(ref _isPboUnlocked, value);
    }

    public ICommand SelectModeCommand { get; }
    public ICommand ResetDefaultsCommand { get; }

    public PerformanceViewModel(ProfileManager profileManager)
    {
        _profileManager = profileManager;

        Modes.Add(new PerformanceModeCard
        {
            Mode = PerformanceMode.Silent,
            Title = "Silent Mode",
            Description = "Acoustic priority. Caps fan RPM and minimizes background overhead.",
            AcousticsLevel = "Whisper quiet (< 24 dBA)",
            ThermalResponse = "Standard",
            TargetProfileId = "silent-preset"
        });

        Modes.Add(new PerformanceModeCard
        {
            Mode = PerformanceMode.Balanced,
            Title = "Balanced Mode",
            Description = "Dynamic curve scaling. Ideal for everyday multitasking and workloads.",
            AcousticsLevel = "Moderate (~ 32 dBA)",
            ThermalResponse = "Responsive",
            TargetProfileId = "balanced-preset"
        });

        Modes.Add(new PerformanceModeCard
        {
            Mode = PerformanceMode.Performance,
            Title = "Performance Mode",
            Description = "High fan speed thresholds and maximum thermal headroom for rendering & compilation.",
            AcousticsLevel = "Audible (~ 42 dBA)",
            ThermalResponse = "Aggressive",
            TargetProfileId = "performance-preset"
        });

        Modes.Add(new PerformanceModeCard
        {
            Mode = PerformanceMode.Gaming,
            Title = "Gaming Mode",
            Description = "Optimized GPU/CPU cooling with custom RGB synchronization and gaming audio EQ preset.",
            AcousticsLevel = "Performance dynamic",
            ThermalResponse = "Maximum",
            TargetProfileId = "gaming-preset"
        });

        SelectModeCommand = new RelayCommand(async param =>
        {
            if (param is PerformanceModeCard card)
            {
                CurrentMode = card.Mode;
                await _profileManager.ActivateProfileAsync(card.TargetProfileId);
            }
            else if (param is string str)
            {
                string presetId = "balanced-preset";
                _isNightMode = str.Equals("Night", StringComparison.OrdinalIgnoreCase);
                if (str.Equals("Gaming", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentMode = PerformanceMode.Gaming;
                    presetId = "gaming-preset";
                }
                else if (str.Equals("Silent", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentMode = PerformanceMode.Silent;
                    presetId = "silent-preset";
                }
                else if (str.Equals("Productivity", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentMode = PerformanceMode.Balanced;
                    presetId = "balanced-preset";
                }
                else if (str.Equals("Night", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentMode = PerformanceMode.Silent;
                    presetId = "silent-preset";
                }
                OnPropertyChanged(nameof(IsNightActive));
                OnPropertyChanged(nameof(IsSilentActive));
                await _profileManager.ActivateProfileAsync(presetId);
            }
        });

        ResetDefaultsCommand = new RelayCommand(() =>
        {
            PowerLimitPercent = 100;
            TempTargetCelsius = 83;
            CoreClockOffsetMhz = 0;
            MemoryClockOffsetMhz = 0;
            IsPboUnlocked = false;
        });

        _currentMode = _profileManager.ActiveProfile.Mode;
        _profileManager.ProfileChanged += (_, p) => CurrentMode = p.Mode;
    }
}
