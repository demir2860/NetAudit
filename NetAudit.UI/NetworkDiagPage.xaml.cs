using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Renci.SshNet;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Fonts;

namespace NetAudit.UI;

public partial class NetworkDiagPage : Page
{
    private SshClient? _sshClient;
    private bool _isConnected = false;
    private string _currentLogs = string.Empty;
    private ObservableCollection<LogEntry> _analysisResults = new();

    public NetworkDiagPage()
    {
        InitializeComponent();
        ErrorBorder.Visibility = Visibility.Collapsed;
        TxtDeviceIP.GotFocus += (s, e) => TxtDeviceIP.SelectAll();
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        BtnTestConnection.IsEnabled = false;
        BtnGenerateReport.IsEnabled = false;
        StatusBorder.Visibility = Visibility.Collapsed;
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            string host = TxtDeviceIP.Text.Trim();
            if (!int.TryParse(TxtPort.Text.Trim(), out int port)) port = 22;
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password;

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
                Timeout = TimeSpan.FromSeconds(20)
            };
            _sshClient = new SshClient(connInfo);
            _sshClient.KeepAliveInterval = TimeSpan.FromSeconds(10);

            StatusText.Text = "✓ TCP OK\n⏳ SSH auth...";

            // Try SSH connect with timeout (async)
            bool sshConnected = false;
            try
            {
                await System.Threading.Tasks.Task.Run(() => _sshClient.Connect());
                sshConnected = true;
            }
            catch (Exception sshEx)
            {
                // SSH failed, try HTTP/HTTPS fallback
                StatusText.Text = "⏳ SSH port 22 failed, trying ports 443/80...";

                int[] fallbackPorts = { 443, 80 };
                foreach (int fbPort in fallbackPorts)
                {
                    try
                    {
                        var tcpClient = new System.Net.Sockets.TcpClient();
                        await System.Threading.Tasks.Task.Run(() => tcpClient.Connect(host, fbPort));
                        tcpClient.Close();

                        string scheme = fbPort == 443 ? "HTTPS" : "HTTP";
                        ErrorText.Text = $"✓ SSH port 22 not available\n✓ {scheme} port {fbPort} is open\n\nFor web access: https://{host}:{fbPort}\n\nPlease use web interface to download logs or enable SSH port 22.";
                        ErrorBorder.Visibility = Visibility.Visible;
                        BtnRunDiagnostic.IsEnabled = false;
                        return;
                    }
                    catch { }
                }

                throw new Exception($"SSH failed: {sshEx.Message}\nSSH port 22, HTTPS 443, and HTTP 80 all unreachable.");
            }

            if (!_sshClient.IsConnected)
            {
                throw new Exception("SSH connection failed");
            }

            StatusText.Text = "✓ TCP OK\n✓ SSH OK\n⏳ Testing device...";

            // Test command (async)
            var cmd = _sshClient.CreateCommand("show version | head -1");
            string testOutput = await System.Threading.Tasks.Task.Run(() => cmd.Execute());

            if (string.IsNullOrWhiteSpace(testOutput))
            {
                throw new Exception("Device not responding");
            }

            StatusText.Text = "✓ TCP OK\n✓ SSH OK\n✓ Device online";
            StatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 245, 233));
            _isConnected = true;
            BtnRunDiagnostic.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"❌ Connection failed:\n{ex.Message}";
            ErrorBorder.Visibility = Visibility.Visible;
            BtnRunDiagnostic.IsEnabled = false;
            _isConnected = false;
            _sshClient?.Dispose();
            _sshClient = null;
        }
        finally
        {
            BtnTestConnection.IsEnabled = true;
        }
    }

    private void OnGenerateReport(object sender, RoutedEventArgs e)
    {
        if (_analysisResults.Count == 0)
        {
            MessageBox.Show("No analysis to report. Run diagnostic first.", "Network Diagnostics");
            return;
        }

        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"NetAudit-Diag-{TxtDeviceIP.Text}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf",
                DefaultExt = ".pdf",
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (saveDialog.ShowDialog() != true) return;

            // Create PDF
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            // Font fallback for Windows compatibility
            var font = new XFont("Arial", 11);
            var fontBold = new XFont("Arial", 14);
            var fontTitle = new XFont("Arial", 16);

            double y = 40;
            const double lineHeight = 20;

            // Title
            gfx.DrawString("AĞ CİHAZ TEŞHIS RAPORU", fontTitle, XBrushes.Black, new XRect(40, y, 500, 30));
            y += 40;

            // Device Info
            gfx.DrawString($"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            y += lineHeight;
            gfx.DrawString($"Cihaz IP: {TxtDeviceIP.Text}", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            y += lineHeight;
            gfx.DrawString($"Cihaz Türü: {(CmbVendor.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Bilinmiyor"}", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            y += 30;

            // Device Status
            string deviceStatus = "SORUN VAR ❌";
            int errorCount = 0, warningCount = 0;
            foreach (var entry in _analysisResults)
            {
                if (entry.Severity.Contains("HEALTHY") || entry.Severity.Contains("STATUS"))
                    deviceStatus = "SAĞLIKLI ✓";
                if (entry.Severity.Contains("CRITICAL") || entry.Severity.Contains("ERROR"))
                    errorCount++;
                if (entry.Severity.Contains("WARNINGS"))
                    warningCount++;
            }

            gfx.DrawString("CİHAZ DURUMU", fontBold, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            y += lineHeight;
            gfx.DrawString(deviceStatus, font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            y += 30;

            // Issues
            gfx.DrawString("BULUNAN SORUNLAR", fontBold, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            y += lineHeight;

            if (errorCount > 0 || warningCount > 0)
            {
                int count = 1;
                foreach (var entry in _analysisResults)
                {
                    if (entry.Severity.Contains("HEALTHY") || entry.Severity.Contains("STATUS") || entry.Severity.Contains("SUMMARY"))
                        continue;

                    string label = entry.Severity.Replace("🔴 ", "").Replace("🟡 ", "").Replace("⚠️  ", "").Replace("ℹ️  ", "");
                    string msg = entry.Message;
                    if (msg.Contains("\n"))
                        msg = msg.Split('\n')[0];

                    gfx.DrawString($"{count}. {label}", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
                    y += lineHeight;
                    gfx.DrawString($"   {msg.Substring(0, Math.Min(90, msg.Length))}", font, XBrushes.Gray, new XRect(50, y, 480, lineHeight));
                    y += lineHeight + 5;
                    count++;
                }
            }
            else
            {
                gfx.DrawString("Sorun bulunamadı. Cihaz normal çalışıyor.", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
                y += lineHeight;
            }

            y += 15;

            // Recommendations
            gfx.DrawString("ÖNERİLER", fontBold, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            y += lineHeight;

            if (errorCount > 0)
            {
                gfx.DrawString("• Network administratörüne haber ver", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
                y += lineHeight;
                gfx.DrawString("• Cihaz konfigürasyonunu kontrol et", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
                y += lineHeight;
                gfx.DrawString("• Kablo bağlantılarını doğrula", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            }
            else if (warningCount > 0)
            {
                gfx.DrawString("• Cihazı yakından takip et", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
                y += lineHeight;
                gfx.DrawString("• Uyarıları not et", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            }
            else
            {
                gfx.DrawString("• Rutin bakım şemasına devam et", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
                y += lineHeight;
                gfx.DrawString("• Aylık kontrol yap", font, XBrushes.Black, new XRect(40, y, 500, lineHeight));
            }

            document.Save(saveDialog.FileName);
            MessageBox.Show($"Rapor kaydedildi:\n{saveDialog.FileName}", "✓ PDF Oluşturuldu");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Cannot Save Report");
        }
    }

    private async void OnRunDiagnostic(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _sshClient == null)
        {
            MessageBox.Show("Not connected. Test connection first.", "Network Diagnostics");
            return;
        }

        BtnRunDiagnostic.IsEnabled = false;
        ResultsBorder.Visibility = Visibility.Collapsed;
        ErrorBorder.Visibility = Visibility.Collapsed;

        try
        {
            if (!_isConnected || _sshClient == null)
            {
                ErrorText.Text = "❌ Connection lost. Test connection again.";
                ErrorBorder.Visibility = Visibility.Visible;
                _isConnected = false;
                return;
            }

            // Check connection is still alive - if closed, reconnect automatically
            if (!_sshClient.IsConnected)
            {
                StatusText.Text = "⏳ SSH session expired, reconnecting...";

                try
                {
                    _sshClient.Connect();
                    StatusText.Text = "✓ Reconnected. Fetching logs...";
                }
                catch (Exception reEx)
                {
                    ErrorText.Text = $"❌ Reconnection failed: {reEx.Message}";
                    ErrorBorder.Visibility = Visibility.Visible;
                    _isConnected = false;
                    return;
                }
            }

            string vendor = (CmbVendor.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Cisco IOS";
            string[] logCommands = GetLogCommands(vendor);

            StatusText.Text = "⏳ Fetching logs from device (may take up to 20 seconds)...";

            _currentLogs = null;
            int attemptCount = 0;
            var attemptLog = new List<string>();

            // Try multiple commands until one succeeds
            foreach (var logCommand in logCommands)
            {
                attemptCount++;
                StatusText.Text = $"⏳ Trying [{attemptCount}/{logCommands.Length}]: {logCommand}";

                try
                {
                    // Disable paging for vendors that use terminal pagination
                    if (vendor.Contains("Aruba") || vendor.Contains("HP") || vendor.Contains("Huawei"))
                    {
                        var pageCmd = _sshClient.CreateCommand("terminal length 0");
                        await System.Threading.Tasks.Task.Run(() => pageCmd.Execute());
                    }

                    var cmd = _sshClient.CreateCommand(logCommand);
                    var output = await System.Threading.Tasks.Task.Run(() => cmd.Execute());
                    var outputLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    attemptLog.Add($"[{attemptCount}] {logCommand} → {outputLines.Length} lines");

                    // Accept ANY output that's not an explicit error — even 1 line is better than nothing
                    if (string.IsNullOrWhiteSpace(output) ||
                        output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        output.Contains("syntax", StringComparison.OrdinalIgnoreCase) ||
                        output.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Try next command
                    }

                    _currentLogs = output;
                    StatusText.Text = $"✓ Retrieved {outputLines.Length} lines";
                    break; // Success, exit loop
                }
                catch (Exception ex)
                {
                    attemptLog.Add($"[{attemptCount}] {logCommand} → ERROR");
                    continue; // Try next command
                }
            }

            if (string.IsNullOrWhiteSpace(_currentLogs))
            {
                ErrorText.Text = $"❌ All log commands failed for {vendor}.\n\nTried {attemptCount} commands:\n{string.Join("\n", logCommands)}\n\nCheck:\n• SSH credentials\n• Device supports logs\n• Different vendor type";
                ErrorBorder.Visibility = Visibility.Visible;
                BtnGenerateReport.IsEnabled = false;
                return;
            }

            // Debug: Show raw output and line count
            var logLines = _currentLogs.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            StatusText.Text = $"✓ Retrieved {logLines.Length} log lines from device";
            StatusBorder.Visibility = Visibility.Visible;

            // Show what was actually retrieved (for debugging)
            ErrorText.Text = $"DEBUG: Raw output length={_currentLogs.Length} chars, {logLines.Length} non-empty lines\n\nFirst 500 chars:\n{_currentLogs.Substring(0, Math.Min(500, _currentLogs.Length))}";
            ErrorBorder.Visibility = Visibility.Visible;

            if (logLines.Length == 0)
            {
                return;
            }

            AnalyzeLogs(_currentLogs);
            DisplayResults(vendor);
            StatusText.Text = "✓ Logs analyzed";
            ErrorBorder.Visibility = Visibility.Collapsed;
            BtnGenerateReport.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"❌ Error: {ex.Message}";
            ErrorBorder.Visibility = Visibility.Visible;
            _isConnected = false;
            BtnGenerateReport.IsEnabled = false;
        }
        finally
        {
            BtnRunDiagnostic.IsEnabled = true;
        }
    }

    private string[] GetLogCommands(string vendor)
    {
        return vendor switch
        {
            "Cisco IOS" => new[] {
                "show log",
                "show logging",
                "show log | include debug",
                "show log | include WARNING",
                "show log | include ERROR"
            },
            "Cisco IOS-XE" => new[] {
                "show log",
                "show logging",
                "show log | include debug",
                "show log | include WARNING",
                "show log | include ERROR"
            },
            "Cisco Nexus (NX-OS)" => new[] {
                "show logging last 100",
                "show logging",
                "show log",
                "show logg | include WARNING",
                "show logging | include ERROR"
            },
            "Huawei VRP" => new[] {
                "display logbuffer",
                "display log buffer",
                "display log",
                "display log | include error",
                "display log | include warning"
            },
            "Aruba OS" => new[] {
                "show logging",
                "show log",
                "show events",
                "show syslog",
                "display logging",
                "display log",
                "show crash-log",
                "show debug-log"
            },
            "HP ProCurve" => new[] {
                "show logging",
                "show logging buffer",
                "show event-log",
                "show log",
                "show logging -w -r",
                "show logging -w -r -m"
            },
            _ => new[] {
                "show log",
                "show logging",
                "show log buffer",
                "show event-log",
                "show all-logs"
            }
        };
    }

    private void AnalyzeLogs(string logs)
    {
        _analysisResults.Clear();

        var lines = logs.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        // Count by severity — Vendor-agnostic (W/E/I prefix OR keyword match)
        int errorCount = lines.Count(l =>
            l.StartsWith("E ") ||
            Regex.IsMatch(l, @"^\s*\*?[A-Z]*ERROR|CRIT|FATAL|FAIL|DOWN|DIED", RegexOptions.IgnoreCase));

        int warningCount = lines.Count(l =>
            l.StartsWith("W ") ||
            Regex.IsMatch(l, @"^\s*%?[A-Z]*WARN|NOTICE|ALERT|FLAP|MISMATCH|FAIL", RegexOptions.IgnoreCase));

        int totalLines = lines.Length;

        _analysisResults.Add(new LogEntry
        {
            Severity = "ℹ️  LOG SCAN",
            Message = $"Scanned: {totalLines} lines | Warnings: {warningCount} | Errors: {errorCount}"
        });

        // Vendor-agnostic issue detection
        var issues = new List<string>();

        // 1. VLAN/Protocol Mismatches (LLDP, PVID, VLAN tag, trunk)
        var vlanMismatches = lines.Where(l => Regex.IsMatch(l,
            @"PVID mismatch|vlan.*mismatch|trunk.*mismatch|tag.*mismatch", RegexOptions.IgnoreCase)).Count();
        if (vlanMismatches > 0)
            issues.Add($"VLAN Configuration Mismatch: {vlanMismatches} - Verify VLAN settings on peer devices");

        // 2. Interface/Port Issues (down, flap, disable, error, CRC)
        var portIssues = lines.Where(l => Regex.IsMatch(l,
            @"port.*down|interface.*down|link.*down|flap|CRC error|collision|drop.*rate", RegexOptions.IgnoreCase)).Count();
        if (portIssues > 0)
            issues.Add($"Port/Interface Issues: {portIssues} - Check cabling, transceivers, and duplex mismatch");

        // 3. Hardware Issues (transceiver, optics, memory, fan, power)
        var hardwareIssues = lines.Where(l => Regex.IsMatch(l,
            @"transceiver|optic|sfp|gbic|memory.*low|buffer.*full|fan|power|temperature|voltage", RegexOptions.IgnoreCase)).Count();
        if (hardwareIssues > 0)
            issues.Add($"Hardware Issues: {hardwareIssues} - Verify physical connections and component health");

        // 4. Network Services Issues (NTP/SNTP, DNS, DHCP, spanning-tree)
        var serviceIssues = lines.Where(l => Regex.IsMatch(l,
            @"sntp.*fail|ntp.*fail|server.*not found|dns.*fail|dhcp.*fail|spanning.*tree.*fail|bpdu.*fail", RegexOptions.IgnoreCase)).Count();
        if (serviceIssues > 0)
            issues.Add($"Network Service Issues: {serviceIssues} - Verify NTP/DNS/DHCP servers and STP topology");

        // 5. Authentication/Security Issues
        var authIssues = lines.Where(l => Regex.IsMatch(l,
            @"invalid.*password|auth.*fail|login.*fail|access.*deny|unauthorized", RegexOptions.IgnoreCase)).Count();
        if (authIssues > 0)
            issues.Add($"Authentication Failures: {authIssues} - Monitor access logs, check credentials and policies");

        // 6. Configuration/Command Issues
        var configIssues = lines.Where(l => Regex.IsMatch(l,
            @"syntax error|unknown command|invalid.*config|config.*fail|load.*fail", RegexOptions.IgnoreCase)).Count();
        if (configIssues > 0)
            issues.Add($"Configuration Errors: {configIssues} - Verify CLI syntax and configuration validity");

        // Display results
        if (issues.Any())
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "🔴 ISSUES DETECTED",
                Message = string.Join("\n  ", issues.Select((issue, i) => $"[{i+1}] {issue}"))
            });
        }
        else
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "✓ DEVICE STATUS",
                Message = "HEALTHY - No significant issues detected"
            });
        }

        // Recommendations
        if (errorCount > 0)
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "📋 RECOMMENDATIONS",
                Message = $"Device has {errorCount} error(s). Investigate and resolve immediately."
            });
        }
        else if (warningCount > 10)
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "📋 RECOMMENDATIONS",
                Message = $"High warning count ({warningCount}). Review and address issues to maintain stability."
            });
        }
        else if (issues.Any())
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "📋 RECOMMENDATIONS",
                Message = "Address detected issues. Monitor device health closely."
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
