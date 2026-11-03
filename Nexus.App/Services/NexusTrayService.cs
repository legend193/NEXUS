using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Nexus.Abstractions.Audio;
using Nexus.Abstractions.Profiles;
using Nexus.Core.Configuration;

namespace Nexus.App.Services;

public class NexusTrayService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1024;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private readonly MainWindow _mainWindow;
    private readonly IProfileManager _profileManager;
    private readonly IAudioEngine _audioEngine;
    private readonly NexusConfiguration _config;
    private HwndSource? _hwndSource;
    private NOTIFYICONDATA _nid;
    private bool _isCreated;
    private Icon? _loadedIcon;

    public NexusTrayService(
        MainWindow mainWindow,
        IProfileManager profileManager,
        IAudioEngine audioEngine,
        NexusConfiguration config)
    {
        _mainWindow = mainWindow;
        _profileManager = profileManager;
        _audioEngine = audioEngine;
        _config = config;
    }

    public void Initialize()
    {
        if (_isCreated) return;

        var helper = new WindowInteropHelper(_mainWindow);
        IntPtr hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
        {
            helper.EnsureHandle();
            hwnd = helper.Handle;
        }

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        IntPtr appIcon = IntPtr.Zero;
        try
        {
            string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "nexus_logo.ico");
            if (System.IO.File.Exists(icoPath))
            {
                _loadedIcon = new Icon(icoPath, 16, 16);
                appIcon = _loadedIcon.Handle;
            }
        }
        catch { }

        if (appIcon == IntPtr.Zero)
        {
            appIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION fallback
        }

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = appIcon,
            szTip = "NEXUS — Unified PC Control Center"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONDBLCLK || eventId == WM_LBUTTONUP)
            {
                RestoreMainWindow();
                handled = true;
            }
            else if (eventId == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void RestoreMainWindow()
    {
        if (_mainWindow.Visibility != Visibility.Visible)
        {
            _mainWindow.Show();
        }
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
        SetForegroundWindow(new WindowInteropHelper(_mainWindow).Handle);
    }

    private void ShowContextMenu()
    {
        var contextMenu = new ContextMenu
        {
            Background = System.Windows.Media.Brushes.Black,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderSubtleBrush")
        };

        // Open
        var openItem = new MenuItem { Header = "Open NEXUS Control Center", FontWeight = FontWeights.Bold };
        openItem.Click += (_, _) => RestoreMainWindow();
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new Separator());

        // Quick Profiles
        var profilesHeader = new MenuItem { Header = "Active Hardware Profile" };

        var silentItem = new MenuItem { Header = "Silent Mode (Quiet)" };
        silentItem.Click += async (_, _) => await _profileManager.ActivateProfileAsync("silent-preset");
        profilesHeader.Items.Add(silentItem);

        var balancedItem = new MenuItem { Header = "Balanced Mode (Standard)" };
        balancedItem.Click += async (_, _) => await _profileManager.ActivateProfileAsync("balanced-preset");
        profilesHeader.Items.Add(balancedItem);

        var gamingItem = new MenuItem { Header = "Gaming Mode (High Performance)" };
        gamingItem.Click += async (_, _) => await _profileManager.ActivateProfileAsync("gaming-preset");
        profilesHeader.Items.Add(gamingItem);

        var perfItem = new MenuItem { Header = "Performance Mode (Max Fan RPM)" };
        perfItem.Click += async (_, _) => await _profileManager.ActivateProfileAsync("performance-preset");
        profilesHeader.Items.Add(perfItem);

        contextMenu.Items.Add(profilesHeader);

        // Mute Toggle
        var muteItem = new MenuItem { Header = "Mute / Unmute Master Audio" };
        muteItem.Click += (_, _) =>
        {
            if (_audioEngine.DefaultPlaybackDevice != null)
            {
                var dev = _audioEngine.DefaultPlaybackDevice;
                _audioEngine.SetPlaybackDeviceMute(dev.Id, !dev.IsMuted);
            }
        };
        contextMenu.Items.Add(muteItem);

        contextMenu.Items.Add(new Separator());

        // Exit
        var exitItem = new MenuItem { Header = "Exit NEXUS" };
        exitItem.Click += (_, _) =>
        {
            Dispose();
            Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);

        contextMenu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_isCreated)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _isCreated = false;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        _loadedIcon?.Dispose();
        _loadedIcon = null;
    }
}