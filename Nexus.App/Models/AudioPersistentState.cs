namespace Nexus.App.Models;

public class AudioPersistentState
{
    public string SelectedTopTab { get; set; } = "Mixer";
    public double ChatMixValue { get; set; } = 0.0; // -1.0 to +1.0

    // Channel Levels & Mute States
    public float MasterVolume { get; set; } = 85f;
    public bool MasterMuted { get; set; } = false;

    public float GameVolume { get; set; } = 80f;
    public bool GameMuted { get; set; } = false;

    public float ChatVolume { get; set; } = 85f;
    public bool ChatMuted { get; set; } = false;

    public float MediaVolume { get; set; } = 60f;
    public bool MediaMuted { get; set; } = false;

    public float AuxVolume { get; set; } = 50f;
    public bool AuxMuted { get; set; } = false;

    public float MicVolume { get; set; } = 85f;
    public bool MicMuted { get; set; } = false;

    // Assigned Audio Device IDs (Independent Source per Channel)
    public string? MasterDeviceId { get; set; }
    public string? GameDeviceId { get; set; }
    public string? ChatDeviceId { get; set; }
    public string? MediaDeviceId { get; set; }
    public string? AuxDeviceId { get; set; }
    public string? MicDeviceId { get; set; }

    // Studio & EQ Features
    public string SelectedPresetName { get; set; } = "CS2 / Warzone Footsteps";
    public float EqPreAmpGainDb { get; set; } = -2.5f;
    public float[] EqualizerBandGains { get; set; } = new float[10];
    public bool IsSpatialAudioEnabled { get; set; } = true;
    public bool IsClearCastAiEnabled { get; set; } = true;
    public float ClearCastStrength { get; set; } = 75f;
    public float BassBoostAmount { get; set; } = 40f;
    public bool IsSmartVolumeEnabled { get; set; } = false;
    public bool IsMicMonitoringEnabled { get; set; } = false;

    // Microphone DSP
    public float MicGainDb { get; set; } = 0.0f;
    public bool NoiseGateEnabled { get; set; } = true;
    public float NoiseGateThresholdDb { get; set; } = -45.0f;
    public float NoiseGateAttackMs { get; set; } = 5.0f;
    public float NoiseGateReleaseMs { get; set; } = 50.0f;

    public bool CompressorEnabled { get; set; } = true;
    public float CompressorThresholdDb { get; set; } = -18.0f;
    public float CompressorRatio { get; set; } = 3.0f;
    public float CompressorMakeUpGainDb { get; set; } = 2.0f;
    public float LimiterCeilingDb { get; set; } = -1.0f;

    // Process Routing Map (ProcessName -> ChannelName)
    public Dictionary<string, string> ProcessRoutingMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Excluded / Hidden Device IDs
    public List<string> ExcludedDeviceIds { get; set; } = new();
}
