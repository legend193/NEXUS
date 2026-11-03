namespace Nexus.Abstractions.Plugins;

public class PluginMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Manufacturer { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedDevices { get; set; } = new();
    public bool RequiresAdministrator { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Status { get; set; } = "Ready";
}

public interface IPlugin
{
    PluginMetadata Metadata { get; }
    Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

public interface IPluginManager
{
    IReadOnlyList<PluginMetadata> InstalledPlugins { get; }
    Task LoadPluginsAsync(CancellationToken cancellationToken = default);
    Task EnablePluginAsync(string pluginId);
    Task DisablePluginAsync(string pluginId);
}
