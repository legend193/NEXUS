using Nexus.App.ViewModels;

namespace Nexus.App.Navigation;

public enum NavSection
{
    Dashboard,
    RgbStudio,
    AudioStudio,
    HardwareMonitor,
    Cooling,
    Devices,
    Performance,
    Automation,
    Profiles,
    Plugins,
    Settings
}

public class NavigationItem : ViewModelBase
{
    private bool _isSelected;
    private string? _badgeText;

    public NavSection Section { get; init; }
    public string Title { get; init; } = string.Empty;
    public string IconKey { get; init; } = string.Empty;
    public string Category { get; init; } = "OVERVIEW";
    public ViewModelBase ViewModel { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string? BadgeText
    {
        get => _badgeText;
        set => SetProperty(ref _badgeText, value);
    }

    public NavigationItem(NavSection section, string title, string iconKey, ViewModelBase viewModel, string category = "OVERVIEW")
    {
        Section = section;
        Title = title;
        IconKey = iconKey;
        ViewModel = viewModel;
        Category = category;
    }
}
