using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Nexus.Abstractions.Audio;
using Nexus.Engines.Audio.Interop;
using IMMNotificationClient = Nexus.Engines.Audio.Interop.IMMNotificationClient;

namespace Nexus.Engines.Audio;

public class AggregatedAudioSession : IAudioSession
{
    private readonly List<AudioSessionControl> _childSessions = new();
    private readonly object _lock = new();
    private float _volume = 1.0f;
    private bool _isMuted;
    private float _peakLevel;
    private float _smoothedPeakLeft;
    private float _smoothedPeakRight;

    public int ProcessId { get; }
    public string ProcessName { get; }
    public string DisplayName { get; }
    public string? ExePath { get; }
    public string? IconPath { get; }
    public bool IsSystemSound { get; }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (_lock)
            {
                foreach (var s in _childSessions)
                {
                    try { s.SimpleAudioVolume.Volume = _volume; }
                    catch { }
                }
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            lock (_lock)
            {
                foreach (var s in _childSessions)
                {
                    try { s.SimpleAudioVolume.Mute = _isMuted; }
                    catch { }
                }
            }
        }
    }

    public float PeakLevel => _peakLevel;
    public float SmoothedPeakLeft => _smoothedPeakLeft;
    public float SmoothedPeakRight => _smoothedPeakRight;

    public AggregatedAudioSession(int processId, string procName, string dispName, string? exePath, bool isSystemSound)
    {
        ProcessId = processId;
        ProcessName = procName;
        DisplayName = dispName;
        ExePath = exePath;
        IsSystemSound = isSystemSound;
    }

    public void AddSession(AudioSessionControl session)
    {
        lock (_lock)
        {
            if (!_childSessions.Contains(session))
            {
                _childSessions.Add(session);
                try
                {
                    _volume = session.SimpleAudioVolume.Volume;
                    _isMuted = session.SimpleAudioVolume.Mute;
                }
                catch { }
            }
        }
    }

    public void ClearDeadSessions()
    {
        lock (_lock)
        {
            _childSessions.RemoveAll(s => s.State == AudioSessionState.AudioSessionStateExpired);
        }
    }

    public bool HasActiveSessions()
    {
        lock (_lock)
        {
            return _childSessions.Count > 0 &&
                   _childSessions.Any(s => s.State != AudioSessionState.AudioSessionStateExpired);
        }
    }

    public void UpdatePeaks()
    {
        float maxPeak = 0f;
        float peakL = 0f;
        float peakR = 0f;

        lock (_lock)
        {
            foreach (var s in _childSessions)
            {
                try
                {
                    if (s.State != AudioSessionState.AudioSessionStateExpired)
                    {
                        var meter = s.AudioMeterInformation;
                        float p = meter.MasterPeakValue;
                        if (p > maxPeak) maxPeak = p;

                        if (meter.PeakValues.Count >= 2)
                        {
                            if (meter.PeakValues[0] > peakL) peakL = meter.PeakValues[0];
                            if (meter.PeakValues[1] > peakR) peakR = meter.PeakValues[1];
                        }
                        else
                        {
                            if (p > peakL) peakL = p;
                            if (p > peakR) peakR = p;
                        }
                    }
                }
                catch { }
            }
        }

        _peakLevel = maxPeak;

        // Ballistics smoothing: Fast attack (0.85), smooth decay (0.88)
        if (peakL > _smoothedPeakLeft)
            _smoothedPeakLeft = _smoothedPeakLeft + 0.85f * (peakL - _smoothedPeakLeft);
        else
            _smoothedPeakLeft = Math.Max(0f, _smoothedPeakLeft * 0.88f);

        if (peakR > _smoothedPeakRight)
            _smoothedPeakRight = _smoothedPeakRight + 0.85f * (peakR - _smoothedPeakRight);
        else
            _smoothedPeakRight = Math.Max(0f, _smoothedPeakRight * 0.88f);
    }
}

public class AudioSessionManagerService : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly IMMDeviceEnumeratorCom? _comEnumerator;
    private readonly ILogger<AudioSessionManagerService>? _logger;
    private readonly ConcurrentDictionary<int, AggregatedAudioSession> _aggregatedSessions = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _meteringTask;

    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg", "svchost", "searchhost", "searchui", "searchapp", "textinputhost",
        "systemsettings", "shellexperiencehost", "startmenuexperiencehost", "smartscreen",
        "wmiprvse", "dllhost", "conhost", "taskhostw", "applicationframehost", "lockapp",
        "openrgb", "nexus.app", "nexus", "idle", "system", "msedgewebview2", "explorer",
        "runtimebroker", "backgroundtaskhost", "gamebar", "gamebarft", "widgetservice",
        "widgets", "sihost", "ctfmon", "powershell", "cmd", "devenv", "code", "antigravity",
        "git", "vssvc", "spoolsv", "lsass", "csrss", "services", "winlogon", "fontdrvhost", "dwm"
    };

    public event EventHandler? SessionsChanged;
    public event EventHandler? DevicesChanged;
    public event EventHandler? PeakMetersUpdated;

    public IReadOnlyList<AggregatedAudioSession> ActiveSessions =>
        _aggregatedSessions.Values.Where(s => s.HasActiveSessions()).ToList();

    public AudioSessionManagerService(ILogger<AudioSessionManagerService>? logger = null)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
        try
        {
            _comEnumerator = (IMMDeviceEnumeratorCom)new MMDeviceEnumeratorComObject();
            _comEnumerator.RegisterEndpointNotificationCallback(this);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not register IMMNotificationClient callback.");
        }

        StartMeteringLoop();
    }

    public Task RefreshSessionsAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var seenPids = new HashSet<int>();
                var renderDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in renderDevices)
                {
                    try
                    {
                        var sessionManager = device.AudioSessionManager;
                        if (sessionManager == null) continue;

                        var sessionList = sessionManager.Sessions;
                        for (int i = 0; i < sessionList.Count; i++)
                        {
                            var s = sessionList[i];
                            if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

                            int pid = (int)s.GetProcessID;
                            if (pid <= 4) continue;

                            seenPids.Add(pid);

                            var aggSession = _aggregatedSessions.GetOrAdd(pid, p => CreateAggregatedSession(p, s));
                            aggSession.AddSession(s);
                        }
                    }
                    catch { }
                }

                // Clean up expired processes
                foreach (var kvp in _aggregatedSessions.ToList())
                {
                    kvp.Value.ClearDeadSessions();
                    if (!seenPids.Contains(kvp.Key) && !kvp.Value.HasActiveSessions())
                    {
                        _aggregatedSessions.TryRemove(kvp.Key, out _);
                    }
                }

                SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to refresh audio sessions.");
            }
        });
    }

    private AggregatedAudioSession CreateAggregatedSession(int pid, AudioSessionControl s)
    {
        string procName = "Process #" + pid;
        string dispName = s.DisplayName;
        string? exePath = null;
        bool isSystem = s.IsSystemSoundsSession;

        try
        {
            using var proc = Process.GetProcessById(pid);
            procName = proc.ProcessName;
            try { exePath = proc.MainModule?.FileName; } catch { }

            if (string.IsNullOrWhiteSpace(dispName))
            {
                dispName = !string.IsNullOrWhiteSpace(proc.MainWindowTitle)
                    ? proc.MainWindowTitle
                    : proc.ProcessName;
            }
        }
        catch
        {
            procName = $"Process #{pid}";
        }

        if (string.IsNullOrWhiteSpace(dispName))
        {
            dispName = procName;
        }

        return new AggregatedAudioSession(pid, procName, dispName, exePath, isSystem);
    }

    private void StartMeteringLoop()
    {
        _meteringTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16)); // ~60 FPS
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (await timer.WaitForNextTickAsync(_cts.Token))
                    {
                        foreach (var session in _aggregatedSessions.Values)
                        {
                            session.UpdatePeaks();
                        }

                        PeakMetersUpdated?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
        });
    }

    #region IMMNotificationClient Implementation
    public void OnDeviceStateChanged(string deviceId, int newState)
    {
        _logger?.LogInformation("Audio endpoint {DeviceId} state changed: {State}", deviceId, newState);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshSessionsAsync();
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        _logger?.LogInformation("Audio endpoint added: {DeviceId}", pwstrDeviceId);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshSessionsAsync();
    }

    public void OnDeviceRemoved(string deviceId)
    {
        _logger?.LogInformation("Audio endpoint removed: {DeviceId}", deviceId);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshSessionsAsync();
    }

    public void OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
    {
        _logger?.LogInformation("Default audio endpoint changed: Flow {Flow}, Role {Role}, Id {DeviceId}", flow, role, defaultDeviceId);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshSessionsAsync();
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, IntPtr key)
    {
    }
    #endregion

    public void Dispose()
    {
        _cts.Cancel();
        try { _comEnumerator?.UnregisterEndpointNotificationCallback(this); } catch { }
        _enumerator.Dispose();
        _cts.Dispose();
    }
}
