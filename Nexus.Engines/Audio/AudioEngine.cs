using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Nexus.Abstractions.Audio;

namespace Nexus.Engines.Audio;

public class AudioEndpointWrapper : IAudioEndpoint
{
    private readonly MMDevice _device;

    public string Id => _device.ID;
    public string Name => _device.FriendlyName;
    public string FriendlyName => _device.FriendlyName;
    public AudioDeviceType DeviceType { get; }
    public bool IsDefault { get; set; }

    public float MasterVolume
    {
        get
        {
            try { return _device.AudioEndpointVolume.MasterVolumeLevelScalar; }
            catch { return 0f; }
        }
        set
        {
            try { _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); }
            catch { }
        }
    }

    public bool IsMuted
    {
        get
        {
            try { return _device.AudioEndpointVolume.Mute; }
            catch { return false; }
        }
        set
        {
            try { _device.AudioEndpointVolume.Mute = value; }
            catch { }
        }
    }

    public float PeakLevel
    {
        get
        {
            try { return _device.AudioMeterInformation.MasterPeakValue; }
            catch { return 0f; }
        }
    }

    public AudioEndpointWrapper(MMDevice device, AudioDeviceType deviceType, bool isDefault)
    {
        _device = device;
        DeviceType = deviceType;
        IsDefault = isDefault;
    }
}

public class AudioSessionWrapper : IAudioSession
{
    private readonly AudioSessionControl _session;

    public int ProcessId => (int)_session.GetProcessID;
    public string ProcessName { get; }
    public string DisplayName { get; }
    public string? IconPath => _session.IconPath;

    public float Volume
    {
        get
        {
            try { return _session.SimpleAudioVolume.Volume; }
            catch { return 0f; }
        }
        set
        {
            try { _session.SimpleAudioVolume.Volume = Math.Clamp(value, 0f, 1f); }
            catch { }
        }
    }

    public bool IsMuted
    {
        get
        {
            try { return _session.SimpleAudioVolume.Mute; }
            catch { return false; }
        }
        set
        {
            try { _session.SimpleAudioVolume.Mute = value; }
            catch { }
        }
    }

    public float PeakLevel
    {
        get
        {
            try { return _session.AudioMeterInformation.MasterPeakValue; }
            catch { return 0f; }
        }
    }

    public bool IsSystemSound => _session.IsSystemSoundsSession;

    public AudioSessionWrapper(AudioSessionControl session)
    {
        _session = session;
        int pid = (int)session.GetProcessID;
        string procName = "System Sounds";
        string dispName = session.DisplayName;

        if (pid > 0)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                procName = proc.ProcessName;
                if (string.IsNullOrWhiteSpace(dispName))
                {
                    dispName = !string.IsNullOrWhiteSpace(proc.MainWindowTitle) ? proc.MainWindowTitle : proc.ProcessName;
                }
            }
            catch
            {
                procName = $"Process #{pid}";
            }
        }

        ProcessName = procName;
        DisplayName = string.IsNullOrWhiteSpace(dispName) ? procName : dispName;
    }
}

public class AudioEngine : IAudioEngine, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly ILogger<AudioEngine>? _logger;
    private readonly List<IAudioEndpoint> _playbackDevices = new();
    private readonly List<IAudioEndpoint> _recordingDevices = new();
    private readonly List<IAudioSession> _activeSessions = new();
    private readonly object _lock = new();

    public IReadOnlyList<IAudioEndpoint> PlaybackDevices { get { lock (_lock) return _playbackDevices.ToList(); } }
    public IReadOnlyList<IAudioEndpoint> RecordingDevices { get { lock (_lock) return _recordingDevices.ToList(); } }
    public IAudioEndpoint? DefaultPlaybackDevice { get; private set; }
    public IAudioEndpoint? DefaultRecordingDevice { get; private set; }
    public IReadOnlyList<IAudioSession> ActiveSessions { get { lock (_lock) return _activeSessions.ToList(); } }

    public EqualizerPreset ActiveEqualizerPreset { get; set; } = EqualizerPreset.Gaming;
    public MicrophoneDspConfig MicDspConfig { get; set; } = new();

    public event EventHandler? DevicesChanged;
    public event EventHandler? SessionsChanged;
    public event EventHandler? PeakLevelsUpdated;

    public AudioEngine(ILogger<AudioEngine>? logger = null)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
    }

    public Task RefreshDevicesAsync()
    {
        lock (_lock)
        {
            _playbackDevices.Clear();
            _recordingDevices.Clear();

            try
            {
                MMDevice? defaultRender = null;
                try { defaultRender = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
                catch { }

                var renderCollection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in renderCollection)
                {
                    bool isDef = defaultRender != null && device.ID == defaultRender.ID;
                    var wrapper = new AudioEndpointWrapper(device, AudioDeviceType.Playback, isDef);
                    _playbackDevices.Add(wrapper);
                    if (isDef) DefaultPlaybackDevice = wrapper;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to enumerate playback endpoints.");
            }

            try
            {
                MMDevice? defaultCapture = null;
                try { defaultCapture = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); }
                catch { }

                var captureCollection = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var device in captureCollection)
                {
                    bool isDef = defaultCapture != null && device.ID == defaultCapture.ID;
                    var wrapper = new AudioEndpointWrapper(device, AudioDeviceType.Recording, isDef);
                    _recordingDevices.Add(wrapper);
                    if (isDef) DefaultRecordingDevice = wrapper;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to enumerate recording endpoints.");
            }
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
        return RefreshSessionsAsync();
    }

    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg",
        "svchost",
        "searchhost",
        "searchui",
        "searchapp",
        "textinputhost",
        "systemsettings",
        "shellexperiencehost",
        "startmenuexperiencehost",
        "smartscreen",
        "wmiprvse",
        "dllhost",
        "conhost",
        "taskhostw",
        "applicationframehost",
        "lockapp",
        "openrgb",
        "nexus.app",
        "nexus",
        "idle",
        "system",
        "msedgewebview2",
        "explorer",
        "runtimebroker",
        "backgroundtaskhost",
        "gamebar",
        "gamebarft",
        "widgetservice",
        "widgets",
        "sihost",
        "ctfmon",
        "powershell",
        "cmd",
        "devenv",
        "code",
        "antigravity",
        "git",
        "vssvc",
        "spoolsv",
        "lsass",
        "csrss",
        "services",
        "winlogon",
        "fontdrvhost",
        "dwm"
    };

    public Task RefreshSessionsAsync()
    {
        lock (_lock)
        {
            _activeSessions.Clear();
            var seenPids = new HashSet<int>();

            try
            {
                var renderCollection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in renderCollection)
                {
                    try
                    {
                        var sessionManager = device.AudioSessionManager;
                        if (sessionManager == null) continue;
                        var sessions = sessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var s = sessions[i];
                            if (s.State != AudioSessionState.AudioSessionStateExpired)
                            {
                                int pid = (int)s.GetProcessID;
                                if (pid > 4 && seenPids.Add(pid))
                                {
                                    var wrapper = new AudioSessionWrapper(s);
                                    if (!string.IsNullOrWhiteSpace(wrapper.ProcessName) &&
                                        !IgnoredProcessNames.Contains(wrapper.ProcessName))
                                    {
                                        _activeSessions.Add(wrapper);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Could not enumerate active audio sessions across render endpoints.");
            }
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
        PeakLevelsUpdated?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void SetPlaybackDeviceVolume(string deviceId, float volume)
    {
        lock (_lock)
        {
            var dev = _playbackDevices.FirstOrDefault(d => d.Id == deviceId);
            if (dev != null) dev.MasterVolume = volume;
        }
    }

    public void SetPlaybackDeviceMute(string deviceId, bool mute)
    {
        lock (_lock)
        {
            var dev = _playbackDevices.FirstOrDefault(d => d.Id == deviceId);
            if (dev != null) dev.IsMuted = mute;
        }
    }

    public void SetRecordingDeviceVolume(string deviceId, float volume)
    {
        lock (_lock)
        {
            var dev = _recordingDevices.FirstOrDefault(d => d.Id == deviceId);
            if (dev != null) dev.MasterVolume = volume;
        }
    }

    public void SetRecordingDeviceMute(string deviceId, bool mute)
    {
        lock (_lock)
        {
            var dev = _recordingDevices.FirstOrDefault(d => d.Id == deviceId);
            if (dev != null) dev.IsMuted = mute;
        }
    }

    public void SetSessionVolume(int processId, float volume)
    {
        lock (_lock)
        {
            var session = _activeSessions.FirstOrDefault(s => s.ProcessId == processId);
            if (session != null) session.Volume = volume;
        }
    }

    public void SetSessionMute(int processId, bool mute)
    {
        lock (_lock)
        {
            var session = _activeSessions.FirstOrDefault(s => s.ProcessId == processId);
            if (session != null) session.IsMuted = mute;
        }
    }

    public Task SetDefaultPlaybackDeviceAsync(string deviceId)
    {
        _logger?.LogInformation("Requested default playback device change to {DeviceId}", deviceId);
        return Task.CompletedTask;
    }

    public Task SetDefaultRecordingDeviceAsync(string deviceId)
    {
        _logger?.LogInformation("Requested default recording device change to {DeviceId}", deviceId);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _enumerator.Dispose();
    }
}
