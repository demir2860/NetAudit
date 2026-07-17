namespace NetAudit.Plugins;

/// <summary>Base interface for all plugins</summary>
public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    Task InitializeAsync();
    Task ExecuteAsync();
}

/// <summary>Plugin metadata</summary>
public class PluginMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
