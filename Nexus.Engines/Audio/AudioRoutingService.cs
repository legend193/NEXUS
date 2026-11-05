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
    private readonly ILogger<AudioRoutingService>? _logger;
    private readonly bool _isSupported;

    public bool IsSupported => _isSupported;

    public AudioRoutingService(ILogger<AudioRoutingService>? logger = null)
    {
        _logger = logger;
        _isSupported = Environment.OSVersion.Platform == PlatformID.Win32NT &&
                       Environment.OSVersion.Version.Major >= 10;
    }

    public Task<bool> SetAppOutputDeviceAsync(int processId, string endpointDeviceId)
    {
        if (!_isSupported || processId <= 0)
        {
            return Task.FromResult(false);
        }

        return Task.Run(() =>
        {
            try
            {
                string normalizedDeviceId = NormalizeDeviceId(endpointDeviceId);
                bool success = InvokeAudioPolicyConfig((factory, win11Factory, win10Factory) =>
                {
                    int hr = 0;
                    IntPtr hDeviceId = IntPtr.Zero;
                    try
                    {
                        if (!string.IsNullOrEmpty(normalizedDeviceId))
                        {
                            hDeviceId = CombaseNativeMethods.WindowsCreateString(normalizedDeviceId, normalizedDeviceId.Length);
                        }

                        if (win11Factory != null)
                        {
                            hr = win11Factory.SetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eMultimedia, normalizedDeviceId);
                            _ = win11Factory.SetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eConsole, normalizedDeviceId);
                        }
                        else if (win10Factory != null)
                        {
                            hr = win10Factory.SetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eMultimedia, hDeviceId);
                            _ = win10Factory.SetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eConsole, hDeviceId);
                        }
                        else if (factory is IAudioPolicyConfigFactory_Win10_1803 oldFactory)
                        {
                            hr = oldFactory.SetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eMultimedia, hDeviceId);
                            _ = oldFactory.SetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eConsole, hDeviceId);
                        }
                    }
                    finally
                    {
                        if (hDeviceId != IntPtr.Zero)
                        {
                            CombaseNativeMethods.WindowsDeleteString(hDeviceId);
                        }
                    }

                    return hr >= 0;
                });

                _logger?.LogInformation("SetAppOutputDevice PID {Pid} -> {DeviceId}: {Result}", processId, endpointDeviceId, success);
                return success;
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
                string? routedDev = null;
                InvokeAudioPolicyConfig((factory, win11Factory, win10Factory) =>
                {
                    int hr = -1;
                    if (win11Factory != null)
                    {
                        hr = win11Factory.GetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eMultimedia, out routedDev);
                    }
                    else if (win10Factory != null)
                    {
                        hr = win10Factory.GetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eMultimedia, out routedDev);
                    }
                    else if (factory is IAudioPolicyConfigFactory_Win10_1803 oldFactory)
                    {
                        hr = oldFactory.GetPersistedDefaultAudioEndpoint((uint)processId, EDataFlow.eRender, ERole.eMultimedia, out routedDev);
                    }

                    return hr >= 0;
                });

                return DenormalizeDeviceId(routedDev);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Could not get persisted audio endpoint for PID {Pid}", processId);
                return null;
            }
        });
    }

    public Task<bool> ResetAppRoutingAsync(int processId)
    {
        return SetAppOutputDeviceAsync(processId, string.Empty);
    }

    private static string NormalizeDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;

        // If it starts with standard MMDevAPI GUID prefix, convert to WinRT device interface path
        if (deviceId.StartsWith("{0.0.0.00000000}", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\?\SWD#MMDEVAPI#" + deviceId + @"#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        }

        return deviceId;
    }

    private static string? DenormalizeDeviceId(string? winRtPath)
    {
        if (string.IsNullOrWhiteSpace(winRtPath)) return null;

        // If it's formatted like \\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#... extract the clean MMDevAPI ID
        int idx = winRtPath.IndexOf("{0.0.0.00000000}", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int hashIdx = winRtPath.IndexOf('#', idx);
            if (hashIdx > idx)
            {
                return winRtPath.Substring(idx, hashIdx - idx);
            }
            return winRtPath.Substring(idx);
        }

        return winRtPath;
    }

    private static bool InvokeAudioPolicyConfig(Func<object, IAudioPolicyConfigFactory_Win11?, IAudioPolicyConfigFactory_Win10_1809?, bool> action)
    {
        IntPtr hClass = IntPtr.Zero;
        try
        {
            hClass = CombaseNativeMethods.WindowsCreateString(AudioPolicyConfigClass, AudioPolicyConfigClass.Length);

            // Attempt 1: Windows 11 IAudioPolicyConfigFactory
            Guid iidWin11 = typeof(IAudioPolicyConfigFactory_Win11).GUID;
            try
            {
                CombaseNativeMethods.RoGetActivationFactory(hClass, ref iidWin11, out object factoryWin11);
                if (factoryWin11 is IAudioPolicyConfigFactory_Win11 win11)
                {
                    return action(factoryWin11, win11, null);
                }
            }
            catch { }

            // Attempt 2: Windows 10 (1809+) IAudioPolicyConfigFactory
            Guid iidWin10 = typeof(IAudioPolicyConfigFactory_Win10_1809).GUID;
            try
            {
                CombaseNativeMethods.RoGetActivationFactory(hClass, ref iidWin10, out object factoryWin10);
                if (factoryWin10 is IAudioPolicyConfigFactory_Win10_1809 win10)
                {
                    return action(factoryWin10, null, win10);
                }
            }
            catch { }

            // Attempt 3: Windows 10 (1803)
            Guid iid1803 = typeof(IAudioPolicyConfigFactory_Win10_1803).GUID;
            try
            {
                CombaseNativeMethods.RoGetActivationFactory(hClass, ref iid1803, out object factory1803);
                if (factory1803 != null)
                {
                    return action(factory1803, null, null);
                }
            }
            catch { }

            return false;
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
