using System.Windows.Input;
using Nexus.Core.Configuration;
using Nexus.Storage.Repositories;

namespace Nexus.App.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly NexusConfiguration _config;
    private readonly StorageRepositories _repositories;
    private string _accentColorHex;
    private bool _darkMode;
    private int _pollIntervalMs;
    private bool _startWithWindows;
    private bool _minimizeToTray;
    private string _dbLocation;
    private string _statusMessage = string.Empty;

    public string AccentColorHex
    {
        get => _accentColorHex;
        set => SetProperty(ref _accentColorHex, value);
    }

    public bool DarkMode
    {
        get => _darkMode;
        set => SetProperty(ref _darkMode, value);
    }

    public int PollIntervalMs
    {
        get => _pollIntervalMs;
        set => SetProperty(ref _pollIntervalMs, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public string DbLocation
    {
        get => _dbLocation;
        set => SetProperty(ref _dbLocation, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SaveSettingsCommand { get; }

    public SettingsViewModel(NexusConfiguration config, StorageRepositories repositories)
    {
        _config = config;
        _repositories = repositories;

        _accentColorHex = _config.AccentColorHex;
        _darkMode = _config.DarkMode;
        _pollIntervalMs = _config.HardwarePollIntervalMs;
        _startWithWindows = _config.StartWithWindows;
        _minimizeToTray = _config.MinimizeToSystemTray;

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _dbLocation = System.IO.Path.Combine(appData, "NEXUS", "nexus.db");
        }
        catch
        {
            _dbLocation = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "nexus.db");
        }

        SaveSettingsCommand = new RelayCommand(async () =>
        {
            _config.AccentColorHex = AccentColorHex;
            _config.DarkMode = DarkMode;
            _config.HardwarePollIntervalMs = PollIntervalMs;
            _config.StartWithWindows = StartWithWindows;
            _config.MinimizeToSystemTray = MinimizeToTray;

            await _repositories.SetSettingAsync("AccentColor", AccentColorHex);
            await _repositories.SetSettingAsync("PollIntervalMs", PollIntervalMs.ToString());

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null)
                    {
                        if (StartWithWindows)
                        {
                            var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                            if (!string.IsNullOrEmpty(exe))
                            {
                                key.SetValue("NEXUS", $"\"{exe}\" --minimized");
                            }
                        }
                        else
                        {
                            key.DeleteValue("NEXUS", false);
                        }
                    }
                }
                catch { }
            }

            StatusMessage = "Settings saved successfully to SQLite database.";
        });
    }
}
