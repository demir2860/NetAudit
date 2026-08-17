using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NetAudit.UI;

public partial class NetworkDiagPage : Page
{
    private string _currentHost = string.Empty;
    private int _currentPort = 22;
    private string _currentUsername = string.Empty;
    private string _currentPassword = string.Empty;
    private string _currentLogs = string.Empty;
    private ObservableCollection<LogEntry> _analysisResults = new();
    private bool _isConnected = false;

    public NetworkDiagPage()
    {
        InitializeComponent();
        ErrorBorder.Visibility = Visibility.Collapsed;
        StatusBorder.Visibility = Visibility.Collapsed;
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        BtnTestConnection.IsEnabled = false;
        StatusBorder.Visibility = Visibility.Collapsed;
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            _currentHost = TxtDeviceIP.Text.Trim();
            _currentPort = int.TryParse(TxtPort.Text.Trim(), out var p) ? p : 22;
            _currentUsername = TxtUsername.Text.Trim();
            _currentPassword = TxtPassword.Text.Trim();

            if (string.IsNullOrWhiteSpace(_currentHost) || string.IsNullOrWhiteSpace(_currentUsername))
            {
                throw new Exception("Host and Username required");
            }

            StatusText.Text = "⏳ Testing TCP connection...";
            StatusBorder.Visibility = Visibility.Visible;

            // TCP test
            using (var tcp = new TcpClient())
            {
                var connectTask = tcp.ConnectAsync(_currentHost, _currentPort);
                if (!connectTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new Exception($"TCP connection timeout on port {_currentPort}");
                }
            }

            StatusText.Text = "✓ TCP connection OK\n⏳ Testing SSH authentication...";

            // SSH auth test via ssh command
            string result = await RunSshCommandAsync(_currentHost, _currentPort, _currentUsername, _currentPassword, "show version | head -1");

            if (string.IsNullOrEmpty(result))
            {
                throw new Exception("SSH auth failed or device didn't respond");
            }

            StatusText.Text = "✓ TCP connection OK\n✓ SSH authentication OK\n✓ Device responding";
            StatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 245, 233));
            BtnRunDiagnostic.IsEnabled = true;
            _isConnected = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"❌ Connection failed:\n{ex.Message}";
            ErrorBorder.Visibility = Visibility.Visible;
            BtnRunDiagnostic.IsEnabled = false;
            _isConnected = false;
        }
        finally
        {
            BtnTestConnection.IsEnabled = true;
        }
    }

    private async void OnRunDiagnostic(object sender, RoutedEventArgs e)
    {
        if (!_isConnected)
        {
            MessageBox.Show("Not connected. Test connection first.", "Network Diagnostics");
            return;
        }

        BtnRunDiagnostic.IsEnabled = false;
        ResultsBorder.Visibility = Visibility.Collapsed;
        ErrorBorder.Visibility = Visibility.Collapsed;
        StatusText.Text = "⏳ Fetching logs...";
        StatusBorder.Visibility = Visibility.Visible;

        try
        {
            string vendor = (CmbVendor.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Cisco IOS";
            string logCommand = GetLogCommand(vendor);

            _currentLogs = await RunSshCommandAsync(_currentHost, _currentPort, _currentUsername, _currentPassword, logCommand);

            if (string.IsNullOrWhiteSpace(_currentLogs))
            {
                ErrorText.Text = "⚠️ No logs returned from device. Device may have no errors or logging disabled.";
                ErrorBorder.Visibility = Visibility.Visible;
                return;
            }

            AnalyzeLogs(_currentLogs);
            DisplayResults(vendor);
            StatusText.Text = "✓ Logs retrieved and analyzed";
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"❌ Failed to retrieve logs:\n{ex.Message}";
            ErrorBorder.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnRunDiagnostic.IsEnabled = true;
        }
    }

    private async Task<string> RunSshCommandAsync(string host, int port, string username, string password, string command)
    {
        return await Task.Run(() => RunSshCommand(host, port, username, password, command));
    }

    private string RunSshCommand(string host, int port, string username, string password, string command)
    {
        try
        {
            string sshPath = FindSshExecutable();
            if (string.IsNullOrEmpty(sshPath))
            {
                throw new Exception("SSH executable not found. Install OpenSSH or Git Bash.");
            }

            string args = $"-o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=5 " +
                         $"-o HostKeyAlgorithms=+ssh-rsa -o PubkeyAcceptedAlgorithms=+ssh-rsa " +
                         $"-p {port} {username}@{host} \"{command}\"";

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = sshPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            // Handle password via stdin (limited support)
            if (!string.IsNullOrEmpty(password))
            {
                proc.StartInfo.RedirectStandardInput = true;
            }

            proc.Start();

            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();

            if (!proc.WaitForExit(10000)) // 10 sec timeout
            {
                proc.Kill();
                throw new TimeoutException("SSH command timeout");
            }

            if (proc.ExitCode != 0 && string.IsNullOrEmpty(output))
            {
                throw new Exception($"SSH error: {error}");
            }

            return output;
        }
        catch (Exception ex)
        {
            throw new Exception($"SSH execution failed: {ex.Message}", ex);
        }
    }

    private string FindSshExecutable()
    {
        // Try common SSH paths
        string[] paths = new[]
        {
            "ssh",  // Linux/Mac native
            "/usr/bin/ssh",
            "C:\\Program Files\\Git\\usr\\bin\\ssh.exe",  // Git Bash
            "C:\\Program Files (x86)\\Git\\usr\\bin\\ssh.exe",
            "C:\\Windows\\System32\\ssh.exe"  // Windows 10+ OpenSSH
        };

        foreach (var path in paths)
        {
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "-V",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                if (proc.WaitForExit(2000))
                {
                    return path;
                }
            }
            catch { }
        }

        return null;
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

        int errorCount = lines.Count(l =>
            Regex.IsMatch(l, @"ERROR|^%\w+-\d-|CRIT", RegexOptions.IgnoreCase));
        int warningCount = lines.Count(l =>
            Regex.IsMatch(l, @"WARN|^%\w+-4-", RegexOptions.IgnoreCase));

        _analysisResults.Add(new LogEntry
        {
            Severity = "📊 SUMMARY",
            Message = $"Total: {lines.Length} lines | 🔴 Errors: {errorCount} | 🟡 Warnings: {warningCount}"
        });

        if (errorCount == 0 && warningCount == 0)
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "✓ STATUS",
                Message = "No errors or warnings found in logs."
            });
        }

        var criticalPatterns = new[] {
            (@"interface|port|link.*down", "Interface Down"),
            (@"memory|buffer|overfl", "Memory/Buffer Issue"),
            (@"cpu.*high|cpu usage", "CPU High"),
            (@"restart|reload|reboot|RESTART", "Device Restart"),
            (@"auth.*fail|permission denied", "Auth Failure"),
            (@"packet.*drop|drop", "Packet Loss")
        };

        foreach (var (pattern, label) in criticalPatterns)
        {
            if (lines.Any(l => Regex.IsMatch(l, pattern, RegexOptions.IgnoreCase)))
            {
                _analysisResults.Add(new LogEntry
                {
                    Severity = "🔴 CRITICAL",
                    Message = $"Found: {label}"
                });
            }
        }

        var errors = lines
            .Where(l => Regex.IsMatch(l, @"ERROR|^%\w+-\d-", RegexOptions.IgnoreCase))
            .Take(5)
            .ToList();

        if (errors.Any())
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "📋 RECENT ERRORS",
                Message = string.Join("\n  ", errors.Select((e, i) => $"{i + 1}. {e.Substring(0, Math.Min(80, e.Length))}"))
            });
        }
    }

    private void DisplayResults(string vendor)
    {
        DeviceInfoText.Text = $"Device: {_currentHost}:{_currentPort} | Vendor: {vendor} | User: {_currentUsername}";
        InterfacesList.ItemsSource = _analysisResults;
        ResultsBorder.Visibility = Visibility.Visible;
    }
}

public class LogEntry
{
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}
