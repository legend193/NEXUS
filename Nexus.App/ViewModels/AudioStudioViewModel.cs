using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Nexus.Abstractions.Audio;

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

    public int ProcessId => _session?.ProcessId ?? 0;
    public string DisplayName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string PillColorHex { get; init; } = "#A855F7";

    public float Volume
    {
        get => (_session?.Volume ?? 1f) * 100f;
        set
        {
            if (_session != null) _session.Volume = value / 100f;
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => _session?.IsMuted ?? false;
        set
        {
            if (_session != null) _session.IsMuted = value;
            OnPropertyChanged();
        }
    }

    public ICommand ToggleMuteCommand { get; }

    public AudioSessionItem(IAudioSession? session, string fallbackName = "WALLPAPER64", string pillColor = "#A855F7")
    {
        _session = session;
        DisplayName = session?.DisplayName ?? fallbackName;
        ProcessName = session?.ProcessName ?? fallbackName;
        PillColorHex = pillColor;
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
    }
}

public class AppRoutingItem : ViewModelBase
{
    private readonly IAudioSession? _session;
    private float _volume = 85f;
    private bool _isChannelRouted = true;

    public string DisplayName { get; init; } = string.Empty;
    public string ChannelName { get; init; } = "Game Channel";
    public string IconKey { get; init; } = "IconAudio";
    public string ColorHex { get; init; } = "#00D2FF";

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
        ChannelName = channelName;
        IconKey = iconKey;
        ColorHex = colorHex;
        _volume = session != null ? session.Volume * 100f : defaultVol;
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

public class SonarChannelStripItem : ViewModelBase
{
    private float _volume = 85f;
    private bool _isMuted;
    private double _peakLevelLeft = 65.0;
    private double _peakLevelRight = 62.0;
    private string _presetName = "Custom";
    private string _assignedDeviceName = "Headset Earphone (HyperX... 50%)";

    public Action<float>? OnVolumeChanged { get; set; }
    public Action<bool>? OnMuteChanged { get; set; }
    public bool IsUpdatingSilently { get; set; }

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
                if (!IsUpdatingSilently) OnMuteChanged?.Invoke(_isMuted);
            }
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

    public SonarChannelStripItem(SonarChannelType type, string title, string iconKey, string colorHex)
    {
        ChannelType = type;
        Title = title;
        IconKey = iconKey;
        ColorHex = colorHex;
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
    }
}

public class AudioStudioViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private string _selectedTopTab = "Mixer";
    private double _chatMixValue = 50.0;
    private EqualizerPreset _selectedPreset = EqualizerPreset.Gaming;
    private readonly DispatcherTimer _refreshTimer;

    // SteelSeries Sonar Studio Features
    private bool _isSpatialAudioEnabled = true;
    private bool _isClearCastAiEnabled = true;
    private float _clearCastStrength = 75f;
    private float _bassBoostAmount = 40f;
    private bool _isSmartVolumeEnabled = false;
    private bool _isMicMonitoringEnabled = false;

    // Microphone Studio
    private float _micGainDb = 0.0f;
    private float _noiseGateThresholdDb = -45.0f;
    private float _compressorThresholdDb = -18.0f;
    private float _limiterCeilingDb = -1.0f;

    // Channels (Exact 6 channels matching Sonar screenshot)
    public ObservableCollection<SonarChannelStripItem> Channels { get; } = new();
    public ObservableCollection<EqualizerPreset> EqualizerPresets { get; } = new();
    public ObservableCollection<EqualizerBandItem> EqualizerBands { get; } = new();
    public ObservableCollection<AppRoutingItem> AppRoutings { get; } = new();

    public string SelectedTopTab
    {
        get => _selectedTopTab;
        set => SetProperty(ref _selectedTopTab, value);
    }

    public double ChatMixValue
    {
        get => _chatMixValue;
        set
        {
            if (SetProperty(ref _chatMixValue, Math.Round(value, 0)))
            {
                ApplyChatMix();
            }
        }
    }

    public bool IsSpatialAudioEnabled
    {
        get => _isSpatialAudioEnabled;
        set => SetProperty(ref _isSpatialAudioEnabled, value);
    }

    public bool IsClearCastAiEnabled
    {
        get => _isClearCastAiEnabled;
        set => SetProperty(ref _isClearCastAiEnabled, value);
    }

    public float ClearCastStrength
    {
        get => _clearCastStrength;
        set => SetProperty(ref _clearCastStrength, value);
    }

    public float BassBoostAmount
    {
        get => _bassBoostAmount;
        set => SetProperty(ref _bassBoostAmount, value);
    }

    public bool IsSmartVolumeEnabled
    {
        get => _isSmartVolumeEnabled;
        set => SetProperty(ref _isSmartVolumeEnabled, value);
    }

    public bool IsMicMonitoringEnabled
    {
        get => _isMicMonitoringEnabled;
        set => SetProperty(ref _isMicMonitoringEnabled, value);
    }

    public float MicGainDb
    {
        get => _micGainDb;
        set
        {
            if (SetProperty(ref _micGainDb, (float)Math.Round(value, 1)))
            {
                _audioEngine.MicDspConfig.InputGainDb = value;
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
            }
        }
    }

    public ICommand SelectTabCommand { get; }
    public ICommand ResetEqBandsCommand { get; }
    public ICommand RefreshAudioCommand { get; }

    public AudioStudioViewModel(IAudioEngine audioEngine)
    {
        _audioEngine = audioEngine;

        SelectTabCommand = new RelayCommand(param =>
        {
            if (param is string tab) SelectedTopTab = tab;
        });

        // 1. MASTER (Blue #4A72FF)
        var masterCh = new SonarChannelStripItem(SonarChannelType.Master, "MASTER", "IconEq", "#4A72FF")
        {
            Volume = 85,
            IsMuted = false,
            PeakLevelLeft = 68,
            PeakLevelRight = 64,
            AssignedDeviceName = "Headset Earphone (...  50%)"
        };

        // 2. GAME (Green #10B981)
        var gameCh = new SonarChannelStripItem(SonarChannelType.Game, "GAME", "IconGame", "#10B981")
        {
            Volume = 50,
            IsMuted = false,
            PeakLevelLeft = 58,
            PeakLevelRight = 54,
            PresetName = "Custom",
            AssignedDeviceName = "Headset Earphone (...  50%)"
        };

        // 3. CHAT (Cyan #00B4D8)
        var chatCh = new SonarChannelStripItem(SonarChannelType.Chat, "CHAT", "IconChat", "#00B4D8")
        {
            Volume = 85,
            IsMuted = false,
            PeakLevelLeft = 45,
            PeakLevelRight = 42,
            PresetName = "Custom",
            AssignedDeviceName = "Headset Earphone (...  50%)"
        };

        // 4. MEDIA (Pink #EC4899)
        var mediaCh = new SonarChannelStripItem(SonarChannelType.Media, "MEDIA", "IconPlay", "#EC4899")
        {
            Volume = 45,
            IsMuted = false,
            PeakLevelLeft = 52,
            PeakLevelRight = 48,
            PresetName = "Custom",
            AssignedDeviceName = "Headset Earphone (...  50%)"
        };

        // 5. AUX (Purple #A855F7) — Muted by default like in user's screenshot, with WALLPAPER64 routed
        var auxCh = new SonarChannelStripItem(SonarChannelType.Aux, "AUX", "IconAux", "#A855F7")
        {
            Volume = 85,
            IsMuted = true, // Matches red mute button in screenshot
            PeakLevelLeft = 0,
            PeakLevelRight = 0,
            PresetName = "Custom",
            AssignedDeviceName = "Headset Earphone (...  50%)"
        };
        // Matches the screenshot's WALLPAPER64 apps
        auxCh.AssignedApps.Add(new AudioSessionItem(null, "WALLPAPER64", "#A855F7"));
        auxCh.AssignedApps.Add(new AudioSessionItem(null, "WALLPAPER64", "#A855F7"));

        // 6. MIC (Amber #F59E0B)
        var micCh = new SonarChannelStripItem(SonarChannelType.Mic, "MIC", "IconMic", "#F59E0B")
        {
            Volume = 85,
            IsMuted = false,
            PeakLevelLeft = 38,
            PeakLevelRight = 38,
            PresetName = "Custom",
            AssignedDeviceName = "Headset Microphone (Hy...  100%)"
        };

        Channels.Add(masterCh);
        Channels.Add(gameCh);
        Channels.Add(chatCh);
        Channels.Add(mediaCh);
        Channels.Add(auxCh);
        Channels.Add(micCh);

        // App Volume & Routing Streams
        AppRoutings.Add(new AppRoutingItem(null, "Discord", "Chat Channel", "IconChatBubble", "#00B4D8", 85f));
        AppRoutings.Add(new AppRoutingItem(null, "Spotify", "Media Channel", "IconMediaPlay", "#EC4899", 70f));
        AppRoutings.Add(new AppRoutingItem(null, "Cyberpunk 2077", "Game Channel", "IconGameController", "#10B981", 100f));

        // Hardware audio binding to Windows Core Audio
        masterCh.OnVolumeChanged = vol =>
        {
            if (_audioEngine.DefaultPlaybackDevice != null)
            {
                _audioEngine.DefaultPlaybackDevice.MasterVolume = vol / 100f;
            }
        };
        masterCh.OnMuteChanged = muted =>
        {
            if (_audioEngine.DefaultPlaybackDevice != null)
            {
                _audioEngine.DefaultPlaybackDevice.IsMuted = muted;
            }
        };

        micCh.OnVolumeChanged = vol =>
        {
            if (_audioEngine.DefaultRecordingDevice != null)
            {
                _audioEngine.DefaultRecordingDevice.MasterVolume = vol / 100f;
            }
        };
        micCh.OnMuteChanged = muted =>
        {
            if (_audioEngine.DefaultRecordingDevice != null)
            {
                _audioEngine.DefaultRecordingDevice.IsMuted = muted;
            }
        };

        Action<SonarChannelStripItem> wireUpAppChannel = ch =>
        {
            ch.OnVolumeChanged = vol =>
            {
                foreach (var app in ch.AssignedApps)
                {
                    app.Volume = vol;
                }
            };
            ch.OnMuteChanged = muted =>
            {
                foreach (var app in ch.AssignedApps)
                {
                    app.IsMuted = muted;
                }
            };
        };

        wireUpAppChannel(gameCh);
        wireUpAppChannel(chatCh);
        wireUpAppChannel(mediaCh);
        wireUpAppChannel(auxCh);

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

        ResetEqBandsCommand = new RelayCommand(() => SelectedPreset = EqualizerPresets[^1]);
        RefreshAudioCommand = new RelayCommand(async () => await RefreshAsync());

        // Refresh loop to populate real Windows audio sessions every 2 seconds
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _ = RefreshAsync();
    }

    private void ApplyChatMix()
    {
        // ChatMix: 50 = 100% game, 100% chat. <50 attenuates chat, >50 attenuates game
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

        Channels[1].Volume = 50f * gameFactor;
        Channels[2].Volume = 85f * chatFactor;
    }

    private void ApplyPresetToBands(EqualizerPreset preset)
    {
        for (int i = 0; i < Math.Min(EqualizerBands.Count, preset.BandGainsDb.Length); i++)
        {
            EqualizerBands[i].GainDb = preset.BandGainsDb[i];
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            await _audioEngine.RefreshDevicesAsync();

            var defaultOut = _audioEngine.DefaultPlaybackDevice;
            if (defaultOut != null)
            {
                string devName = defaultOut.Name.Length > 22 ? defaultOut.Name.Substring(0, 20) + "..." : defaultOut.Name;
                int vol = (int)Math.Round(defaultOut.MasterVolume * 100);
                string summary = $"{devName}  {vol}%";

                Channels[0].AssignedDeviceName = summary;
                Channels[1].AssignedDeviceName = summary;
                Channels[2].AssignedDeviceName = summary;
                Channels[3].AssignedDeviceName = summary;
                Channels[4].AssignedDeviceName = summary;

                Channels[0].IsUpdatingSilently = true;
                Channels[0].Volume = vol;
                Channels[0].IsMuted = defaultOut.IsMuted;
                Channels[0].IsUpdatingSilently = false;
            }

            var defaultIn = _audioEngine.DefaultRecordingDevice;
            if (defaultIn != null)
            {
                string devName = defaultIn.Name.Length > 22 ? defaultIn.Name.Substring(0, 20) + "..." : defaultIn.Name;
                int vol = (int)Math.Round(defaultIn.MasterVolume * 100);
                Channels[5].AssignedDeviceName = $"{devName}  {vol}%";

                Channels[5].IsUpdatingSilently = true;
                Channels[5].Volume = vol;
                Channels[5].IsMuted = defaultIn.IsMuted;
                Channels[5].IsUpdatingSilently = false;
            }

            // Route real Windows audio sessions into respective channels
            var sessions = _audioEngine.ActiveSessions;
            if (sessions.Count > 0)
            {
                var gameSessions = new List<AudioSessionItem>();
                var chatSessions = new List<AudioSessionItem>();
                var mediaSessions = new List<AudioSessionItem>();

                foreach (var s in sessions)
                {
                    string name = s.ProcessName.ToLowerInvariant();
                    if (name.Contains("discord") || name.Contains("teams") || name.Contains("zoom") || name.Contains("skype"))
                    {
                        chatSessions.Add(new AudioSessionItem(s, s.ProcessName, "#00B4D8"));
                    }
                    else if (name.Contains("spotify") || name.Contains("chrome") || name.Contains("vlc") || name.Contains("msedge") || name.Contains("firefox"))
                    {
                        mediaSessions.Add(new AudioSessionItem(s, s.ProcessName, "#EC4899"));
                    }
                    else if (!name.Contains("idle") && !name.Contains("audiodg"))
                    {
                        gameSessions.Add(new AudioSessionItem(s, s.ProcessName, "#10B981"));
                    }
                }

                // Update assigned apps collections
                SyncCollection(Channels[1].AssignedApps, gameSessions);
                SyncCollection(Channels[2].AssignedApps, chatSessions);
                SyncCollection(Channels[3].AssignedApps, mediaSessions);
            }
        }
        catch { }
    }

    private static void SyncCollection(ObservableCollection<AudioSessionItem> target, List<AudioSessionItem> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}
