using NetAudit.Core.Interfaces;
using NetAudit.Plugins;

namespace NetAudit.Switch;

public class SwitchAnalyzer : IPlugin
{
    public string Name => "Switch Analyzer";
    public string Version => "1.0.0";

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ExecuteAsync() => Task.CompletedTask;
}
