using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Nexus.App.Navigation;
using Nexus.Core.Configuration;

namespace Nexus.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    private NavigationItem _selectedNavItem;
    private bool _isSidebarCompact;
    private string _systemStatusText = "Ecosystem Active";

    public DashboardViewModel Dashboard { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set
        {
            if (_currentViewModel != value)
            {
                if (_currentViewModel is IActiveAware oldActive)
                {
                    oldActive.IsActive = false;
                }
                if (value is IActiveAware newActive)
                {
                    newActive.IsActive = true;
                }
                SetProperty(ref _currentViewModel, value);
            }
        }
    }

    public NavigationItem SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (SetProperty(ref _selectedNavItem, value) && value != null)
            {
                foreach (var item in NavigationItems)
                {
                    item.IsSelected = item == value;
                }
                CurrentViewModel = value.ViewModel;
            }
        }
    }

    public bool IsSidebarCompact
    {
        get => _isSidebarCompact;
        set => SetProperty(ref _isSidebarCompact, value);
    }

    public string SystemStatusText
    {
        get => _systemStatusText;
        set => SetProperty(ref _systemStatusText, value);
    }

    public ICommand ToggleSidebarCommand { get; }
    public ICommand NavigateCommand { get; }

    public MainViewModel(
        DashboardViewModel dashboardVm,
        RgbStudioViewModel rgbStudioVm,
        AudioStudioViewModel audioStudioVm,
        HardwareMonitorViewModel hardwareVm,
        CoolingViewModel coolingVm,
        DevicesViewModel devicesVm,
        PerformanceViewModel performanceVm,
        AutomationViewModel automationVm,
        ProfilesViewModel profilesVm,
        PluginsViewModel pluginsVm,
        SettingsViewModel settingsVm)
    {
        // 11 Core Navigation Sections categorized logically
        NavigationItems.Add(new NavigationItem(NavSection.Dashboard, "Dashboard", "IconDashboard", dashboardVm, "OVERVIEW"));
        NavigationItems.Add(new NavigationItem(NavSection.HardwareMonitor, "Hardware Monitor", "IconHardware", hardwareVm, "OVERVIEW"));

        NavigationItems.Add(new NavigationItem(NavSection.RgbStudio, "RGB Studio", "IconRgb", rgbStudioVm, "HARDWARE CONTROL"));
        NavigationItems.Add(new NavigationItem(NavSection.AudioStudio, "Audio Studio", "IconAudio", audioStudioVm, "HARDWARE CONTROL"));
        NavigationItems.Add(new NavigationItem(NavSection.Cooling, "Cooling", "IconCooling", coolingVm, "HARDWARE CONTROL"));

        NavigationItems.Add(new NavigationItem(NavSection.Devices, "Devices", "IconDevices", devicesVm, "TUNING & ENGINES"));
        NavigationItems.Add(new NavigationItem(NavSection.Performance, "Performance", "IconPerformance", performanceVm, "TUNING & ENGINES"));
        NavigationItems.Add(new NavigationItem(NavSection.Automation, "Automation", "IconAutomation", automationVm, "TUNING & ENGINES"));
        NavigationItems.Add(new NavigationItem(NavSection.Profiles, "Profiles", "IconProfiles", profilesVm, "TUNING & ENGINES"));

        NavigationItems.Add(new NavigationItem(NavSection.Plugins, "Plugins", "IconPlugins", pluginsVm, "SYSTEM & PREFERENCES"));
        NavigationItems.Add(new NavigationItem(NavSection.Settings, "Settings", "IconSettings", settingsVm, "SYSTEM & PREFERENCES"));

        _currentViewModel = dashboardVm;
        Dashboard = dashboardVm;
        if (_currentViewModel is IActiveAware initActive)
        {
            initActive.IsActive = true;
        }
        _selectedNavItem = NavigationItems[0];
        _selectedNavItem.IsSelected = true;

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCompact = !IsSidebarCompact);

        NavigateCommand = new RelayCommand(param =>
        {
            if (param is NavigationItem item)
            {
                SelectedNavItem = item;
            }
        });
    }
}
