using System;
using System.Runtime.InteropServices;

namespace Nexus.Engines.Audio.Interop;

public enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

public static class CombaseNativeMethods
{
    [DllImport("combase.dll", PreserveSig = false)]
    public static extern void RoGetActivationFactory(
        IntPtr activatableClassId,
        [In] ref Guid iid,
        [Out, MarshalAs(UnmanagedType.IInspectable)] out object factory);

    [DllImport("combase.dll", PreserveSig = false)]
    public static extern IntPtr WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length);

    [DllImport("combase.dll", PreserveSig = false)]
    public static extern void WindowsDeleteString(IntPtr hstring);
}

// Windows 10 (1809 - 21H2) and Windows 11
[ComImport]
[Guid("ab653000-8411-4944-b870-78904e85d95b")]
[InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
public interface IAudioPolicyConfigFactory_Win10_1809
{
    int __vfptr0();
    int __vfptr1();
    int __vfptr2();
    int __vfptr3();
    int __vfptr4();
    int __vfptr5();
    int __vfptr6();
    int __vfptr7();
    int __vfptr8();
    int __vfptr9();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        IntPtr deviceId);

    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        [MarshalAs(UnmanagedType.LPWStr)] out string deviceId);

    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

// Windows 10 (1803)
[ComImport]
[Guid("2a55050c-0069-4666-993d-82089408e010")]
[InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
public interface IAudioPolicyConfigFactory_Win10_1803
{
    int __vfptr0();
    int __vfptr1();
    int __vfptr2();
    int __vfptr3();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        IntPtr deviceId);

    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        [MarshalAs(UnmanagedType.LPWStr)] out string deviceId);

    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

// Windows 11 22H2 / 23H2 / 24H2
[ComImport]
[Guid("ab653000-8411-4944-b870-78904e85d95b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioPolicyConfigFactory_Win11
{
    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        [MarshalAs(UnmanagedType.LPWStr)] out string deviceId);

    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

[ComImport]
[Guid("7991283b-0177-4467-9257-5e602e1c71b4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, IntPtr key);
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumeratorCom
{
    int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr ppEndpoint);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IntPtr ppDevice);
    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
public class MMDeviceEnumeratorComObject
{
}
