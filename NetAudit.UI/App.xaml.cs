using System.Windows;
using Serilog;

namespace NetAudit.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File("netaudit.log")
            .CreateLogger();
        Log.Information("NetAudit Pro Wireshark started");
    }
}
