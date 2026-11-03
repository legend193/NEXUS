using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexus.Abstractions.Profiles;
using Nexus.Core.ProfileManager;

namespace Nexus.App.ViewModels;

public class ProfileDisplayItem : ViewModelBase
{
    private readonly ProfileData _data;
    private bool _isActive;

    public string Id => _data.Id;
    public string Name => _data.Name;
    public string Description => _data.Description;
    public string Mode => _data.Mode.ToString();
    public string RgbEffect => _data.RgbConfig.EffectType.ToString();
    public string FanCurve => _data.FanCurves.Count > 0 ? _data.FanCurves[0].Name : "Standard Curve";

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ProfileDisplayItem(ProfileData data, bool isActive)
    {
        _data = data;
        _isActive = isActive;
    }

    public ProfileData RawData => _data;
}

public class ProfilesViewModel : ViewModelBase
{
    private readonly ProfileManager _profileManager;
    private ProfileDisplayItem? _selectedProfile;
    private string _exportedJson = string.Empty;

    public ObservableCollection<ProfileDisplayItem> Profiles { get; } = new();

    public ProfileDisplayItem? SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
    }

    public string ExportedJson
    {
        get => _exportedJson;
        set
        {
            if (SetProperty(ref _exportedJson, value))
            {
                OnPropertyChanged(nameof(HasExportedJson));
            }
        }
    }

    public bool HasExportedJson => !string.IsNullOrWhiteSpace(ExportedJson);

    public ICommand ActivateProfileCommand { get; }
    public ICommand DuplicateProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand ExportProfileCommand { get; }
    public ICommand CloseExportCommand { get; }

    public ProfilesViewModel(ProfileManager profileManager)
    {
        _profileManager = profileManager;

        ActivateProfileCommand = new RelayCommand(async param =>
        {
            if (param is ProfileDisplayItem p)
            {
                await _profileManager.ActivateProfileAsync(p.Id);
                RefreshList();
            }
        });

        DuplicateProfileCommand = new RelayCommand(async param =>
        {
            if (param is ProfileDisplayItem p)
            {
                await _profileManager.DuplicateProfileAsync(p.Id);
                RefreshList();
            }
        });

        DeleteProfileCommand = new RelayCommand(async param =>
        {
            if (param is ProfileDisplayItem p)
            {
                await _profileManager.DeleteProfileAsync(p.Id);
                RefreshList();
            }
        });

        ExportProfileCommand = new RelayCommand(async param =>
        {
            if (param is ProfileDisplayItem p)
            {
                ExportedJson = await _profileManager.ExportProfileJsonAsync(p.Id);
            }
        });

        CloseExportCommand = new RelayCommand(() => ExportedJson = string.Empty);

        RefreshList();
    }

    private void RefreshList()
    {
        Profiles.Clear();
        string activeId = _profileManager.ActiveProfile.Id;
        foreach (var p in _profileManager.Profiles)
        {
            Profiles.Add(new ProfileDisplayItem(p, p.Id == activeId));
        }
        SelectedProfile = Profiles.FirstOrDefault(x => x.Id == activeId);
    }
}
