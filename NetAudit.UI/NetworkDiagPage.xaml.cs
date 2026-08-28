using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Renci.SshNet;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

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

            // Try multiple commands until one succeeds
            foreach (var logCommand in logCommands)
            {
                attemptCount++;
                StatusText.Text = $"⏳ Trying command {attemptCount}/{logCommands.Length}...";

                try
                {
                    var cmd = _sshClient.CreateCommand(logCommand);
                    var output = await System.Threading.Tasks.Task.Run(() => cmd.Execute());

                    // Check if output is an error
                    if (string.IsNullOrWhiteSpace(output) ||
                        output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        output.Contains("syntax", StringComparison.OrdinalIgnoreCase) ||
                        output.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Try next command
                    }

                    _currentLogs = output;
                    break; // Success, exit loop
                }
                catch
                {
                    continue; // Try next command
                }
            }

            if (string.IsNullOrWhiteSpace(_currentLogs))
            {
                ErrorText.Text = $"❌ All log commands failed for {vendor}.\n\nTried {attemptCount} commands. Check:\n• SSH credentials\n• Device supports logs\n• Different vendor type";
                ErrorBorder.Visibility = Visibility.Visible;
                BtnGenerateReport.IsEnabled = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentLogs))
            {
                ErrorText.Text = "⚠️ No logs returned. Device may have no logs or logging disabled.";
                ErrorBorder.Visibility = Visibility.Visible;
                BtnGenerateReport.IsEnabled = false;
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
            "Cisco IOS" => new[] { "show log | head -50", "show logging | head -50", "show log" },
            "Cisco IOS-XE" => new[] { "show log | head -50", "show logging | head -50", "show log" },
            "Cisco Nexus (NX-OS)" => new[] { "show logging last 50", "show log | head -50", "show logging" },
            "Huawei VRP" => new[] { "display logbuffer | head -50", "display log | head -50", "display logbuffer" },
            "Aruba OS" => new[] { "show logging -e -w -r", "show logging -w -r", "show logging" },
            "HP ProCurve" => new[] { "show logging -w -r -m", "show logging -w -r", "show logging" },
            _ => new[] { "show log | head -50", "show logging", "show log" }
        };
    }

    private void AnalyzeLogs(string logs)
    {
        _analysisResults.Clear();

        var lines = logs.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        // Count logs by severity (W = warning, I = info, E = error)
        int errorCount = lines.Count(l => l.StartsWith("E ") || Regex.IsMatch(l, @"ERROR|CRIT", RegexOptions.IgnoreCase));
        int warningCount = lines.Count(l => l.StartsWith("W "));
        int totalLines = lines.Length;

        _analysisResults.Add(new LogEntry
        {
            Severity = "ℹ️  LOG SCAN",
            Message = $"Scanned: {totalLines} lines | Warnings: {warningCount} | Errors: {errorCount}"
        });

        // Detect Aruba-specific issues
        var issues = new List<string>();

        // LLDP PVID Mismatch
        var pvidMismatch = lines.Where(l => Regex.IsMatch(l, @"PVID mismatch", RegexOptions.IgnoreCase)).Count();
        if (pvidMismatch > 0)
            issues.Add($"LLDP PVID Mismatch: {pvidMismatch} instances - Check VLAN configuration on connected devices");

        // Port B18 Collision/Drop
        var b18Issues = lines.Where(l => Regex.IsMatch(l, @"port B18.*collision|drop rate", RegexOptions.IgnoreCase)).Count();
        if (b18Issues > 0)
            issues.Add($"Port B18 High Collision/Drop Rate: {b18Issues} alerts - Check cabling, transceiver, or link partner");

        // Unsupported Transceiver
        var transceiverIssues = lines.Where(l => Regex.IsMatch(l, @"Unsupported Transceiver", RegexOptions.IgnoreCase)).Count();
        if (transceiverIssues > 0)
            issues.Add("Unsupported Transceiver Detected: Replace with compatible optics (verify part numbers)");

        // SNTP Failures
        var sntpFails = lines.Where(l => Regex.IsMatch(l, @"SNTP.*Server not found|Unable to reach", RegexOptions.IgnoreCase)).Count();
        if (sntpFails > 0)
            issues.Add($"SNTP Server Unreachable: {sntpFails} failures - Verify NTP server IP and network connectivity");

        // Auth Failures
        var authFails = lines.Where(l => Regex.IsMatch(l, @"Invalid user name/password", RegexOptions.IgnoreCase)).Count();
        if (authFails > 0)
            issues.Add($"Authentication Failures: {authFails} failed login attempts - Monitor for brute-force attacks");

        // Port Flapping
        var flappingPorts = lines.Where(l => Regex.IsMatch(l, @"port.*is now (on|off)-line", RegexOptions.IgnoreCase))
            .GroupBy(l => Regex.Match(l, @"port ([A-D]\d+)").Groups[1].Value)
            .Where(g => g.Count() > 3)
            .ToList();
        if (flappingPorts.Any())
            issues.Add($"Port Flapping Detected: {flappingPorts.Count} ports oscillating - Check cable connections and port status");

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
                Message = "HEALTHY - No critical issues detected"
            });
        }

        // Recommendations
        if (warningCount > 5)
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "📋 RECOMMENDATIONS",
                Message = $"High warning count ({warningCount}). Review and address reported issues to maintain device stability."
            });
        }
        else if (issues.Any())
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "📋 RECOMMENDATIONS",
                Message = "Address detected issues. Verify configurations and check device connectivity."
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
