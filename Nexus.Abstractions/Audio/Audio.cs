namespace Nexus.Abstractions.Audio;

public enum AudioDeviceType
{
    Playback,
    Recording
}

public interface IAudioEndpoint
{
    string Id { get; }
    string Name { get; }
    string FriendlyName { get; }
    AudioDeviceType DeviceType { get; }
    bool IsDefault { get; }
    float MasterVolume { get; set; } // 0.0f to 1.0f
    bool IsMuted { get; set; }
    float PeakLevel { get; } // 0.0f to 1.0f
}

public interface IAudioSession
{
    int ProcessId { get; }
    string ProcessName { get; }
    string DisplayName { get; }
    string? IconPath { get; }
    float Volume { get; set; } // 0.0f to 1.0f
    bool IsMuted { get; set; }
    float PeakLevel { get; } // 0.0f to 1.0f
    bool IsSystemSound { get; }
}

public enum ParametricFilterType
{
    LowPass,
    HighPass,
    LowShelf,
    HighShelf,
    PeakingEQ,
    Notch
}

public class ParametricEqBand
{
    public int BandIndex { get; set; }
    public ParametricFilterType FilterType { get; set; } = ParametricFilterType.PeakingEQ;
    public float Frequency { get; set; } = 1000f; // 20 Hz to 20,000 Hz
    public float GainDb { get; set; } = 0.0f; // -15.0 dB to +15.0 dB
    public float QFactor { get; set; } = 1.414f; // 0.1 to 10.0
    public bool IsEnabled { get; set; } = true;
}

public class EqualizerPreset
{
    public string Name { get; set; } = "Default";
    public float PreAmpGainDb { get; set; } = 0.0f;
    // 10 bands: 31Hz, 63Hz, 125Hz, 250Hz, 500Hz, 1kHz, 2kHz, 4kHz, 8kHz, 16kHz
    public float[] BandGainsDb { get; set; } = new float[10];
    public List<ParametricEqBand> ParametricBands { get; set; } = new();

    public static readonly float[] DefaultFrequencies = new float[]
    {
        31f, 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f
    };

    public static EqualizerPreset Flat => CreatePreset("Flat", 0f, new float[10]);

    public static EqualizerPreset Gaming => CreatePreset("CS2 / Warzone Footsteps", -2.5f,
        new float[] { -2.0f, -1.0f, 0.5f, 1.5f, 2.0f, 3.5f, 5.0f, 4.0f, 2.5f, 1.0f });

    public static EqualizerPreset BassBoost => CreatePreset("Bass Boost", -4.0f,
        new float[] { 7.0f, 5.5f, 4.0f, 2.0f, 0.5f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f });

    public static EqualizerPreset Vocals => CreatePreset("Vocal Clarity / Broadcast", -1.5f,
        new float[] { -4.0f, -2.0f, 0.0f, 2.0f, 4.0f, 4.5f, 3.0f, 2.0f, 1.0f, 0.0f });

    public static EqualizerPreset Cinema => CreatePreset("Cinema Immersion", -3.0f,
        new float[] { 5.0f, 3.5f, 1.0f, -1.0f, -0.5f, 1.0f, 2.5f, 4.0f, 4.5f, 3.0f });

    private static EqualizerPreset CreatePreset(string name, float preAmp, float[] gains)
    {
        var preset = new EqualizerPreset
        {
            Name = name,
            PreAmpGainDb = preAmp,
            BandGainsDb = (float[])gains.Clone()
        };

        for (int i = 0; i < 10; i++)
        {
            var filterType = i switch
            {
                0 => ParametricFilterType.LowShelf,
                9 => ParametricFilterType.HighShelf,
                _ => ParametricFilterType.PeakingEQ
            };

            preset.ParametricBands.Add(new ParametricEqBand
            {
                BandIndex = i,
                FilterType = filterType,
                Frequency = DefaultFrequencies[i],
                GainDb = gains[i],
                QFactor = 1.414f,
                IsEnabled = true
            });
        }

        return preset;
    }
}

public class MicrophoneDspConfig
{
    public bool NoiseGateEnabled { get; set; } = true;
    public float NoiseGateThresholdDb { get; set; } = -45.0f; // dB (-60 to -20)
    public float NoiseGateAttackMs { get; set; } = 5.0f;
    public float NoiseGateReleaseMs { get; set; } = 50.0f;

    public bool NoiseSuppressionEnabled { get; set; } = false;

    public bool CompressorEnabled { get; set; } = true;
    public float CompressorThresholdDb { get; set; } = -18.0f;
    public float CompressorRatio { get; set; } = 3.0f; // 3:1
    public float CompressorAttackMs { get; set; } = 10.0f;
    public float CompressorReleaseMs { get; set; } = 80.0f;
    public float CompressorMakeUpGainDb { get; set; } = 2.0f;

    public bool LimiterEnabled { get; set; } = true;
    public float LimiterCeilingDb { get; set; } = -1.0f;

    public float InputGainDb { get; set; } = 0.0f; // -12dB to +12dB
    public bool MonitoringEnabled { get; set; } = false;
    public int MonitoringLatencyMs { get; set; } = 20; // ms
}

public interface IParametricEqEngine
{
    float PreAmpGainDb { get; set; }
    IReadOnlyList<ParametricEqBand> Bands { get; }
    void LoadPreset(EqualizerPreset preset);
    void UpdateBand(int bandIndex, float gainDb, float frequency, float qFactor, ParametricFilterType type, bool isEnabled);
    double[] CalculateFrequencyResponse(double[] frequencies, int sampleRate = 48000);
}

public interface IAudioEngine
{
    IReadOnlyList<IAudioEndpoint> PlaybackDevices { get; }
    IReadOnlyList<IAudioEndpoint> RecordingDevices { get; }
    IAudioEndpoint? DefaultPlaybackDevice { get; }
    IAudioEndpoint? DefaultRecordingDevice { get; }
    IReadOnlyList<IAudioSession> ActiveSessions { get; }

    ISonarBusManager BusManager { get; }
    IAudioRoutingService RoutingService { get; }
    IParametricEqEngine EqEngine { get; }

    EqualizerPreset ActiveEqualizerPreset { get; set; }
    MicrophoneDspConfig MicDspConfig { get; set; }

    event EventHandler? DevicesChanged;
    event EventHandler? SessionsChanged;
    event EventHandler? PeakLevelsUpdated;

    Task RefreshDevicesAsync();
    Task RefreshSessionsAsync();
    void SetPlaybackDeviceVolume(string deviceId, float volume);
    void SetPlaybackDeviceMute(string deviceId, bool mute);
    void SetRecordingDeviceVolume(string deviceId, float volume);
    void SetRecordingDeviceMute(string deviceId, bool mute);
    void SetSessionVolume(int processId, float volume);
    void SetSessionMute(int processId, bool mute);
    Task SetDefaultPlaybackDeviceAsync(string deviceId);
    Task SetDefaultRecordingDeviceAsync(string deviceId);
}
