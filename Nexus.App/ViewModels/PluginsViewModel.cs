using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexus.Abstractions.Plugins;
using Nexus.Core.PluginManager;

namespace Nexus.App.ViewModels;

public class PluginsViewModel : ViewModelBase
{
    private readonly PluginManager _pluginManager;

    public ObservableCollection<PluginMetadata> Plugins { get; } = new();

    public ICommand TogglePluginCommand { get; }

    public PluginsViewModel(PluginManager pluginManager)
    {
        _pluginManager = pluginManager;

        TogglePluginCommand = new RelayCommand(async param =>
        {
            if (param is PluginMetadata plugin)
            {
                if (plugin.IsEnabled)
                {
                    await _pluginManager.DisablePluginAsync(plugin.Id);
                }
                else
                {
                    await _pluginManager.EnablePluginAsync(plugin.Id);
                }
                RefreshList();
            }
        });

        RefreshList();
    }

    private void RefreshList()
    {
        Plugins.Clear();
        foreach (var p in _pluginManager.InstalledPlugins)
        {
            Plugins.Add(p);
        }
    }
}
