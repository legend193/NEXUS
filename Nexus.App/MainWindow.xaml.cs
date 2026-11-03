using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Core.Configuration;
using Wpf.Ui.Controls;

namespace Nexus.App;

public partial class MainWindow : FluentWindow
{
    private readonly NexusConfiguration? _config;
    private NexusTrayService? _trayService;
    private Views.MiniHudWindow? _miniHud;

    public MainWindow(MainViewModel viewModel, NexusConfiguration config)
    {
        InitializeComponent();
        DataContext = viewModel;
        _config = config;

        Loaded += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && Application.Current is App app)
            {
                // Tray service initialized on window load
            }
        };
    }

    public void SetMiniHud(Views.MiniHudWindow miniHud)
    {
        _miniHud = miniHud;
    }

    private void BtnMiniHud_Click(object sender, RoutedEventArgs e)
    {
        if (_miniHud != null)
        {
            if (_miniHud.IsVisible) _miniHud.Hide();
            else _miniHud.Show();
        }
    }

    public void AttachTrayService(NexusTrayService trayService)
    {
        _trayService = trayService;
        _trayService.Initialize();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_config != null && _config.MinimizeToSystemTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _trayService?.Dispose();
        base.OnClosing(e);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_config != null && _config.MinimizeToSystemTray)
        {
            Hide();
        }
        else
        {
            Close();
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
