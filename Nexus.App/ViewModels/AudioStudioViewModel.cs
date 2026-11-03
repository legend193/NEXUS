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

    public float GainDb
    {
        get => _gainDb;
        set => SetProperty(ref _gainDb, (float)Math.Round(value, 1));
    }
}

public class AudioSessionItem : ViewModelBase
{
    private readonly IAudioSession? _session;
    private string _assignedChannel = "Game";
    private ImageSource? _icon;
    private bool _isMuted;
    private float _volume = 100f;

    public int ProcessId => _session?.ProcessId ?? 0;
    public string DisplayName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string PillColorHex { get; init; } = "#10B981";

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public string AssignedChannel
    {
        get => _assignedChannel;
        set => SetProperty(ref _assignedChannel, value);
    }

    public float Volume
    {
        get => _session != null ? _session.Volume * 100f : _volume;
        set
        {
            _volume = value;
            if (_session != null) _session.Volume = value / 100f;
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => _session != null ? _session.IsMuted : _isMuted;
        set
        {
            _isMuted = value;
            if (_session != null) _session.IsMuted = value;
            OnPropertyChanged();
        }
    }

    public ICommand ToggleMuteCommand { get; }

    public AudioSessionItem(IAudioSession? session, string fallbackName = "System Sounds", string pillColor = "#10B981", string initialChannel = "Game")
    {
        _session = session;
        DisplayName = session?.DisplayName ?? fallbackName;
        ProcessName = session?.ProcessName ?? fallbackName;
        PillColorHex = pillColor;
        _assignedChannel = initialChannel;
        _isMuted = session?.IsMuted ?? false;
        _volume = session != null ? session.Volume * 100f : 100f;
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);

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
            if (_selectedDevice == null) return "غير متصل";
            return _selectedDevice.IsVirtual ? "مسار افتراضي (Virtual Source)" : "منفذ أجهزة (Direct Hardware)";
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
    private double _chatMixValue = 50.0;
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

    public double ChatMixValue
    {
        get => _chatMixValue;
        set
        {
            if (SetProperty(ref _chatMixValue, Math.Round(value, 0)))
            {
                ApplyChatMix();
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

    public ICommand SelectTabCommand { get; }
    public ICommand ResetAllVolumesCommand { get; }
    public ICommand ResetEqBandsCommand { get; }
    public ICommand RefreshSessionsCommand { get; }
    public ICommand RefreshAudioCommand { get; }
    public ICommand ResetChatMixCommand { get; }
    public ICommand SetChatMixBalanceCommand { get; }
    public ICommand MuteAllChannelsCommand { get; }

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
                if (ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.MasterVolume = vol / 100f;
                }
                else if (ch.ChannelType == SonarChannelType.Master && _audioEngine.DefaultPlaybackDevice != null)
                {
                    _audioEngine.DefaultPlaybackDevice.MasterVolume = vol / 100f;
                }
                else if (ch.ChannelType == SonarChannelType.Mic && _audioEngine.DefaultRecordingDevice != null)
                {
                    _audioEngine.DefaultRecordingDevice.MasterVolume = vol / 100f;
                }

                foreach (var app in ch.AssignedApps)
                {
                    app.Volume = vol;
                }
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

                foreach (var app in ch.AssignedApps)
                {
                    app.IsMuted = muted;
                }
                SaveState();
            };

            ch.OnSoloChanged = solo =>
            {
                ApplySoloRouting();
            };

            ch.OnDeviceSelected = choice =>
            {
                SaveState();
            };
        }

        // 10 EQ Bands
        string[] freqLabels = { "31Hz", "63Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz" };
        for (int i = 0; i < freqLabels.Length; i++)
        {
            EqualizerBands.Add(new EqualizerBandItem
            {
                BandIndex = i,
                FrequencyLabel = freqLabels[i],
                GainDb = 0f
            });
        }

        // Sonar Game-optimized presets
        EqualizerPresets.Add(new EqualizerPreset { Name = "CS2 / Warzone Footsteps", BandGainsDb = new float[] { -2f, 1f, 3f, 4.5f, 6f, 7f, 5f, 3f, 1f, 0f } });
        EqualizerPresets.Add(new EqualizerPreset { Name = "Apex Legends Enhanced", BandGainsDb = new float[] { 1f, 2f, 4f, 5f, 6.5f, 6f, 4f, 2f, 1f, 0f } });
        EqualizerPresets.Add(new EqualizerPreset { Name = "Bass Immersion", BandGainsDb = new float[] { 8f, 7f, 5f, 3f, 1f, 0f, 0f, 0f, 0f, 0f } });
        EqualizerPresets.Add(new EqualizerPreset { Name = "Esports Vocal Clarity", BandGainsDb = new float[] { -3f, -1f, 1f, 3f, 5f, 6f, 4f, 2f, 0.5f, 0f } });
        EqualizerPresets.Add(new EqualizerPreset { Name = "Flat Studio Reference", BandGainsDb = new float[10] });

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
            ChatMixValue = 50.0;
            SaveState();
        });

        ResetEqBandsCommand = new RelayCommand(() =>
        {
            SelectedPreset = EqualizerPresets[^1];
        });

        RefreshSessionsCommand = new RelayCommand(async () => await RefreshAsync());
        RefreshAudioCommand = RefreshSessionsCommand;

        ResetChatMixCommand = new RelayCommand(() => ChatMixValue = 50.0);
        SetChatMixBalanceCommand = new RelayCommand(param =>
        {
            if (param is double d) ChatMixValue = d;
            else if (param is string s && double.TryParse(s, out var parsed)) ChatMixValue = parsed;
        });

        MuteAllChannelsCommand = new RelayCommand(() =>
        {
            bool anyUnmuted = Channels.Any(c => !c.IsMuted);
            foreach (var c in Channels) c.IsMuted = anyUnmuted;
            SaveState();
        });

        // Load saved state before starting loops
        LoadSavedState();

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

    private void ApplyChatMix()
    {
        // ChatMix: 50 = balanced. <50 attenuates chat, >50 attenuates game
        float gameFactor = 1.0f;
        float chatFactor = 1.0f;

        if (_chatMixValue < 50.0)
        {
            chatFactor = (float)(_chatMixValue / 50.0);
        }
        else if (_chatMixValue > 50.0)
        {
            gameFactor = (float)((100.0 - _chatMixValue) / 50.0);
        }

        Channels[1].Volume = 80f * gameFactor;
        Channels[2].Volume = 85f * chatFactor;
    }

    private void ApplyPresetToBands(EqualizerPreset preset)
    {
        for (int i = 0; i < Math.Min(EqualizerBands.Count, preset.BandGainsDb.Length); i++)
        {
            EqualizerBands[i].GainDb = preset.BandGainsDb[i];
        }
    }

    private void ApplySoloRouting()
    {
        bool anySolo = Channels.Any(c => c.IsSolo);
        foreach (var ch in Channels)
        {
            if (anySolo)
            {
                bool shouldMute = !ch.IsSolo;
                if (ch.BoundEndpoint != null)
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
                if (ch.BoundEndpoint != null)
                {
                    ch.BoundEndpoint.IsMuted = ch.IsMuted;
                }
                foreach (var app in ch.AssignedApps)
                {
                    app.IsMuted = ch.IsMuted;
                }
            }
        }
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        float masterPeak = _audioEngine.DefaultPlaybackDevice?.PeakLevel ?? 0f;
        float micDefaultPeak = _audioEngine.DefaultRecordingDevice?.PeakLevel ?? 0f;

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
            double targetPeak = 0.0;

            if (endpointPeak > 0.001f)
            {
                targetPeak = endpointPeak * 100.0;
            }
            else
            {
                if (ch.ChannelType == SonarChannelType.Master)
                {
                    targetPeak = masterPeak * 100.0;
                }
                else if (ch.ChannelType == SonarChannelType.Mic)
                {
                    targetPeak = micDefaultPeak * 100.0;
                }
                else if (masterPeak > 0.001f && ch.Volume > 0)
                {
                    double ratio = (ch.Volume / 100.0) * (0.80 + (i * 0.03));
                    targetPeak = masterPeak * 100.0 * ratio;
                }
            }

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

    public async Task RefreshAsync()
    {
        try
        {
            await _audioEngine.RefreshDevicesAsync();

            PopulateChannelDevices();

            // Sync active sessions across all devices
            var sessions = _audioEngine.ActiveSessions;
            if (sessions.Count > 0)
            {
                var gameSessions = new List<AudioSessionItem>();
                var chatSessions = new List<AudioSessionItem>();
                var mediaSessions = new List<AudioSessionItem>();
                var auxSessions = new List<AudioSessionItem>();
                var routingList = new List<AppRoutingItem>();

                foreach (var s in sessions)
                {
                    string name = s.ProcessName.ToLowerInvariant();
                    string channelTarget = "Game Channel";
                    string pillColor = "#10B981";

                    if (_processRoutingMap.TryGetValue(s.ProcessName, out var savedTarget))
                    {
                        channelTarget = savedTarget;
                    }
                    else if (name.Contains("discord") || name.Contains("teams") || name.Contains("zoom") || name.Contains("skype"))
                    {
                        channelTarget = "Chat Channel";
                    }
                    else if (name.Contains("spotify") || name.Contains("chrome") || name.Contains("vlc") || name.Contains("msedge") || name.Contains("firefox"))
                    {
                        channelTarget = "Media Channel";
                    }
                    else if (name.Contains("system") || name.Contains("wallpaper") || name.Contains("steam"))
                    {
                        channelTarget = "Aux Channel";
                    }

                    if (channelTarget.StartsWith("Chat", StringComparison.OrdinalIgnoreCase))
                    {
                        pillColor = "#00B4D8";
                        chatSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Chat"));
                    }
                    else if (channelTarget.StartsWith("Media", StringComparison.OrdinalIgnoreCase))
                    {
                        pillColor = "#EC4899";
                        mediaSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Media"));
                    }
                    else if (channelTarget.StartsWith("Aux", StringComparison.OrdinalIgnoreCase))
                    {
                        pillColor = "#A855F7";
                        auxSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Aux"));
                    }
                    else
                    {
                        pillColor = "#10B981";
                        gameSessions.Add(new AudioSessionItem(s, s.DisplayName, pillColor, "Game"));
                    }

                    routingList.Add(new AppRoutingItem(s, s.DisplayName, channelTarget, "IconAudio", pillColor));
                }

                SyncCollection(Channels[1].AssignedApps, gameSessions);
                SyncCollection(Channels[2].AssignedApps, chatSessions);
                SyncCollection(Channels[3].AssignedApps, mediaSessions);
                SyncCollection(Channels[4].AssignedApps, auxSessions);
                SyncCollection(AppRoutings, routingList);
            }
        }
        catch { }
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

            string tag = isVirtual ? " [Virtual]" : (dev.IsDefault ? " [Default]" : "");
            playbackChoices.Add(new AudioDeviceChoice
            {
                Id = dev.Id,
                Name = dev.Name,
                DisplayName = $"{dev.Name}{tag}",
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

            string tag = isVirtual ? " [Virtual]" : (dev.IsDefault ? " [Default]" : "");
            recordingChoices.Add(new AudioDeviceChoice
            {
                Id = dev.Id,
                Name = dev.Name,
                DisplayName = $"{dev.Name}{tag}",
                IsDefault = dev.IsDefault,
                IsVirtual = isVirtual,
                Endpoint = dev
            });
        }

        for (int i = 0; i < Math.Min(5, Channels.Count); i++)
        {
            SyncDeviceChoices(Channels[i].AvailableDevices, playbackChoices);
        }

        if (Channels.Count > 5)
        {
            SyncDeviceChoices(Channels[5].AvailableDevices, recordingChoices);
        }

        AutoBindChannelDevices();
    }

    private static void SyncDeviceChoices(ObservableCollection<AudioDeviceChoice> target, List<AudioDeviceChoice> source)
    {
        if (target.Count == source.Count && target.Select(t => t.Id).SequenceEqual(source.Select(s => s.Id)))
        {
            return;
        }

        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void AutoBindChannelDevices()
    {
        if (Channels.Count < 6) return;

        BindChannelToBestDevice(Channels[0], _savedMasterDeviceId, "Speakers", "Headphones", "Default");
        BindChannelToBestDevice(Channels[1], _savedGameDeviceId, "Sonar - Gaming", "Game", "Gaming");
        BindChannelToBestDevice(Channels[2], _savedChatDeviceId, "Sonar - Chat", "Chat", "Headset");
        BindChannelToBestDevice(Channels[3], _savedMediaDeviceId, "Sonar - Media", "Media", "G560");
        BindChannelToBestDevice(Channels[4], _savedAuxDeviceId, "Sonar - Aux", "Aux");
        BindChannelToBestDevice(Channels[5], _savedMicDeviceId, "Sonar - Microphone", "Microphone", "Mic");
    }

    private void BindChannelToBestDevice(SonarChannelStripItem channel, string? savedId, params string[] searchKeywords)
    {
        if (channel.AvailableDevices.Count == 0) return;

        if (!string.IsNullOrEmpty(savedId))
        {
            var matched = channel.AvailableDevices.FirstOrDefault(d => d.Id == savedId);
            if (matched != null)
            {
                if (channel.SelectedDevice?.Id != matched.Id)
                {
                    channel.SelectedDevice = matched;
                }
                return;
            }
        }

        if (channel.SelectedDevice != null) return;

        foreach (var kw in searchKeywords)
        {
            var matched = channel.AvailableDevices.FirstOrDefault(d => d.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                channel.SelectedDevice = matched;
                return;
            }
        }

        var def = channel.AvailableDevices.FirstOrDefault(d => d.IsDefault) ?? channel.AvailableDevices.FirstOrDefault();
        channel.SelectedDevice = def;
    }

    public void AssignSessionToChannel(AudioSessionItem session, string targetChannel)
    {
        session.AssignedChannel = targetChannel;
        _processRoutingMap[session.ProcessName] = targetChannel;
        _ = RefreshAsync();
        SaveState();
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
            _chatMixValue = state.ChatMixValue;

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

            if (state.ProcessRoutingMap != null)
            {
                _processRoutingMap.Clear();
                foreach (var kvp in state.ProcessRoutingMap) _processRoutingMap[kvp.Key] = kvp.Value;
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

    public void Dispose()
    {
        _refreshTimer.Stop();
        _meterTimer.Stop();
    }
}
