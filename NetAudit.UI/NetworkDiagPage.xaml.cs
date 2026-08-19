using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Renci.SshNet;

namespace NetAudit.UI;

public partial class NetworkDiagPage : Page
{
    private SshClient? _sshClient;
    private string _currentLogs = string.Empty;
    private ObservableCollection<LogEntry> _analysisResults = new();

    public NetworkDiagPage()
    {
        InitializeComponent();
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void OnTestConnection(object sender, RoutedEventArgs e)
    {
        BtnTestConnection.IsEnabled = false;
        StatusBorder.Visibility = Visibility.Collapsed;
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            string host = TxtDeviceIP.Text.Trim();
            if (!int.TryParse(TxtPort.Text.Trim(), out int port)) port = 22;
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Text.Trim();

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
            {
                throw new Exception("Device IP and Username required");
            }

            StatusText.Text = "⏳ Testing TCP connection...";
            StatusBorder.Visibility = Visibility.Visible;

            // Dispose old connection if exists
            _sshClient?.Dispose();

            var connInfo = new PasswordConnectionInfo(host, port, username, password)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            _sshClient = new SshClient(connInfo);

            StatusText.Text = "✓ TCP OK\n⏳ SSH auth...";

            // Connect with timeout
            var connectTask = System.Threading.Tasks.Task.Run(() => _sshClient.Connect());
            if (!connectTask.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("SSH connection timeout");
            }

            if (!_sshClient.IsConnected)
            {
                throw new Exception("SSH connection failed");
            }

            // Test command
            var cmd = _sshClient.CreateCommand("show version | head -1");
            string testOutput = cmd.Execute();

            if (string.IsNullOrWhiteSpace(testOutput))
            {
                throw new Exception("Device not responding");
            }

            StatusText.Text = "✓ TCP OK\n✓ SSH OK\n✓ Device online";
            StatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 245, 233));
            BtnRunDiagnostic.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"❌ Connection failed:\n{ex.Message}";
            ErrorBorder.Visibility = Visibility.Visible;
            BtnRunDiagnostic.IsEnabled = false;
            _sshClient?.Dispose();
            _sshClient = null;
        }
        finally
        {
            BtnTestConnection.IsEnabled = true;
        }
    }

    private void OnRunDiagnostic(object sender, RoutedEventArgs e)
    {
        if (_sshClient == null || !_sshClient.IsConnected)
        {
            MessageBox.Show("Not connected. Test connection first.", "Network Diagnostics");
            return;
        }

        BtnRunDiagnostic.IsEnabled = false;
        ResultsBorder.Visibility = Visibility.Collapsed;
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            string vendor = (CmbVendor.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Cisco IOS";
            string logCommand = GetLogCommand(vendor);

            StatusText.Text = "⏳ Fetching logs...";

            var cmd = _sshClient.CreateCommand(logCommand);
            _currentLogs = cmd.Execute();

            if (string.IsNullOrWhiteSpace(_currentLogs))
            {
                ErrorText.Text = "⚠️ No logs returned from device";
                ErrorBorder.Visibility = Visibility.Visible;
                return;
            }

            AnalyzeLogs(_currentLogs);
            DisplayResults(vendor);
            StatusText.Text = "✓ Logs analyzed";
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"❌ Error: {ex.Message}";
            ErrorBorder.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnRunDiagnostic.IsEnabled = true;
        }
    }

    private string GetLogCommand(string vendor)
    {
        return vendor switch
        {
            "Cisco IOS" => "show logging",
            "Cisco IOS-XE" => "show logging",
            "Huawei VRP" => "display logbuffer",
            "Aruba OS" => "show log",
            _ => "show logging"
        };
    }

    private void AnalyzeLogs(string logs)
    {
        _analysisResults.Clear();

        var lines = logs.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        int errorCount = lines.Count(l => Regex.IsMatch(l, @"ERROR|CRIT", RegexOptions.IgnoreCase));
        int warningCount = lines.Count(l => Regex.IsMatch(l, @"WARN", RegexOptions.IgnoreCase));

        _analysisResults.Add(new LogEntry
        {
            Severity = "📊 SUMMARY",
            Message = $"Lines: {lines.Length} | Errors: {errorCount} | Warnings: {warningCount}"
        });

        var criticalPatterns = new[] {
            (@"interface.*down|port.*down|link down", "🔴 Interface Down"),
            (@"memory|buffer|overfl", "🔴 Memory Issue"),
            (@"cpu.*high", "🔴 CPU High"),
            (@"restart|reload", "🔴 Restart Detected"),
        };

        foreach (var (pattern, label) in criticalPatterns)
        {
            if (lines.Any(l => Regex.IsMatch(l, pattern, RegexOptions.IgnoreCase)))
            {
                _analysisResults.Add(new LogEntry { Severity = label, Message = "Found in logs" });
            }
        }

        if (errorCount == 0 && warningCount == 0)
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "✓ STATUS",
                Message = "No errors/warnings found"
            });
        }
    }

    private void DisplayResults(string vendor)
    {
        DeviceInfoText.Text = $"Device: {TxtDeviceIP.Text}:{TxtPort.Text} | Vendor: {vendor}";
        InterfacesList.ItemsSource = _analysisResults;
        ResultsBorder.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _sshClient?.Dispose();
    }
}

public class LogEntry
{
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}
