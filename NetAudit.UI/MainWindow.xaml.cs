using System.Windows;
using System.Windows.Controls;
using Serilog;

namespace NetAudit.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Log.Information("NetAudit Pro v4 launched");
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            StatusMessage.Text = $"Selected: {btn.Content}";
            Log.Information($"Menu clicked: {btn.Content}");
        }
    }

    private void OnStartScanClick(object sender, RoutedEventArgs e)
    {
        StatusMessage.Text = "Scan started...";
        Log.Information("Scan initiated");
    }
}
