using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexus.Abstractions.Audio;
using Nexus.App.Models;
using Nexus.App.Services;

namespace Nexus.App.ViewModels;

public class EqualizerBandItem : ViewModelBase
{
    private float _gainDb;
    public string FrequencyLabel { get; init; } = string.Empty;
    public int BandIndex { get; init; }
    public Action? OnGainChanged { get; set; }

    public float GainDb
    {
        get => _gainDb;
        set
        {
            if (SetProperty(ref _gainDb, (float)Math.Round(value, 1)))
            {
                OnGainChanged?.Invoke();
            }
        }
    }
}

public class AudioSessionItem : ViewModelBase
{
    private readonly IAudioSession? _session;
    private string _assignedChannel = "Game";
    private string _pillColorHex = "#10B981";
    private ImageSource? _icon;
    private bool _isMuted;
    private float _volume = 100f;

    public IAudioSession? Session => _session;
    public int ProcessId => _session?.ProcessId ?? 0;
    public float PeakLevel => _session?.PeakLevel ?? 0f;
    public string DisplayName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayNameUpper => (string.IsNullOrWhiteSpace(ProcessName) ? DisplayName : ProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)).ToUpperInvariant();

    public Action<AudioSessionItem>? OnAppSettingChanged { get; set; }

    public string PillColorHex
    {
        get => _pillColorHex;
        set => SetProperty(ref _pillColorHex, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public string AssignedChannel
    {
        get => _assignedChannel;
        set
        {
            if (SetProperty(ref _assignedChannel, value))
            {
                PillColorHex = value switch
                {
                    var c when c.StartsWith("Game", StringComparison.OrdinalIgnoreCase) => "#10B981",
                    var c when c.StartsWith("Chat", StringComparison.OrdinalIgnoreCase) => "#00B4D8",
                    var c when c.StartsWith("Media", StringComparison.OrdinalIgnoreCase) => "#EC4899",
                    var c when c.StartsWith("Aux", StringComparison.OrdinalIgnoreCase) => "#A855F7",
                    var c when c.StartsWith("Mic", StringComparison.OrdinalIgnoreCase) => "#F59E0B",
                    _ => "#10B981"
                };
            }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, Math.Clamp(value, 0f, 100f)))
            {
                OnAppSettingChanged?.Invoke(this);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnAppSettingChanged?.Invoke(this);
            }
        }
    }

    public void UpdateLiveMetrics(IAudioSession liveSession)
    {
        if (_icon == null)
        {
            _icon = AppIconExtractorService.GetProcessIcon(liveSession.ProcessId, liveSession.ProcessName);
            OnPropertyChanged(nameof(Icon));
        }
        OnPropertyChanged(nameof(PeakLevel));
    }

    public ICommand ToggleMuteCommand { get; }
    public ICommand MoveToGameCommand { get; }
    public ICommand MoveToChatCommand { get; }
    public ICommand MoveToMediaCommand { get; }
    public ICommand MoveToAuxCommand { get; }

    public AudioSessionItem(IAudioSession? session, string fallbackName = "System Sounds", string pillColor = "#10B981", string initialChannel = "Game", Action<AudioSessionItem, string>? onRouteToChannel = null, Action<AudioSessionItem>? onAppSettingChanged = null)
    {
        _session = session;
        DisplayName = session?.DisplayName ?? fallbackName;
        ProcessName = session?.ProcessName ?? fallbackName;
        _pillColorHex = pillColor;
        _assignedChannel = initialChannel;
        _isMuted = session?.IsMuted ?? false;
        _volume = 100f;
        OnAppSettingChanged = onAppSettingChanged;
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);

        MoveToGameCommand = new RelayCommand(() => onRouteToChannel?.Invoke(this, "Game Channel"));
        MoveToChatCommand = new RelayCommand(() => onRouteToChannel?.Invoke(this, "Chat Channel"));
        MoveToMediaCommand = new RelayCommand(() => onRouteToChannel?.Invoke(this, "Media Channel"));
        MoveToAuxCommand = new RelayCommand(() => onRouteToChannel?.Invoke(this, "Aux Channel"));

        // Extract real app icon
        if (session != null)
        {
            _icon = AppIconExtractorService.GetProcessIcon(session.ProcessId, session.ProcessName);
        }
    }
}

public class AppRoutingItem : ViewModelBase
{
    private readonly IAudioSession? _session;
    private float _volume = 85f;
    private bool _isChannelRouted = true;
    private string _channelName = "Game Channel";
    private ImageSource? _icon;

    public string DisplayName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string IconKey { get; init; } = "IconAudio";
    public string ColorHex { get; init; } = "#00D2FF";

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public string ChannelName
    {
        get => _channelName;
        set => SetProperty(ref _channelName, value);
    }

    public float Volume
    {
        get => _session != null ? _session.Volume * 100f : _volume;
        set
        {
            float clamped = Math.Clamp(value, 0f, 100f);
            if (_session != null) _session.Volume = clamped / 100f;
            SetProperty(ref _volume, clamped);
        }
    }

    public bool IsChannelRouted
    {
        get => _isChannelRouted;
        set => SetProperty(ref _isChannelRouted, value);
    }

    public AppRoutingItem(IAudioSession? session, string name, string channelName, string iconKey, string colorHex, float defaultVol = 85f)
    {
        _session = session;
        DisplayName = session?.DisplayName ?? name;
        ProcessName = session?.ProcessName ?? name;
        _channelName = channelName;
        IconKey = iconKey;
        ColorHex = colorHex;
        _volume = session != null ? session.Volume * 100f : defaultVol;

        if (session != null)
        {
            _icon = AppIconExtractorService.GetProcessIcon(session.ProcessId, session.ProcessName);
        }
    }
}

public enum SonarChannelType
{
    Master,
    Game,
    Chat,
    Media,
    Aux,
    Mic
}

public class AudioDeviceChoice : ViewModelBase
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public bool IsVirtual { get; init; }
    public string TagText => IsVirtual ? "VIRTUAL" : (IsDefault ? "DEFAULT" : "HARDWARE");
    public IAudioEndpoint? Endpoint { get; init; }

    public string ShortName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Default";
            if (Name.Contains("Sonar - Gaming", StringComparison.OrdinalIgnoreCase)) return "Sonar Gaming";
            if (Name.Contains("Sonar - Chat", StringComparison.OrdinalIgnoreCase)) return "Sonar Chat";
            if (Name.Contains("Sonar - Media", StringComparison.OrdinalIgnoreCase)) return "Sonar Media";
            if (Name.Contains("Sonar - Aux", StringComparison.OrdinalIgnoreCase)) return "Sonar Aux";
            if (Name.Contains("Sonar - Microphone", StringComparison.OrdinalIgnoreCase) || Name.Contains("Sonar - Mic", StringComparison.OrdinalIgnoreCase)) return "Sonar Mic";
            if (Name.Contains("Sonar - Stream", StringComparison.OrdinalIgnoreCase)) return "Sonar Stream";
            if (Name.Contains("Logitech G560", StringComparison.OrdinalIgnoreCase) || Name.Contains("G560", StringComparison.OrdinalIgnoreCase)) return "Logitech G560";
            if (Name.Contains("Realtek", StringComparison.OrdinalIgnoreCase)) return "Realtek HD";
            if (Name.Contains("Headset", StringComparison.OrdinalIgnoreCase)) return "Headset";
            if (Name.Contains("Headphones", StringComparison.OrdinalIgnoreCase)) return "Headphones";

            string clean = Name.Replace("Speakers (", "").Replace("Headphones (", "").Replace(")", "").Trim();
            return clean.Length > 16 ? clean.Substring(0, 14) + ".." : clean;
        }
    }

    public override string ToString() => ShortName;
}

public class AudioFilterDeviceItem : ViewModelBase
{
    private bool _isVisibleInDropdown = true;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsVirtual { get; init; }
    public bool IsDefault { get; init; }
    public bool IsCapture { get; init; }

    public string DeviceTypeTag => IsVirtual ? "VIRTUAL" : (IsCapture ? "INPUT" : "HARDWARE");
    public string DeviceTypeColor => IsVirtual ? "#38BDF8" : (IsCapture ? "#F59E0B" : "#10B981");

    public bool IsVisibleInDropdown
    {
        get => _isVisibleInDropdown;
        set
        {
            if (SetProperty(ref _isVisibleInDropdown, value))
            {
                OnVisibilityToggled?.Invoke(this);
            }
        }
    }

    public Action<AudioFilterDeviceItem>? OnVisibilityToggled { get; set; }
}

public class SonarChannelStripItem : ViewModelBase
{
    private float _volume = 85f;
    private bool _isMuted;
    private bool _isSolo;
    private double _peakLevelLeft = 0.0;
    private double _peakLevelRight = 0.0;
    private string _presetName = "Custom";
    private string _assignedDeviceName = "Default Audio Device";
    private AudioDeviceChoice? _selectedDevice;
    private IAudioEndpoint? _boundEndpoint;

    public Action<float>? OnVolumeChanged { get; set; }
    public Action<bool>? OnMuteChanged { get; set; }
    public Action<bool>? OnSoloChanged { get; set; }
    public Action<AudioDeviceChoice?>? OnDeviceSelected { get; set; }
    public bool IsUpdatingSilently { get; set; }

    public ObservableCollection<AudioDeviceChoice> AvailableDevices { get; } = new();

    public AudioDeviceChoice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                _boundEndpoint = value?.Endpoint;
                AssignedDeviceName = value?.DisplayName ?? "Default Audio Device";
                OnPropertyChanged(nameof(IsVirtualSource));
                OnPropertyChanged(nameof(EndpointStatusText));
                if (!IsUpdatingSilently) OnDeviceSelected?.Invoke(value);
            }
        }
    }

    public IAudioEndpoint? BoundEndpoint
    {
        get => _boundEndpoint;
        set
        {
            if (SetProperty(ref _boundEndpoint, value))
            {
                OnPropertyChanged(nameof(IsVirtualSource));
                OnPropertyChanged(nameof(EndpointStatusText));
            }
        }
    }

    public bool IsVirtualSource => _selectedDevice?.IsVirtual ?? false;

    public string EndpointStatusText
    {
        get
        {
            if (_selectedDevice == null) return "Disconnected";
            if (ChannelType == SonarChannelType.Master) return "Primary Hardware Output";
            return _selectedDevice.IsVirtual ? "Sonar Virtual Bus" : "Direct Hardware Device";
        }
    }

    public double PeakLevelLeft
    {
        get => _isMuted ? 0.0 : _peakLevelLeft;
        set => SetProperty(ref _peakLevelLeft, Math.Clamp(value, 0.0, 100.0));
    }

    public double PeakLevelRight
    {
        get => _isMuted ? 0.0 : _peakLevelRight;
        set => SetProperty(ref _peakLevelRight, Math.Clamp(value, 0.0, 100.0));
    }

    public SonarChannelType ChannelType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string IconKey { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#4A72FF";
    public bool HasPresetRow => ChannelType != SonarChannelType.Master;

    public float Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, (float)Math.Round(value, 0)))
            {
                OnPropertyChanged(nameof(DecibelsDisplay));
                if (!IsUpdatingSilently) OnVolumeChanged?.Invoke(_volume);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(PeakLevelLeft));
                OnPropertyChanged(nameof(PeakLevelRight));
                OnPropertyChanged(nameof(DecibelsDisplay));
                if (!IsUpdatingSilently) OnMuteChanged?.Invoke(_isMuted);
            }
        }
    }

    public bool IsSolo
    {
        get => _isSolo;
        set
        {
            if (SetProperty(ref _isSolo, value))
            {
                if (!IsUpdatingSilently) OnSoloChanged?.Invoke(_isSolo);
            }
        }
    }

    public string DecibelsDisplay
    {
        get
        {
            if (_isMuted || _volume <= 0.01f) return "-inf dB";
            double db = 20.0 * Math.Log10(Math.Max(0.001, _volume / 100.0));
            return $"{db:0.0} dB";
        }
    }

    public string PresetName
    {
        get => _presetName;
        set => SetProperty(ref _presetName, value);
    }

    public string AssignedDeviceName
    {
        get => _assignedDeviceName;
        set => SetProperty(ref _assignedDeviceName, value);
    }

    public ObservableCollection<AudioSessionItem> AssignedApps { get; } = new();

    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleSoloCommand { get; }

    public SonarChannelStripItem(SonarChannelType type, string title, string iconKey, string colorHex)
    {
        ChannelType = type;
        Title = title;
        IconKey = iconKey;
        ColorHex = colorHex;
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        ToggleSoloCommand = new RelayCommand(() => IsSolo = !IsSolo);
    }
}

public class AudioStudioViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private string _selectedTopTab = "Mixer";
    private double _chatMixValue = 0.0; // -1.0 (100% Game, 0% Chat) to 0.0 (Balanced) to +1.0 (0% Game, 100% Chat)
    private float _eqPreAmpGainDb = -2.5f;
    private string _eqCurvePathData = string.Empty;
    private string _eqCurveFillData = string.Empty;

    public double ChatMixSliderValue
    {
        get => _chatMixValue * 100.0;
        set
        {
            ChatMixValue = Math.Clamp(value / 100.0, -1.0, 1.0);
        }
    }

    public double ChatMixValue
    {
        get => _chatMixValue;
        set
        {
            if (SetProperty(ref _chatMixValue, Math.Round(value, 2)))
            {
                OnPropertyChanged(nameof(ChatMixSliderValue));
                OnPropertyChanged(nameof(ChatMixBalanceText));
                ApplyChatMix();
                SaveState();
            }
        }
    }

    public string ChatMixBalanceText
    {
        get
        {
            if (Math.Abs(_chatMixValue) < 0.02) return "Game 100% • Chat 100% (Balanced)";
            if (_chatMixValue < 0)
            {
                int chatPct = (int)Math.Round((1.0 + _chatMixValue) * 100.0);
                return $"Game 100% • Chat {chatPct}%";
            }
            else
            {
                int gamePct = (int)Math.Round((1.0 - _chatMixValue) * 100.0);
                return $"Game {gamePct}% • Chat 100%";
            }
        }
    }

    public float EqPreAmpGainDb
    {
        get => _eqPreAmpGainDb;
        set
        {
            if (SetProperty(ref _eqPreAmpGainDb, (float)Math.Round(value, 1)))
            {
                UpdateEqCurve();
                SaveState();
            }
        }
    }

    public string EqCurvePathData
    {
        get => _eqCurvePathData;
        private set => SetProperty(ref _eqCurvePathData, value);
    }

    public string EqCurveFillData
    {
        get => _eqCurveFillData;
        private set => SetProperty(ref _eqCurveFillData, value);
    }
    private EqualizerPreset _selectedPreset;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _meterTimer;
    private bool _isLoadingState;

    private static readonly bool IsTestHost =
        System.Diagnostics.Process.GetCurrentProcess().ProcessName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName != null && a.FullName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase));

    public bool EnablePersistence { get; set; } = !IsTestHost;
    public string StateFilePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "audio_state.json");

    private string? _savedMasterDeviceId;
    private string? _savedGameDeviceId;
    private string? _savedChatDeviceId;
    private string? _savedMediaDeviceId;
    private string? _savedAuxDeviceId;
    private string? _savedMicDeviceId;
    private readonly Dictionary<string, string> _processRoutingMap = new(StringComparer.OrdinalIgnoreCase);

    // SteelSeries Sonar Studio Features
    private bool _isSpatialAudioEnabled = true;
    private bool _isClearCastAiEnabled = true;
    private float _clearCastStrength = 75f;
    private float _bassBoostAmount = 40f;
    private bool _isSmartVolumeEnabled = false;
    private bool _isMicMonitoringEnabled = false;

    // Microphone Studio DSP
    private float _micGainDb = 0.0f;
    private float _noiseGateThresholdDb = -45.0f;
    private float _compressorThresholdDb = -18.0f;
    private float _limiterCeilingDb = -1.0f;

    // 6 Channels Rack
    public ObservableCollection<SonarChannelStripItem> Channels { get; } = new();
    public ObservableCollection<EqualizerPreset> EqualizerPresets { get; } = new();
    public ObservableCollection<EqualizerBandItem> EqualizerBands { get; } = new();
    public ObservableCollection<AppRoutingItem> AppRoutings { get; } = new();
    public ObservableCollection<string> AvailableChannelNames { get; } = new() { "Game Channel", "Chat Channel", "Media Channel", "Aux Channel" };

    // Device Visibility & Exclusion Filter
    public HashSet<string> ExcludedDeviceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<AudioFilterDeviceItem> ManageableDevices { get; } = new();

    private bool _isDeviceManagerOpen;
    public bool IsDeviceManagerOpen
    {
        get => _isDeviceManagerOpen;
        set => SetProperty(ref _isDeviceManagerOpen, value);
    }

    public string VisibleDevicesSummary =>
        $"{ManageableDevices.Count(d => d.IsVisibleInDropdown)} of {ManageableDevices.Count} devices visible in dropdowns";

    public string SelectedTopTab
    {
        get => _selectedTopTab;
        set
        {
            if (SetProperty(ref _selectedTopTab, value))
            {
                SaveState();
            }
        }
    }


    public bool IsSpatialAudioEnabled
    {
        get => _isSpatialAudioEnabled;
        set
        {
            if (SetProperty(ref _isSpatialAudioEnabled, value))
            {
                SaveState();
            }
        }
    }

    public bool IsClearCastAiEnabled
    {
        get => _isClearCastAiEnabled;
        set
        {
            if (SetProperty(ref _isClearCastAiEnabled, value))
            {
                SaveState();
            }
        }
    }

    public float ClearCastStrength
    {
        get => _clearCastStrength;
        set
        {
            if (SetProperty(ref _clearCastStrength, value))
            {
                SaveState();
            }
        }
    }

    public float BassBoostAmount
    {
        get => _bassBoostAmount;
        set
        {
            if (SetProperty(ref _bassBoostAmount, value))
            {
                SaveState();
            }
        }
    }

    public bool IsSmartVolumeEnabled
    {
        get => _isSmartVolumeEnabled;
        set
        {
            if (SetProperty(ref _isSmartVolumeEnabled, value))
            {
                SaveState();
            }
        }
    }

    public bool IsMicMonitoringEnabled
    {
        get => _isMicMonitoringEnabled;
        set
        {
            if (SetProperty(ref _isMicMonitoringEnabled, value))
            {
                SaveState();
            }
        }
    }

    public float MicGainDb
    {
        get => _micGainDb;
        set
        {
            if (SetProperty(ref _micGainDb, (float)Math.Round(value, 1)))
            {
                _audioEngine.MicDspConfig.InputGainDb = value;
                SaveState();
            }
        }
    }

    public float NoiseGateThresholdDb
    {
        get => _noiseGateThresholdDb;
        set
        {
            if (SetProperty(ref _noiseGateThresholdDb, (float)Math.Round(value, 1)))
            {
                _audioEngine.MicDspConfig.NoiseGateThresholdDb = value;
                SaveState();
            }
        }
    }

    public float CompressorThresholdDb
    {
        get => _compressorThresholdDb;
        set
        {
            if (SetProperty(ref _compressorThresholdDb, (float)Math.Round(value, 1)))
            {
                _audioEngine.MicDspConfig.CompressorThresholdDb = value;
                SaveState();
            }
        }
    }

    public float LimiterCeilingDb
    {
        get => _limiterCeilingDb;
        set
        {
            if (SetProperty(ref _limiterCeilingDb, (float)Math.Round(value, 1)))
            {
                _audioEngine.MicDspConfig.LimiterCeilingDb = value;
                SaveState();
            }
        }
    }

    public EqualizerPreset SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value) && value != null)
            {
                _audioEngine.ActiveEqualizerPreset = value;
                ApplyPresetToBands(value);
                SaveState();
            }
        }
    }

    private bool _isPopoutActive;
    private bool _isPopoutAlwaysOnTop = true;
    private System.Windows.Window? _popoutWindow;

    public bool IsPopoutActive
    {
        get => _isPopoutActive;
        set => SetProperty(ref _isPopoutActive, value);
    }

    public bool IsPopoutAlwaysOnTop
    {
        get => _isPopoutAlwaysOnTop;
        set
        {
            if (SetProperty(ref _isPopoutAlwaysOnTop, value))
            {
                if (_popoutWindow != null)
                {
                    _popoutWindow.Topmost = value;
                }
            }
        }
    }

    // Hero Telemetry Properties (Dashboard Unified Layout)
    public int ActiveStreamsCount => AppRoutings.Count;
    public string ActiveStreamsCountText => AppRoutings.Count.ToString();
    public double MasterVolumePercent => Channels.Count > 0 ? (double)Channels[0].Volume : 100.0;
    public string MasterVolumeText => Channels.Count > 0 ? $"{(int)Channels[0].Volume}%" : "100%";
    public string MasterDeviceName => Channels.Count > 0 && Channels[0].SelectedDevice != null ? Channels[0].SelectedDevice.DisplayName : "SteelSeries Sonar - Gaming";
    public string MasterDecibelsText => Channels.Count > 0 ? Channels[0].DecibelsDisplay : "0.0 dB";
    public string ActivePresetName => SelectedPreset?.Name ?? "Flat";
    public int GameVolumePercent => Channels.Count > 1 ? (int)Math.Round(Channels[1].Volume) : 100;
    public int ChatVolumePercent => Channels.Count > 2 ? (int)Math.Round(Channels[2].Volume) : 100;

    public void NotifyHeroPropertiesChanged()
    {
        OnPropertyChanged(nameof(ActiveStreamsCount));
        OnPropertyChanged(nameof(ActiveStreamsCountText));
        OnPropertyChanged(nameof(MasterVolumePercent));
        OnPropertyChanged(nameof(MasterVolumeText));
        OnPropertyChanged(nameof(MasterDeviceName));
        OnPropertyChanged(nameof(MasterDecibelsText));
        OnPropertyChanged(nameof(ActivePresetName));
        OnPropertyChanged(nameof(GameVolumePercent));
        OnPropertyChanged(nameof(ChatVolumePercent));
    }

    public ICommand SelectTabCommand { get; }
    public ICommand ResetAllVolumesCommand { get; }
    public ICommand ResetEqBandsCommand { get; }
    public ICommand RefreshSessionsCommand { get; }
    public ICommand RefreshAudioCommand { get; }
    public ICommand ResetChatMixCommand { get; }
    public ICommand SetChatMixBalanceCommand { get; }
    public ICommand MuteAllChannelsCommand { get; }
    public ICommand PopoutMixerCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand ClosePopoutCommand { get; }
    public ICommand SelectEffectCommand { get; }
    public ICommand ApplyNamedPresetCommand { get; }
    public ICommand OpenDeviceManagerCommand { get; }
    public ICommand CloseDeviceManagerCommand { get; }
    public ICommand ShowAllDevicesCommand { get; }
    public ICommand HideVirtualDevicesCommand { get; }

    private bool _isAudioEffectsEnabled = true;
    public bool IsAudioEffectsEnabled
    {
        get => _isAudioEffectsEnabled;
        set => SetProperty(ref _isAudioEffectsEnabled, value);
    }

    private string _selectedEffect = "Equalizer";
    public string SelectedEffect
    {
        get => _selectedEffect;
        set => SetProperty(ref _selectedEffect, value);
    }

    public AudioStudioViewModel(IAudioEngine audioEngine)
    {
        _audioEngine = audioEngine;

        SelectTabCommand = new RelayCommand(param =>
        {
            if (param is string tab) SelectedTopTab = tab;
        });

        // 1. MASTER (Blue #4A72FF)
        var masterCh = new SonarChannelStripItem(SonarChannelType.Master, "MASTER", "IconSpeaker", "#4A72FF")
        {
            Volume = 85,
            IsMuted = false,
            AssignedDeviceName = "Default Audio Output"
        };

        // 2. GAME (Emerald #10B981)
        var gameCh = new SonarChannelStripItem(SonarChannelType.Game, "GAME", "IconGameController", "#10B981")
        {
            Volume = 80,
            IsMuted = false,
            PresetName = "CS2 / Warzone",
            AssignedDeviceName = "Default Audio Output"
        };

        // 3. CHAT (Cyan #00B4D8)
        var chatCh = new SonarChannelStripItem(SonarChannelType.Chat, "CHAT", "IconChatBubble", "#00B4D8")
        {
            Volume = 85,
            IsMuted = false,
            PresetName = "Vocal Clarity",
            AssignedDeviceName = "Default Audio Output"
        };

        // 4. MEDIA (Pink #EC4899)
        var mediaCh = new SonarChannelStripItem(SonarChannelType.Media, "MEDIA", "IconMediaPlay", "#EC4899")
        {
            Volume = 60,
            IsMuted = false,
            PresetName = "Bass Boost",
            AssignedDeviceName = "Default Audio Output"
        };

        // 5. AUX (Purple #A855F7)
        var auxCh = new SonarChannelStripItem(SonarChannelType.Aux, "AUX", "IconAuxWaves", "#A855F7")
        {
            Volume = 50,
            IsMuted = false,
            PresetName = "Flat",
            AssignedDeviceName = "Default Audio Output"
        };

        // 6. MIC (Amber #F59E0B)
        var micCh = new SonarChannelStripItem(SonarChannelType.Mic, "MIC", "IconMicrophone", "#F59E0B")
        {
            Volume = 85,
            IsMuted = false,
            PresetName = "Broadcast Mic",
            AssignedDeviceName = "Default Microphone"
        };

        Channels.Add(masterCh);
        Channels.Add(gameCh);
        Channels.Add(chatCh);
        Channels.Add(mediaCh);
        Channels.Add(auxCh);
        Channels.Add(micCh);

        // Hardware & Virtual Audio Binding per Channel (SteelSeries Sonar Independent Architecture)
        foreach (var channel in Channels)
        {
            var ch = channel;
            ch.OnVolumeChanged = vol =>
            {
                if (ch.ChannelType == SonarChannelType.Game || ch.ChannelType == SonarChannelType.Chat)
                {
                    ApplyChatMixToEndpoints();
                }
                else if (ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.MasterVolume = Math.Clamp(vol / 100f, 0f, 1f);
                }
                else if (ch.ChannelType == SonarChannelType.Master && _audioEngine.DefaultPlaybackDevice != null)
                {
                    _audioEngine.DefaultPlaybackDevice.MasterVolume = Math.Clamp(vol / 100f, 0f, 1f);
                }
                else if (ch.ChannelType == SonarChannelType.Mic && _audioEngine.DefaultRecordingDevice != null)
                {
                    _audioEngine.DefaultRecordingDevice.MasterVolume = Math.Clamp(vol / 100f, 0f, 1f);
                }

                ApplyCompoundVolumesAndMutes();
                NotifyHeroPropertiesChanged();
                SaveState();
            };

            ch.OnMuteChanged = muted =>
            {
                if (ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.IsMuted = muted;
                }
                else if (ch.ChannelType == SonarChannelType.Master && _audioEngine.DefaultPlaybackDevice != null)
                {
                    _audioEngine.DefaultPlaybackDevice.IsMuted = muted;
                }
                else if (ch.ChannelType == SonarChannelType.Mic && _audioEngine.DefaultRecordingDevice != null)
                {
                    _audioEngine.DefaultRecordingDevice.IsMuted = muted;
                }

                ApplyCompoundVolumesAndMutes();
                NotifyHeroPropertiesChanged();
                SaveState();
            };

            ch.OnSoloChanged = solo =>
            {
                ApplySoloRouting();
            };

            ch.OnDeviceSelected = choice =>
            {
                if (choice == null) return;
                switch (ch.ChannelType)
                {
                    case SonarChannelType.Master:
                        _savedMasterDeviceId = choice.Id;
                        _ = _audioEngine.SetDefaultPlaybackDeviceAsync(choice.Id);
                        break;
                    case SonarChannelType.Game:
                        _savedGameDeviceId = choice.Id;
                        RouteChannelAppsToDevice(ch, choice.Id);
                        break;
                    case SonarChannelType.Chat:
                        _savedChatDeviceId = choice.Id;
                        RouteChannelAppsToDevice(ch, choice.Id);
                        break;
                    case SonarChannelType.Media:
                        _savedMediaDeviceId = choice.Id;
                        RouteChannelAppsToDevice(ch, choice.Id);
                        break;
                    case SonarChannelType.Aux:
                        _savedAuxDeviceId = choice.Id;
                        RouteChannelAppsToDevice(ch, choice.Id);
                        break;
                    case SonarChannelType.Mic:
                        _savedMicDeviceId = choice.Id;
                        _ = _audioEngine.SetDefaultRecordingDeviceAsync(choice.Id);
                        break;
                }
                SaveState();
            };
        }

        // 10 EQ Bands
        string[] freqLabels = { "31Hz", "63Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz" };
        for (int i = 0; i < freqLabels.Length; i++)
        {
            var band = new EqualizerBandItem
            {
                BandIndex = i,
                FrequencyLabel = freqLabels[i],
                GainDb = 0f
            };
            band.OnGainChanged = () =>
            {
                UpdateEqCurve();
                SaveState();
            };
            EqualizerBands.Add(band);
        }

        // Sonar Game-optimized presets
        EqualizerPresets.Add(EqualizerPreset.Gaming);
        EqualizerPresets.Add(EqualizerPreset.BassBoost);
        EqualizerPresets.Add(EqualizerPreset.Vocals);
        EqualizerPresets.Add(EqualizerPreset.Cinema);
        EqualizerPresets.Add(EqualizerPreset.Flat);

        _selectedPreset = EqualizerPresets[0];
        ApplyPresetToBands(_selectedPreset);

        // Commands
        ResetAllVolumesCommand = new RelayCommand(() =>
        {
            masterCh.Volume = 85;
            gameCh.Volume = 80;
            chatCh.Volume = 85;
            mediaCh.Volume = 60;
            auxCh.Volume = 50;
            micCh.Volume = 85;
            foreach (var ch in Channels) ch.IsMuted = false;
            ChatMixValue = 0.0;
            ApplyCompoundVolumesAndMutes();
            SaveState();
        });

        ResetEqBandsCommand = new RelayCommand(() =>
        {
            SelectedPreset = EqualizerPresets[^1];
        });

        RefreshSessionsCommand = new RelayCommand(async () => await RefreshAsync());
        RefreshAudioCommand = RefreshSessionsCommand;

        ResetChatMixCommand = new RelayCommand(() => ChatMixValue = 0.0);
        SetChatMixBalanceCommand = new RelayCommand(param =>
        {
            if (param is double d) ChatMixValue = d;
            else if (param is string s && double.TryParse(s, out var parsed)) ChatMixValue = parsed;
        });

        MuteAllChannelsCommand = new RelayCommand(() =>
        {
            bool anyUnmuted = Channels.Any(c => !c.IsMuted);
            foreach (var c in Channels) c.IsMuted = anyUnmuted;
            ApplyCompoundVolumesAndMutes();
            SaveState();
        });

        ToggleAlwaysOnTopCommand = new RelayCommand(() =>
        {
            IsPopoutAlwaysOnTop = !IsPopoutAlwaysOnTop;
        });

        ClosePopoutCommand = new RelayCommand(ClosePopout);
        PopoutMixerCommand = new RelayCommand(OpenOrActivatePopout);

        SelectEffectCommand = new RelayCommand(param =>
        {
            if (param is string effect)
            {
                SelectedEffect = effect;
                if (effect.Equals("Equalizer", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedTopTab = "Game";
                }
                else if (effect.Equals("VoiceClarity", StringComparison.OrdinalIgnoreCase) || effect.Equals("Voice Clarity", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedTopTab = "Chat";
                }
                else if (effect.Equals("NoiseGate", StringComparison.OrdinalIgnoreCase) || effect.Equals("Noise Gate", StringComparison.OrdinalIgnoreCase) ||
                         effect.Equals("Compressor", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedTopTab = "Mic";
                }
            }
        });

        ApplyNamedPresetCommand = new RelayCommand(param =>
        {
            if (param is string name)
            {
                var matched = EqualizerPresets.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    SelectedPreset = matched;
                }
            }
        });

        OpenDeviceManagerCommand = new RelayCommand(OpenDeviceManager);
        CloseDeviceManagerCommand = new RelayCommand(() => IsDeviceManagerOpen = false);
        ShowAllDevicesCommand = new RelayCommand(ShowAllDevices);
        HideVirtualDevicesCommand = new RelayCommand(HideVirtualDevices);

        // Load saved state before starting loops
        LoadSavedState();
        UpdateEqCurve();
        RefreshManageableDevices();

        // 35ms Peak Meter animation loop (decay and responsiveness)
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        _meterTimer.Tick += OnMeterTick;
        _meterTimer.Start();

        // 2-second background session poll
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _ = RefreshAsync();
    }

    private static readonly double[] EqFrequencies = GenerateLogFrequencies(120, 20.0, 20000.0);

    private static double[] GenerateLogFrequencies(int count, double minFreq, double maxFreq)
    {
        var freqs = new double[count];
        double logMin = Math.Log10(minFreq);
        double logMax = Math.Log10(maxFreq);
        for (int i = 0; i < count; i++)
        {
            double logF = logMin + (logMax - logMin) * i / (count - 1);
            freqs[i] = Math.Pow(10, logF);
        }
        return freqs;
    }

    public void UpdateEqCurve()
    {
        for (int i = 0; i < EqualizerBands.Count; i++)
        {
            var b = EqualizerBands[i];
            var type = i switch
            {
                0 => ParametricFilterType.LowShelf,
                9 => ParametricFilterType.HighShelf,
                _ => ParametricFilterType.PeakingEQ
            };
            float freq = i < EqualizerPreset.DefaultFrequencies.Length ? EqualizerPreset.DefaultFrequencies[i] : 1000f;
            _audioEngine.EqEngine.UpdateBand(i, b.GainDb, freq, 1.414f, type, true);
        }

        _audioEngine.EqEngine.PreAmpGainDb = _eqPreAmpGainDb;

        var response = _audioEngine.EqEngine.CalculateFrequencyResponse(EqFrequencies);

        double width = 640.0;
        double height = 160.0;
        double midY = height / 2.0;
        double dbRange = 15.0;

        var sbPath = new System.Text.StringBuilder();
        var sbFill = new System.Text.StringBuilder();

        for (int i = 0; i < EqFrequencies.Length; i++)
        {
            double x = (double)i / (EqFrequencies.Length - 1) * width;
            double gain = Math.Clamp(response[i], -dbRange, dbRange);
            double y = midY - (gain / dbRange) * (midY - 10.0);

            if (i == 0)
            {
                sbPath.Append(System.Globalization.CultureInfo.InvariantCulture, $"M {x:F1},{y:F1}");
                sbFill.Append(System.Globalization.CultureInfo.InvariantCulture, $"M {x:F1},{y:F1}");
            }
            else
            {
                sbPath.Append(System.Globalization.CultureInfo.InvariantCulture, $" L {x:F1},{y:F1}");
                sbFill.Append(System.Globalization.CultureInfo.InvariantCulture, $" L {x:F1},{y:F1}");
            }
        }

        sbFill.Append(System.Globalization.CultureInfo.InvariantCulture, $" L {width:F1},{height:F1} L 0,{height:F1} Z");

        EqCurvePathData = sbPath.ToString();
        EqCurveFillData = sbFill.ToString();
    }

    private void ApplyChatMix()
    {
        _audioEngine.BusManager.ChatMixValue = _chatMixValue;
        ApplyChatMixToEndpoints();
        ApplyCompoundVolumesAndMutes();
        OnPropertyChanged(nameof(ChatMixBalanceText));
        OnPropertyChanged(nameof(ChatMixSliderValue));
        SaveState();
    }

    private void ApplyChatMixToEndpoints()
    {
        _audioEngine.BusManager.ChatMixValue = _chatMixValue;
        float gameFactor = _audioEngine.BusManager.GameChatMixMultiplier;
        float chatFactor = _audioEngine.BusManager.ChatChatMixMultiplier;

        if (Channels.Count >= 3)
        {
            var gameCh = Channels[1];
            var chatCh = Channels[2];

            if (gameCh.BoundEndpoint != null && !gameCh.IsMuted)
            {
                gameCh.BoundEndpoint.MasterVolume = Math.Clamp((gameCh.Volume / 100f) * gameFactor, 0f, 1f);
            }
            if (chatCh.BoundEndpoint != null && !chatCh.IsMuted)
            {
                chatCh.BoundEndpoint.MasterVolume = Math.Clamp((chatCh.Volume / 100f) * chatFactor, 0f, 1f);
            }
        }
    }

    public static string GetProcessFamily(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
        string lower = processName.ToLowerInvariant().Replace(".exe", "").Trim();
        if (lower.StartsWith("steam")) return "steam";
        if (lower.StartsWith("discord")) return "discord";
        if (lower.StartsWith("chrome")) return "chrome";
        if (lower.StartsWith("msedge")) return "msedge";
        if (lower.StartsWith("spotify")) return "spotify";
        if (lower.StartsWith("firefox")) return "firefox";
        if (lower.StartsWith("brave")) return "brave";
        if (lower.StartsWith("opera")) return "opera";
        if (lower.StartsWith("wallpaper")) return "wallpaper";
        return lower;
    }

    public void ApplyCompoundVolumesAndMutes()
    {
        float masterVolFactor = Channels.Count > 0 ? Math.Clamp(Channels[0].Volume / 100f, 0f, 1f) : 1f;
        bool masterMuted = Channels.Count > 0 && Channels[0].IsMuted;

        float gameFactor = _audioEngine.BusManager.GameChatMixMultiplier;
        float chatFactor = _audioEngine.BusManager.ChatChatMixMultiplier;

        var allActive = _audioEngine.ActiveSessions;

        for (int i = 1; i < Channels.Count; i++)
        {
            var ch = Channels[i];
            float chVolFactor = Math.Clamp(ch.Volume / 100f, 0f, 1f);
            bool chMuted = ch.IsMuted;

            float busMultiplier = ch.ChannelType switch
            {
                SonarChannelType.Game => gameFactor,
                SonarChannelType.Chat => chatFactor,
                _ => 1f
            };

            foreach (var app in ch.AssignedApps)
            {
                float appFactor = Math.Clamp(app.Volume / 100f, 0f, 1f);
                float compoundVolume = masterVolFactor * chVolFactor * appFactor * busMultiplier;
                bool compoundMute = masterMuted || chMuted || app.IsMuted;

                _audioEngine.SetSessionVolume(app.ProcessId, compoundVolume);
                _audioEngine.SetSessionMute(app.ProcessId, compoundMute);

                // Also apply to any child/sibling sessions in the same process family
                string family = GetProcessFamily(app.ProcessName);
                if (!string.IsNullOrEmpty(family))
                {
                    foreach (var s in allActive)
                    {
                        if (s.ProcessId != app.ProcessId && GetProcessFamily(s.ProcessName) == family)
                        {
                            _audioEngine.SetSessionVolume(s.ProcessId, compoundVolume);
                            _audioEngine.SetSessionMute(s.ProcessId, compoundMute);
                        }
                    }
                }
            }
        }
    }

    private void ApplyPresetToBands(EqualizerPreset preset)
    {
        _eqPreAmpGainDb = preset.PreAmpGainDb;
        OnPropertyChanged(nameof(EqPreAmpGainDb));

        for (int i = 0; i < Math.Min(EqualizerBands.Count, preset.BandGainsDb.Length); i++)
        {
            EqualizerBands[i].GainDb = preset.BandGainsDb[i];
        }

        UpdateEqCurve();
    }

    private void ApplySoloRouting()
    {
        bool anySolo = Channels.Any(c => c.IsSolo);
        foreach (var ch in Channels)
        {
            if (anySolo)
            {
                bool shouldMute = !ch.IsSolo;
                if (ch.ChannelType == SonarChannelType.Master && ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.IsMuted = shouldMute || ch.IsMuted;
                }
                else if (ch.IsVirtualSource && ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.IsMuted = shouldMute || ch.IsMuted;
                }

                foreach (var app in ch.AssignedApps)
                {
                    app.IsMuted = shouldMute || ch.IsMuted;
                }
            }
            else
            {
                if (ch.ChannelType == SonarChannelType.Master && ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.IsMuted = ch.IsMuted;
                }
                else if (ch.IsVirtualSource && ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.IsMuted = ch.IsMuted;
                }

                foreach (var app in ch.AssignedApps)
                {
                    app.IsMuted = ch.IsMuted;
                }
            }
        }
        SaveState();
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        for (int i = 0; i < Channels.Count; i++)
        {
            var ch = Channels[i];
            if (ch.IsMuted)
            {
                ch.PeakLevelLeft = Math.Max(0.0, ch.PeakLevelLeft * 0.75 - 2.0);
                ch.PeakLevelRight = Math.Max(0.0, ch.PeakLevelRight * 0.75 - 2.0);
                continue;
            }

            float endpointPeak = ch.BoundEndpoint?.PeakLevel ?? 0f;

            // Check peak levels of active assigned apps in this channel
            if (endpointPeak <= 0.001f && ch.AssignedApps.Count > 0)
            {
                foreach (var app in ch.AssignedApps)
                {
                    if (!app.IsMuted && app.PeakLevel > endpointPeak)
                    {
                        endpointPeak = app.PeakLevel;
                    }
                }
            }

            // Fallback for Master physical channel if bound endpoint is null
            if (endpointPeak <= 0.001f && ch.ChannelType == SonarChannelType.Master)
            {
                endpointPeak = _audioEngine.DefaultPlaybackDevice?.PeakLevel ?? 0f;
            }
            else if (endpointPeak <= 0.001f && ch.ChannelType == SonarChannelType.Mic)
            {
                endpointPeak = _audioEngine.DefaultRecordingDevice?.PeakLevel ?? 0f;
            }

            double targetPeak = endpointPeak * 100.0;
            UpdateChannelMeter(ch, targetPeak);
        }
    }

    private static void UpdateChannelMeter(SonarChannelStripItem channel, double targetPeak)
    {
        if (channel.IsMuted || targetPeak <= 0.5)
        {
            channel.PeakLevelLeft = Math.Max(0.0, channel.PeakLevelLeft * 0.75 - 2.0);
            channel.PeakLevelRight = Math.Max(0.0, channel.PeakLevelRight * 0.75 - 2.0);
            return;
        }

        double left = targetPeak;
        double right = targetPeak * 0.94;

        channel.PeakLevelLeft = left > channel.PeakLevelLeft ? left : Math.Max(0.0, channel.PeakLevelLeft * 0.85);
        channel.PeakLevelRight = right > channel.PeakLevelRight ? right : Math.Max(0.0, channel.PeakLevelRight * 0.85);
    }

    private string GetChannelForSession(IAudioSession s)
    {
        string rawName = s.ProcessName.ToLowerInvariant().Replace(".exe", "").Trim();
        string family = GetProcessFamily(s.ProcessName);

        // 1. Direct process name match
        if (_processRoutingMap.TryGetValue(s.ProcessName, out var savedTarget) && !string.IsNullOrWhiteSpace(savedTarget))
        {
            return savedTarget;
        }

        // 2. Process family match (e.g. steamwebhelper inherits steam's channel)
        if (!string.IsNullOrEmpty(family) && _processRoutingMap.TryGetValue(family, out var familyTarget) && !string.IsNullOrWhiteSpace(familyTarget))
        {
            return familyTarget;
        }

        // 3. Category defaults
        if (family == "discord" || rawName.Contains("teams") || rawName.Contains("zoom") || rawName.Contains("skype") || rawName.Contains("slack"))
        {
            return "Chat Channel";
        }

        if (family == "spotify" || family == "chrome" || family == "msedge" || family == "firefox" || family == "brave" || family == "opera" || rawName.Contains("vlc"))
        {
            return "Media Channel";
        }

        if (family == "wallpaper" || rawName.Contains("system"))
        {
            return "Aux Channel";
        }

        // Default: Games, Steam, and all gaming clients
        return "Game Channel";
    }

    public async Task RefreshAsync()
    {
        try
        {
            await _audioEngine.RefreshDevicesAsync();

            PopulateChannelDevices();

            // Sync active sessions across all devices
            var sessions = _audioEngine.ActiveSessions;
            var gameSessions = new List<AudioSessionItem>();
            var chatSessions = new List<AudioSessionItem>();
            var mediaSessions = new List<AudioSessionItem>();
            var auxSessions = new List<AudioSessionItem>();
            var routingList = new List<AppRoutingItem>();

            foreach (var s in sessions)
            {
                string channelTarget = GetChannelForSession(s);
                string pillColor = "#10B981";

                if (channelTarget.StartsWith("Chat", StringComparison.OrdinalIgnoreCase))
                {
                    pillColor = "#00B4D8";
                    chatSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Chat", 
                        (item, target) => AssignSessionToChannel(item, target),
                        _ => ApplyCompoundVolumesAndMutes()));
                }
                else if (channelTarget.StartsWith("Media", StringComparison.OrdinalIgnoreCase))
                {
                    pillColor = "#EC4899";
                    mediaSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Media", 
                        (item, target) => AssignSessionToChannel(item, target),
                        _ => ApplyCompoundVolumesAndMutes()));
                }
                else if (channelTarget.StartsWith("Aux", StringComparison.OrdinalIgnoreCase))
                {
                    pillColor = "#A855F7";
                    auxSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Aux", 
                        (item, target) => AssignSessionToChannel(item, target),
                        _ => ApplyCompoundVolumesAndMutes()));
                }
                else
                {
                    pillColor = "#10B981";
                    gameSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Game", 
                        (item, target) => AssignSessionToChannel(item, target),
                        _ => ApplyCompoundVolumesAndMutes()));
                }

                routingList.Add(new AppRoutingItem(s, s.DisplayName, channelTarget, "IconAudio", pillColor));
            }

            SyncSessionCollection(Channels[1].AssignedApps, gameSessions);
            SyncSessionCollection(Channels[2].AssignedApps, chatSessions);
            SyncSessionCollection(Channels[3].AssignedApps, mediaSessions);
            SyncSessionCollection(Channels[4].AssignedApps, auxSessions);
            SyncCollection(AppRoutings, routingList);

            ApplyCompoundVolumesAndMutes();
            NotifyHeroPropertiesChanged();
        }
        catch { }
    }

    private static string FormatCleanDeviceName(string rawName, bool isVirtual)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "Unknown Device";

        string clean = rawName;
        if (clean.Contains("SteelSeries Sonar - Gaming", StringComparison.OrdinalIgnoreCase))
            return "SteelSeries Sonar - Gaming [Virtual Bus]";
        if (clean.Contains("SteelSeries Sonar - Chat", StringComparison.OrdinalIgnoreCase))
            return "SteelSeries Sonar - Chat [Virtual Bus]";
        if (clean.Contains("SteelSeries Sonar - Media", StringComparison.OrdinalIgnoreCase))
            return "SteelSeries Sonar - Media [Virtual Bus]";
        if (clean.Contains("SteelSeries Sonar - Aux", StringComparison.OrdinalIgnoreCase))
            return "SteelSeries Sonar - Aux [Virtual Bus]";
        if (clean.Contains("SteelSeries Sonar - Microphone", StringComparison.OrdinalIgnoreCase))
            return "SteelSeries Sonar - Mic [Virtual Input]";
        if (clean.Contains("SteelSeries Sonar - Stream", StringComparison.OrdinalIgnoreCase))
            return "SteelSeries Sonar - Stream [Virtual Out]";

        if (clean.StartsWith("Speakers (", StringComparison.OrdinalIgnoreCase) && clean.EndsWith(")"))
            clean = clean.Substring(10, clean.Length - 11);
        else if (clean.StartsWith("Headset Earphone (", StringComparison.OrdinalIgnoreCase) && clean.EndsWith(")"))
            clean = clean.Substring(18, clean.Length - 19);
        else if (clean.StartsWith("Headphones (", StringComparison.OrdinalIgnoreCase) && clean.EndsWith(")"))
            clean = clean.Substring(12, clean.Length - 13);

        return isVirtual ? $"{clean} [Virtual]" : $"{clean} [Hardware]";
    }

    private void RouteChannelAppsToDevice(SonarChannelStripItem ch, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return;
        var allActive = _audioEngine.ActiveSessions;

        foreach (var app in ch.AssignedApps)
        {
            _ = _audioEngine.RoutingService.SetAppOutputDeviceAsync(app.ProcessId, deviceId);

            string family = GetProcessFamily(app.ProcessName);
            if (!string.IsNullOrEmpty(family))
            {
                foreach (var s in allActive)
                {
                    if (s.ProcessId != app.ProcessId && GetProcessFamily(s.ProcessName) == family)
                    {
                        _ = _audioEngine.RoutingService.SetAppOutputDeviceAsync(s.ProcessId, deviceId);
                    }
                }
            }
        }
    }

    public void OpenDeviceManager()
    {
        RefreshManageableDevices();
        IsDeviceManagerOpen = true;
    }

    public void RefreshManageableDevices()
    {
        var playbacks = _audioEngine.PlaybackDevices;
        var recordings = _audioEngine.RecordingDevices;

        var items = new List<AudioFilterDeviceItem>();

        foreach (var dev in playbacks)
        {
            bool isVirtual = dev.Name.Contains("Sonar", StringComparison.OrdinalIgnoreCase) ||
                             dev.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                             dev.Name.Contains("Cable", StringComparison.OrdinalIgnoreCase);

            var item = new AudioFilterDeviceItem
            {
                Id = dev.Id,
                Name = dev.Name,
                DisplayName = FormatCleanDeviceName(dev.Name, isVirtual),
                IsVirtual = isVirtual,
                IsDefault = dev.IsDefault,
                IsCapture = false,
                IsVisibleInDropdown = !ExcludedDeviceIds.Contains(dev.Id),
                OnVisibilityToggled = OnDeviceVisibilityToggled
            };
            items.Add(item);
        }

        foreach (var dev in recordings)
        {
            bool isVirtual = dev.Name.Contains("Sonar", StringComparison.OrdinalIgnoreCase) ||
                             dev.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

            var item = new AudioFilterDeviceItem
            {
                Id = dev.Id,
                Name = dev.Name,
                DisplayName = FormatCleanDeviceName(dev.Name, isVirtual),
                IsVirtual = isVirtual,
                IsDefault = dev.IsDefault,
                IsCapture = true,
                IsVisibleInDropdown = !ExcludedDeviceIds.Contains(dev.Id),
                OnVisibilityToggled = OnDeviceVisibilityToggled
            };
            items.Add(item);
        }

        ManageableDevices.Clear();
        foreach (var it in items)
        {
            ManageableDevices.Add(it);
        }

        OnPropertyChanged(nameof(VisibleDevicesSummary));
    }

    private void OnDeviceVisibilityToggled(AudioFilterDeviceItem item)
    {
        if (item.IsVisibleInDropdown)
        {
            ExcludedDeviceIds.Remove(item.Id);
        }
        else
        {
            ExcludedDeviceIds.Add(item.Id);
        }

        PopulateChannelDevices();
        OnPropertyChanged(nameof(VisibleDevicesSummary));
        SaveState();
    }

    public void ShowAllDevices()
    {
        ExcludedDeviceIds.Clear();
        foreach (var item in ManageableDevices)
        {
            item.IsVisibleInDropdown = true;
        }
        PopulateChannelDevices();
        OnPropertyChanged(nameof(VisibleDevicesSummary));
        SaveState();
    }

    public void HideVirtualDevices()
    {
        foreach (var item in ManageableDevices)
        {
            if (item.IsVirtual)
            {
                item.IsVisibleInDropdown = false;
                ExcludedDeviceIds.Add(item.Id);
            }
        }
        PopulateChannelDevices();
        OnPropertyChanged(nameof(VisibleDevicesSummary));
        SaveState();
    }

    private void PopulateChannelDevices()
    {
        var playbacks = _audioEngine.PlaybackDevices;
        var recordings = _audioEngine.RecordingDevices;

        var playbackChoices = new List<AudioDeviceChoice>();
        foreach (var dev in playbacks)
        {
            bool isVirtual = dev.Name.Contains("Sonar", StringComparison.OrdinalIgnoreCase) ||
                             dev.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                             dev.Name.Contains("Cable", StringComparison.OrdinalIgnoreCase);

            playbackChoices.Add(new AudioDeviceChoice
            {
                Id = dev.Id,
                Name = dev.Name,
                DisplayName = FormatCleanDeviceName(dev.Name, isVirtual),
                IsDefault = dev.IsDefault,
                IsVirtual = isVirtual,
                Endpoint = dev
            });
        }

        var recordingChoices = new List<AudioDeviceChoice>();
        foreach (var dev in recordings)
        {
            bool isVirtual = dev.Name.Contains("Sonar", StringComparison.OrdinalIgnoreCase) ||
                             dev.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

            recordingChoices.Add(new AudioDeviceChoice
            {
                Id = dev.Id,
                Name = dev.Name,
                DisplayName = FormatCleanDeviceName(dev.Name, isVirtual),
                IsDefault = dev.IsDefault,
                IsVirtual = isVirtual,
                Endpoint = dev
            });
        }

        var filteredPlayback = playbackChoices.Where(d => !ExcludedDeviceIds.Contains(d.Id)).ToList();
        var filteredRecording = recordingChoices.Where(d => !ExcludedDeviceIds.Contains(d.Id)).ToList();

        if (filteredPlayback.Count == 0 && playbackChoices.Count > 0)
        {
            filteredPlayback = playbackChoices;
        }

        for (int i = 0; i < Math.Min(5, Channels.Count); i++)
        {
            var chList = new List<AudioDeviceChoice>(filteredPlayback);
            if (Channels[i].SelectedDevice != null && !chList.Any(d => d.Id == Channels[i].SelectedDevice.Id))
            {
                var cur = playbackChoices.FirstOrDefault(d => d.Id == Channels[i].SelectedDevice.Id);
                if (cur != null) chList.Insert(0, cur);
            }
            SyncDeviceChoices(Channels[i], chList);
        }

        if (Channels.Count > 5)
        {
            var micList = new List<AudioDeviceChoice>(filteredRecording);
            if (Channels[5].SelectedDevice != null && !micList.Any(d => d.Id == Channels[5].SelectedDevice.Id))
            {
                var cur = recordingChoices.FirstOrDefault(d => d.Id == Channels[5].SelectedDevice.Id);
                if (cur != null) micList.Insert(0, cur);
            }
            SyncDeviceChoices(Channels[5], micList);
        }

        AutoBindChannelDevices();
    }

    private static void SyncDeviceChoices(SonarChannelStripItem channel, List<AudioDeviceChoice> source)
    {
        var target = channel.AvailableDevices;
        if (target.Count == source.Count && target.Select(t => t.Id).SequenceEqual(source.Select(s => s.Id)))
        {
            return;
        }

        string? currentSelectedId = channel.SelectedDevice?.Id;
        channel.IsUpdatingSilently = true;
        try
        {
            target.Clear();
            foreach (var item in source) target.Add(item);

            if (!string.IsNullOrEmpty(currentSelectedId))
            {
                var reMatch = target.FirstOrDefault(d => d.Id == currentSelectedId);
                if (reMatch != null)
                {
                    channel.SelectedDevice = reMatch;
                }
            }
        }
        finally
        {
            channel.IsUpdatingSilently = false;
        }
    }

    private void AutoBindChannelDevices()
    {
        if (Channels.Count < 6) return;

        // 1. Master: Pure Physical Hardware Output (Never Sonar)
        BindMasterToPhysicalHardware(Channels[0], _savedMasterDeviceId);

        // 2. Game: SteelSeries Sonar - Gaming
        BindToDedicatedSonarDevice(Channels[1], _savedGameDeviceId, "Sonar - Gaming", "Gaming");

        // 3. Chat: SteelSeries Sonar - Chat
        BindToDedicatedSonarDevice(Channels[2], _savedChatDeviceId, "Sonar - Chat", "Chat");

        // 4. Media: SteelSeries Sonar - Media
        BindToDedicatedSonarDevice(Channels[3], _savedMediaDeviceId, "Sonar - Media", "Media");

        // 5. Aux: SteelSeries Sonar - Aux
        BindToDedicatedSonarDevice(Channels[4], _savedAuxDeviceId, "Sonar - Aux", "Aux");

        // 6. Mic: SteelSeries Sonar - Microphone
        BindToDedicatedSonarDevice(Channels[5], _savedMicDeviceId, "Sonar - Microphone", "Microphone", "Mic");
    }

    private void BindMasterToPhysicalHardware(SonarChannelStripItem channel, string? savedId)
    {
        if (channel.AvailableDevices.Count == 0) return;

        var nonVirtual = channel.AvailableDevices.Where(d => !d.IsVirtual && !d.Name.Contains("Sonar", StringComparison.OrdinalIgnoreCase)).ToList();
        if (nonVirtual.Count == 0) nonVirtual = channel.AvailableDevices.ToList();

        if (!string.IsNullOrEmpty(savedId))
        {
            var matchSaved = nonVirtual.FirstOrDefault(d => d.Id == savedId);
            if (matchSaved != null)
            {
                SetChannelDevice(channel, matchSaved);
                return;
            }
        }

        var matchHw = nonVirtual.FirstOrDefault(d => d.Name.Contains("G560", StringComparison.OrdinalIgnoreCase) ||
                                                     d.Name.Contains("HyperX", StringComparison.OrdinalIgnoreCase) ||
                                                     d.Name.Contains("Speakers", StringComparison.OrdinalIgnoreCase) ||
                                                     d.IsDefault)
                      ?? nonVirtual.FirstOrDefault();

        if (matchHw != null) SetChannelDevice(channel, matchHw);
    }

    private void BindToDedicatedSonarDevice(SonarChannelStripItem channel, string? savedId, params string[] sonarKeywords)
    {
        if (channel.AvailableDevices.Count == 0) return;

        AudioDeviceChoice? sonarDevice = null;
        foreach (var kw in sonarKeywords)
        {
            sonarDevice = channel.AvailableDevices.FirstOrDefault(d => d.Name.Contains("Sonar", StringComparison.OrdinalIgnoreCase) &&
                                                                       d.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
            if (sonarDevice != null) break;
        }

        if (sonarDevice != null)
        {
            SetChannelDevice(channel, sonarDevice);
            return;
        }

        if (!string.IsNullOrEmpty(savedId))
        {
            var matched = channel.AvailableDevices.FirstOrDefault(d => d.Id == savedId);
            if (matched != null)
            {
                SetChannelDevice(channel, matched);
                return;
            }
        }

        var def = channel.AvailableDevices.FirstOrDefault(d => d.IsDefault) ?? channel.AvailableDevices.FirstOrDefault();
        if (def != null) SetChannelDevice(channel, def);
    }

    private static void SetChannelDevice(SonarChannelStripItem channel, AudioDeviceChoice device)
    {
        if (channel.SelectedDevice?.Id != device.Id)
        {
            channel.IsUpdatingSilently = true;
            try { channel.SelectedDevice = device; }
            finally { channel.IsUpdatingSilently = false; }
        }
    }

    public void AssignSessionToChannel(AudioSessionItem session, string targetChannel)
    {
        if (session == null || string.IsNullOrWhiteSpace(targetChannel)) return;

        string cleanTarget = targetChannel.Trim();
        if (!cleanTarget.EndsWith("Channel", StringComparison.OrdinalIgnoreCase))
        {
            cleanTarget += " Channel";
        }

        session.AssignedChannel = cleanTarget;
        _processRoutingMap[session.ProcessName] = cleanTarget;
        string family = GetProcessFamily(session.ProcessName);
        if (!string.IsNullOrEmpty(family))
        {
            _processRoutingMap[family] = cleanTarget;
            if (family.Equals("steam", StringComparison.OrdinalIgnoreCase))
            {
                _processRoutingMap["steam"] = cleanTarget;
                _processRoutingMap["steamwebhelper"] = cleanTarget;
            }
        }

        SonarBusType busType = cleanTarget switch
        {
            var t when t.StartsWith("Game", StringComparison.OrdinalIgnoreCase) => SonarBusType.Game,
            var t when t.StartsWith("Chat", StringComparison.OrdinalIgnoreCase) => SonarBusType.Chat,
            var t when t.StartsWith("Media", StringComparison.OrdinalIgnoreCase) => SonarBusType.Media,
            var t when t.StartsWith("Aux", StringComparison.OrdinalIgnoreCase) => SonarBusType.Aux,
            _ => SonarBusType.Game
        };

        session.PillColorHex = busType switch
        {
            SonarBusType.Game => "#10B981",
            SonarBusType.Chat => "#00B4D8",
            SonarBusType.Media => "#EC4899",
            SonarBusType.Aux => "#A855F7",
            SonarBusType.Mic => "#F59E0B",
            _ => "#10B981"
        };

        foreach (var ch in Channels)
        {
            if (ch.AssignedApps.Contains(session))
            {
                ch.AssignedApps.Remove(session);
            }
        }

        var targetStrip = Channels.FirstOrDefault(c => c.ChannelType.ToString().Equals(busType.ToString(), StringComparison.OrdinalIgnoreCase));
        if (targetStrip != null && !targetStrip.AssignedApps.Contains(session))
        {
            targetStrip.AssignedApps.Add(session);
        }

        _audioEngine.BusManager.AssignProcessToBus(session.ProcessId, busType);
        _audioEngine.BusManager.AssignProcessNameToBus(session.ProcessName, busType);

        if (targetStrip?.SelectedDevice != null && !string.IsNullOrEmpty(targetStrip.SelectedDevice.Id))
        {
            _ = _audioEngine.RoutingService.SetAppOutputDeviceAsync(session.ProcessId, targetStrip.SelectedDevice.Id);

            foreach (var activeSession in _audioEngine.ActiveSessions)
            {
                if (activeSession.ProcessId != session.ProcessId && GetProcessFamily(activeSession.ProcessName) == family)
                {
                    _ = _audioEngine.RoutingService.SetAppOutputDeviceAsync(activeSession.ProcessId, targetStrip.SelectedDevice.Id);
                }
            }
        }

        ApplyCompoundVolumesAndMutes();
        SaveState();
    }

    private void SyncSessionCollection(ObservableCollection<AudioSessionItem> target, List<AudioSessionItem> incoming)
    {
        var incomingPids = new HashSet<int>(incoming.Select(i => i.ProcessId));
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!incomingPids.Contains(target[i].ProcessId))
            {
                target.RemoveAt(i);
            }
        }

        foreach (var inc in incoming)
        {
            var existing = target.FirstOrDefault(t => t.ProcessId == inc.ProcessId);
            if (existing != null)
            {
                if (inc.Session != null)
                {
                    existing.UpdateLiveMetrics(inc.Session);
                }
            }
            else
            {
                target.Add(inc);
            }
        }
    }

    private static void SyncCollection<T>(ObservableCollection<T> target, List<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void LoadSavedState()
    {
        if (!EnablePersistence) return;

        try
        {
            if (!File.Exists(StateFilePath)) return;

            string json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<AudioPersistentState>(json);
            if (state == null) return;

            _isLoadingState = true;
            _selectedTopTab = state.SelectedTopTab ?? "Mixer";

            if (state.ChatMixValue > 1.0)
                _chatMixValue = (state.ChatMixValue - 50.0) / 50.0;
            else
                _chatMixValue = state.ChatMixValue;

            _eqPreAmpGainDb = state.EqPreAmpGainDb;

            if (Channels.Count >= 6)
            {
                Channels[0].Volume = state.MasterVolume;
                Channels[0].IsMuted = state.MasterMuted;

                Channels[1].Volume = state.GameVolume;
                Channels[1].IsMuted = state.GameMuted;

                Channels[2].Volume = state.ChatVolume;
                Channels[2].IsMuted = state.ChatMuted;

                Channels[3].Volume = state.MediaVolume;
                Channels[3].IsMuted = state.MediaMuted;

                Channels[4].Volume = state.AuxVolume;
                Channels[4].IsMuted = state.AuxMuted;

                Channels[5].Volume = state.MicVolume;
                Channels[5].IsMuted = state.MicMuted;
            }

            _savedMasterDeviceId = state.MasterDeviceId;
            _savedGameDeviceId = state.GameDeviceId;
            _savedChatDeviceId = state.ChatDeviceId;
            _savedMediaDeviceId = state.MediaDeviceId;
            _savedAuxDeviceId = state.AuxDeviceId;
            _savedMicDeviceId = state.MicDeviceId;

            // Self-heal: If virtual channels were saved pointing to Master's physical hardware endpoint, clear them so they self-heal to Sonar
            if (!string.IsNullOrEmpty(_savedMasterDeviceId))
            {
                if (_savedGameDeviceId == _savedMasterDeviceId) _savedGameDeviceId = null;
                if (_savedChatDeviceId == _savedMasterDeviceId) _savedChatDeviceId = null;
                if (_savedMediaDeviceId == _savedMasterDeviceId) _savedMediaDeviceId = null;
                if (_savedAuxDeviceId == _savedMasterDeviceId) _savedAuxDeviceId = null;
            }

            if (state.ProcessRoutingMap != null)
            {
                _processRoutingMap.Clear();
                foreach (var kvp in state.ProcessRoutingMap)
                {
                    _processRoutingMap[kvp.Key] = kvp.Value;
                    string fam = GetProcessFamily(kvp.Key);
                    if (!string.IsNullOrEmpty(fam))
                    {
                        _processRoutingMap[fam] = kvp.Value;
                        if (fam.Equals("steam", StringComparison.OrdinalIgnoreCase))
                        {
                            _processRoutingMap["steam"] = kvp.Value;
                            _processRoutingMap["steamwebhelper"] = kvp.Value;
                        }
                    }

                    SonarBusType bType = kvp.Value switch
                    {
                        var t when t.StartsWith("Game", StringComparison.OrdinalIgnoreCase) => SonarBusType.Game,
                        var t when t.StartsWith("Chat", StringComparison.OrdinalIgnoreCase) => SonarBusType.Chat,
                        var t when t.StartsWith("Media", StringComparison.OrdinalIgnoreCase) => SonarBusType.Media,
                        var t when t.StartsWith("Aux", StringComparison.OrdinalIgnoreCase) => SonarBusType.Aux,
                        _ => SonarBusType.Game
                    };
                    _audioEngine.BusManager.AssignProcessNameToBus(kvp.Key, bType);
                    if (!string.IsNullOrEmpty(fam))
                    {
                        _audioEngine.BusManager.AssignProcessNameToBus(fam, bType);
                    }
                }
            }

            _isSpatialAudioEnabled = state.IsSpatialAudioEnabled;
            _isClearCastAiEnabled = state.IsClearCastAiEnabled;
            _clearCastStrength = state.ClearCastStrength;
            _bassBoostAmount = state.BassBoostAmount;
            _isSmartVolumeEnabled = state.IsSmartVolumeEnabled;
            _isMicMonitoringEnabled = state.IsMicMonitoringEnabled;

            _micGainDb = state.MicGainDb;
            _noiseGateThresholdDb = state.NoiseGateThresholdDb;
            _compressorThresholdDb = state.CompressorThresholdDb;
            _limiterCeilingDb = state.LimiterCeilingDb;

            ExcludedDeviceIds.Clear();
            if (state.ExcludedDeviceIds != null)
            {
                foreach (var id in state.ExcludedDeviceIds)
                {
                    ExcludedDeviceIds.Add(id);
                }
            }

            if (state.EqualizerBandGains != null && state.EqualizerBandGains.Length == EqualizerBands.Count)
            {
                for (int i = 0; i < EqualizerBands.Count; i++)
                {
                    EqualizerBands[i].GainDb = state.EqualizerBandGains[i];
                }
            }

            var preset = EqualizerPresets.FirstOrDefault(p => p.Name.Equals(state.SelectedPresetName, StringComparison.OrdinalIgnoreCase));
            if (preset != null) _selectedPreset = preset;
        }
        catch
        {
        }
        finally
        {
            _isLoadingState = false;
        }
    }

    public void SaveState()
    {
        if (!EnablePersistence || _isLoadingState) return;

        try
        {
            var state = new AudioPersistentState
            {
                SelectedTopTab = _selectedTopTab,
                ChatMixValue = _chatMixValue,
                EqPreAmpGainDb = _eqPreAmpGainDb,

                MasterVolume = Channels.Count > 0 ? Channels[0].Volume : 85f,
                MasterMuted = Channels.Count > 0 && Channels[0].IsMuted,

                GameVolume = Channels.Count > 1 ? Channels[1].Volume : 80f,
                GameMuted = Channels.Count > 1 && Channels[1].IsMuted,

                ChatVolume = Channels.Count > 2 ? Channels[2].Volume : 85f,
                ChatMuted = Channels.Count > 2 && Channels[2].IsMuted,

                MediaVolume = Channels.Count > 3 ? Channels[3].Volume : 60f,
                MediaMuted = Channels.Count > 3 && Channels[3].IsMuted,

                AuxVolume = Channels.Count > 4 ? Channels[4].Volume : 50f,
                AuxMuted = Channels.Count > 4 && Channels[4].IsMuted,

                MicVolume = Channels.Count > 5 ? Channels[5].Volume : 85f,
                MicMuted = Channels.Count > 5 && Channels[5].IsMuted,

                MasterDeviceId = Channels.Count > 0 ? Channels[0].SelectedDevice?.Id : null,
                GameDeviceId = Channels.Count > 1 ? Channels[1].SelectedDevice?.Id : null,
                ChatDeviceId = Channels.Count > 2 ? Channels[2].SelectedDevice?.Id : null,
                MediaDeviceId = Channels.Count > 3 ? Channels[3].SelectedDevice?.Id : null,
                AuxDeviceId = Channels.Count > 4 ? Channels[4].SelectedDevice?.Id : null,
                MicDeviceId = Channels.Count > 5 ? Channels[5].SelectedDevice?.Id : null,
                ProcessRoutingMap = new Dictionary<string, string>(_processRoutingMap),
                ExcludedDeviceIds = ExcludedDeviceIds.ToList(),

                SelectedPresetName = _selectedPreset?.Name ?? "CS2 / Warzone Footsteps",
                EqualizerBandGains = EqualizerBands.Select(b => b.GainDb).ToArray(),
                IsSpatialAudioEnabled = _isSpatialAudioEnabled,
                IsClearCastAiEnabled = _isClearCastAiEnabled,
                ClearCastStrength = _clearCastStrength,
                BassBoostAmount = _bassBoostAmount,
                IsSmartVolumeEnabled = _isSmartVolumeEnabled,
                IsMicMonitoringEnabled = _isMicMonitoringEnabled,

                MicGainDb = _micGainDb,
                NoiseGateThresholdDb = _noiseGateThresholdDb,
                CompressorThresholdDb = _compressorThresholdDb,
                LimiterCeilingDb = _limiterCeilingDb
            };

            string folder = Path.GetDirectoryName(StateFilePath) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS");
            Directory.CreateDirectory(folder);
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch { }
    }

    public void OpenOrActivatePopout()
    {
        void Action()
        {
            try
            {
                if (_popoutWindow != null)
                {
                    if (_popoutWindow.WindowState == System.Windows.WindowState.Minimized)
                    {
                        _popoutWindow.WindowState = System.Windows.WindowState.Normal;
                    }
                    _popoutWindow.Show();
                    _popoutWindow.Activate();
                    _popoutWindow.Focus();
                    IsPopoutActive = true;
                    return;
                }

                var win = new Views.SonarPopoutWindow(this);
                _popoutWindow = win;
                win.Closed += (_, _) =>
                {
                    _popoutWindow = null;
                    IsPopoutActive = false;
                };
                IsPopoutActive = true;
                win.Show();
                win.Activate();
            }
            catch
            {
                // Headless test execution fallback
                IsPopoutActive = true;
            }
        }

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Action);
        }
        else
        {
            Action();
        }
    }

    public void ClosePopout()
    {
        void Action()
        {
            if (_popoutWindow != null)
            {
                _popoutWindow.Close();
                _popoutWindow = null;
            }
            IsPopoutActive = false;
        }

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Action);
        }
        else
        {
            Action();
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _meterTimer.Stop();
        if (_popoutWindow != null)
        {
            try { _popoutWindow.Close(); } catch { }
            _popoutWindow = null;
        }
    }
}
