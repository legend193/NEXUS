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

public class EqualizerPreset
{
    public string Name { get; set; } = "Default";
    // 10 bands: 31Hz, 63Hz, 125Hz, 250Hz, 500Hz, 1kHz, 2kHz, 4kHz, 8kHz, 16kHz
    // Gain in dB: -12.0 to +12.0
    public float[] BandGainsDb { get; set; } = new float[10];

    public static EqualizerPreset Flat => new() { Name = "Flat" };
    public static EqualizerPreset Gaming => new()
    {
        Name = "Gaming",
        BandGainsDb = new float[] { 3.5f, 2.0f, 0.0f, -1.0f, 0.5f, 2.5f, 4.0f, 3.5f, 2.0f, 1.0f }
    };
    public static EqualizerPreset BassBoost => new()
    {
        Name = "Bass Boost",
        BandGainsDb = new float[] { 7.0f, 5.5f, 4.0f, 2.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f }
    };
    public static EqualizerPreset Vocals => new()
    {
        Name = "Vocal Clarity",
        BandGainsDb = new float[] { -2.0f, -1.0f, 0.0f, 2.0f, 4.0f, 4.5f, 3.0f, 1.5f, 0.5f, 0.0f }
    };
    public static EqualizerPreset Cinema => new()
    {
        Name = "Cinema",
        BandGainsDb = new float[] { 5.0f, 3.5f, 1.0f, -1.0f, -0.5f, 1.0f, 2.5f, 4.0f, 4.5f, 3.0f }
    };
}

public class MicrophoneDspConfig
{
    public bool NoiseGateEnabled { get; set; } = true;
    public float NoiseGateThresholdDb { get; set; } = -45.0f; // dB

    public bool NoiseSuppressionEnabled { get; set; } = false;

    public bool CompressorEnabled { get; set; } = true;
    public float CompressorThresholdDb { get; set; } = -18.0f;
    public float CompressorRatio { get; set; } = 3.0f; // 3:1

    public bool LimiterEnabled { get; set; } = true;
    public float LimiterCeilingDb { get; set; } = -1.0f;

    public float InputGainDb { get; set; } = 0.0f; // -12dB to +12dB
    public bool MonitoringEnabled { get; set; } = false;
    public int MonitoringLatencyMs { get; set; } = 20; // ms
}

public interface IAudioEngine
{
    IReadOnlyList<IAudioEndpoint> PlaybackDevices { get; }
    IReadOnlyList<IAudioEndpoint> RecordingDevices { get; }
    IAudioEndpoint? DefaultPlaybackDevice { get; }
    IAudioEndpoint? DefaultRecordingDevice { get; }
    IReadOnlyList<IAudioSession> ActiveSessions { get; }
    
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
