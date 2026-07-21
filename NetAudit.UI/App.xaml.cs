using System;
using System.Windows;
using System.Threading;
using Serilog;

namespace NetAudit.UI;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "NetAuditWireshark-SingleInstance";

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("NetAudit is already running.", "Single Instance", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Shutdown(1);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Mutex error");
        }

        base.OnStartup(e);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File("netaudit.log")
            .CreateLogger();
        Log.Information("NetAudit Pro Wireshark started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
