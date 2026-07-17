using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NetAudit.Database;
using NetAudit.Core.Interfaces;
using Serilog;

namespace NetAudit.UI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Setup logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(GetLogPath())
            .CreateLogger();

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Show main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        (_serviceProvider as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Register database
        services.AddSingleton(_ => new AppDbContext());

        // Register UI
        services.AddSingleton<MainWindow>();
    }

    private static string GetLogPath()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NetAudit", "logs");
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, "netaudit-.log");
    }
}
