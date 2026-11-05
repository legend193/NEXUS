using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Audio;
using Nexus.Engines.Audio.Interop;

namespace Nexus.Engines.Audio;

public class AudioRoutingService : IAudioRoutingService
{
    private const string AudioPolicyConfigClass = "Windows.Media.Internal.AudioPolicyConfig";
    private const string DEVINTERFACE_AUDIO_RENDER = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string DEVINTERFACE_AUDIO_CAPTURE = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";
    private const string MMDEVAPI_TOKEN = @"\\?\SWD#MMDEVAPI#";

    private readonly ILogger<AudioRoutingService>? _logger;
    private readonly bool _isSupported;
    private readonly object _lock = new();

    private IAudioPolicyConfigFactoryVariantFor21H2? _policyConfig21H2;
    private IAudioPolicyConfigFactoryVariantForDownlevel? _policyConfigDownlevel;

    public bool IsSupported => _isSupported;

    public AudioRoutingService(ILogger<AudioRoutingService>? logger = null)
    {
        _logger = logger;
        _isSupported = Environment.OSVersion.Platform == PlatformID.Win32NT &&
                       Environment.OSVersion.Version.Major >= 10;
    }

    private void EnsurePolicyConfig()
    {
        if (_policyConfig21H2 != null || _policyConfigDownlevel != null) return;

        lock (_lock)
        {
            if (_policyConfig21H2 != null || _policyConfigDownlevel != null) return;

            IntPtr hClass = IntPtr.Zero;
            try
            {
                int hr = CombaseNativeMethods.WindowsCreateString(
                    AudioPolicyConfigClass,
                    (uint)AudioPolicyConfigClass.Length,
                    out hClass);

                if (hr < 0 || hClass == IntPtr.Zero) return;

                // Try 21H2 / Win11 variant first
                try
                {
                    Guid iid21H2 = typeof(IAudioPolicyConfigFactoryVariantFor21H2).GUID;
                    CombaseNativeMethods.RoGetActivationFactory(hClass, ref iid21H2, out object factoryObj);
                    if (factoryObj is IAudioPolicyConfigFactoryVariantFor21H2 f21H2)
                    {
                        _policyConfig21H2 = f21H2;
                        return;
                    }
                }
                catch { }

                // Fallback to Downlevel Win10 variant
                try
                {
                    Guid iidDown = typeof(IAudioPolicyConfigFactoryVariantForDownlevel).GUID;
                    CombaseNativeMethods.RoGetActivationFactory(hClass, ref iidDown, out object downObj);
                    if (downObj is IAudioPolicyConfigFactoryVariantForDownlevel fDown)
                    {
                        _policyConfigDownlevel = fDown;
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to activate AudioPolicyConfig COM factory.");
            }
            finally
            {
                if (hClass != IntPtr.Zero)
                {
                    CombaseNativeMethods.WindowsDeleteString(hClass);
                }
            }
        }
    }

    public static string GenerateDeviceId(string deviceId, EDataFlow flow = EDataFlow.eRender)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;

        // If already formatted, return as-is
        if (deviceId.StartsWith(MMDEVAPI_TOKEN, StringComparison.OrdinalIgnoreCase))
        {
            return deviceId;
        }

        string cleanId = deviceId.Trim();
        string suffix = flow == EDataFlow.eRender ? DEVINTERFACE_AUDIO_RENDER : DEVINTERFACE_AUDIO_CAPTURE;
        return $"{MMDEVAPI_TOKEN}{cleanId}{suffix}";
    }

    public static string UnpackDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;

        string clean = deviceId;
        if (clean.StartsWith(MMDEVAPI_TOKEN, StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(MMDEVAPI_TOKEN.Length);
        }

        if (clean.EndsWith(DEVINTERFACE_AUDIO_RENDER, StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(0, clean.Length - DEVINTERFACE_AUDIO_RENDER.Length);
        }
        else if (clean.EndsWith(DEVINTERFACE_AUDIO_CAPTURE, StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(0, clean.Length - DEVINTERFACE_AUDIO_CAPTURE.Length);
        }

        return clean;
    }

    public Task<bool> SetAppOutputDeviceAsync(int processId, string? endpointDeviceId)
    {
        if (!_isSupported || processId <= 0)
        {
            return Task.FromResult(false);
        }

        return Task.Run(() =>
        {
            try
            {
                EnsurePolicyConfig();
                IntPtr hDeviceId = IntPtr.Zero;

                try
                {
                    if (!string.IsNullOrWhiteSpace(endpointDeviceId))
                    {
                        string fullDevId = GenerateDeviceId(endpointDeviceId, EDataFlow.eRender);
                        CombaseNativeMethods.WindowsCreateString(
                            fullDevId,
                            (uint)fullDevId.Length,
                            out hDeviceId);
                    }

                    int hr1 = -1;
                    int hr2 = -1;

                    if (_policyConfig21H2 != null)
                    {
                        hr1 = _policyConfig21H2.SetPersistedDefaultAudioEndpoint(
                            (uint)processId,
                            EDataFlow.eRender,
                            ERole.eMultimedia,
                            hDeviceId);

                        hr2 = _policyConfig21H2.SetPersistedDefaultAudioEndpoint(
                            (uint)processId,
                            EDataFlow.eRender,
                            ERole.eConsole,
                            hDeviceId);
                    }
                    else if (_policyConfigDownlevel != null)
                    {
                        hr1 = _policyConfigDownlevel.SetPersistedDefaultAudioEndpoint(
                            (uint)processId,
                            EDataFlow.eRender,
                            ERole.eMultimedia,
                            hDeviceId);

                        hr2 = _policyConfigDownlevel.SetPersistedDefaultAudioEndpoint(
                            (uint)processId,
                            EDataFlow.eRender,
                            ERole.eConsole,
                            hDeviceId);
                    }

                    // Fallback: If formatted device string failed, try with raw/unpacked endpoint ID
                    if (hr1 < 0 && hr2 < 0 && !string.IsNullOrWhiteSpace(endpointDeviceId))
                    {
                        string cleanId = UnpackDeviceId(endpointDeviceId);
                        if (!string.IsNullOrEmpty(cleanId))
                        {
                            if (hDeviceId != IntPtr.Zero)
                            {
                                CombaseNativeMethods.WindowsDeleteString(hDeviceId);
                                hDeviceId = IntPtr.Zero;
                            }

                            CombaseNativeMethods.WindowsCreateString(
                                cleanId,
                                (uint)cleanId.Length,
                                out hDeviceId);

                            if (_policyConfig21H2 != null)
                            {
                                hr1 = _policyConfig21H2.SetPersistedDefaultAudioEndpoint(
                                    (uint)processId,
                                    EDataFlow.eRender,
                                    ERole.eMultimedia,
                                    hDeviceId);

                                hr2 = _policyConfig21H2.SetPersistedDefaultAudioEndpoint(
                                    (uint)processId,
                                    EDataFlow.eRender,
                                    ERole.eConsole,
                                    hDeviceId);
                            }
                            else if (_policyConfigDownlevel != null)
                            {
                                hr1 = _policyConfigDownlevel.SetPersistedDefaultAudioEndpoint(
                                    (uint)processId,
                                    EDataFlow.eRender,
                                    ERole.eMultimedia,
                                    hDeviceId);

                                hr2 = _policyConfigDownlevel.SetPersistedDefaultAudioEndpoint(
                                    (uint)processId,
                                    EDataFlow.eRender,
                                    ERole.eConsole,
                                    hDeviceId);
                            }
                        }
                    }

                    bool success = hr1 >= 0 || hr2 >= 0;
                    _logger?.LogInformation("SetAppOutputDevice PID {Pid} -> {DeviceId} (hr1: 0x{Hr1:X8}, hr2: 0x{Hr2:X8})",
                        processId, endpointDeviceId, hr1, hr2);
                    return success;
                }
                finally
                {
                    if (hDeviceId != IntPtr.Zero)
                    {
                        CombaseNativeMethods.WindowsDeleteString(hDeviceId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to set audio routing for PID {Pid}", processId);
                return false;
            }
        });
    }

    public Task<string?> GetAppOutputDeviceAsync(int processId)
    {
        if (!_isSupported || processId <= 0)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.Run(() =>
        {
            try
            {
                EnsurePolicyConfig();
                string? rawDeviceId = null;
                int hr = -1;

                if (_policyConfig21H2 != null)
                {
                    hr = _policyConfig21H2.GetPersistedDefaultAudioEndpoint(
                        (uint)processId,
                        EDataFlow.eRender,
                        ERole.eMultimedia,
                        out rawDeviceId);
                }
                else if (_policyConfigDownlevel != null)
                {
                    hr = _policyConfigDownlevel.GetPersistedDefaultAudioEndpoint(
                        (uint)processId,
                        EDataFlow.eRender,
                        ERole.eMultimedia,
                        out rawDeviceId);
                }

                if (hr >= 0 && !string.IsNullOrEmpty(rawDeviceId))
                {
                    return UnpackDeviceId(rawDeviceId);
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to get audio routing for PID {Pid}", processId);
                return null;
            }
        });
    }

    public Task<bool> ResetAppRoutingAsync(int processId)
    {
        return SetAppOutputDeviceAsync(processId, null);
    }

    public Task<bool> ResetAppRoutingToDefaultAsync(int processId)
    {
        return SetAppOutputDeviceAsync(processId, null);
    }

    public Task<bool> SetDefaultPlaybackDeviceAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                var policyConfig = (IPolicyConfigVista)new PolicyConfigVistaClient();
                string cleanId = UnpackDeviceId(deviceId);
                int hr1 = policyConfig.SetDefaultEndpoint(cleanId, ERole.eConsole);
                int hr2 = policyConfig.SetDefaultEndpoint(cleanId, ERole.eMultimedia);
                int hr3 = policyConfig.SetDefaultEndpoint(cleanId, ERole.eCommunications);
                bool success = hr1 >= 0 || hr2 >= 0;
                _logger?.LogInformation("SetDefaultPlaybackDevice {DeviceId} -> hr1=0x{Hr1:X8}, hr2=0x{Hr2:X8}, hr3=0x{Hr3:X8}", cleanId, hr1, hr2, hr3);
                return success;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to set default playback endpoint {DeviceId}", deviceId);
                return false;
            }
        });
    }
}
