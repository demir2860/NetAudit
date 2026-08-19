using System;
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

    private void OnTestConnection(object sender, RoutedEventArgs e)
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
            _sshClient.KeepAliveInterval = TimeSpan.FromSeconds(30);

            StatusText.Text = "✓ TCP OK\n⏳ SSH auth...";

            // Connect with timeout
            var connectTask = System.Threading.Tasks.Task.Run(() => _sshClient.Connect());
            if (!connectTask.Wait(TimeSpan.FromSeconds(20)))
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

    private void OnRunDiagnostic(object sender, RoutedEventArgs e)
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
            if (!_isConnected || _sshClient == null || !_sshClient.IsConnected)
            {
                ErrorText.Text = "❌ Connection lost. Test connection again.";
                ErrorBorder.Visibility = Visibility.Visible;
                _isConnected = false;
                return;
            }

            string vendor = (CmbVendor.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Cisco IOS";
            string logCommand = GetLogCommand(vendor);

            StatusText.Text = "⏳ Fetching logs...";

            using (var cmd = _sshClient.CreateCommand(logCommand))
            {
                _currentLogs = cmd.Execute();
            }

            // Check for command errors in output
            if (_currentLogs.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                _currentLogs.Contains("syntax", StringComparison.OrdinalIgnoreCase) ||
                _currentLogs.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
            {
                ErrorText.Text = $"❌ Device rejected command:\n{_currentLogs}\n\nTry different vendor type or SSH login details.";
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

    private string GetLogCommand(string vendor)
    {
        return vendor switch
        {
            "Cisco IOS" => "show log | head -50",
            "Cisco IOS-XE" => "show log | head -50",
            "Cisco Nexus (NX-OS)" => "show logging last 50",
            "Huawei VRP" => "display logbuffer | head -50",
            "Aruba OS" => "show logging -e -w -r",
            "HP ProCurve" => "show logging -w -r -m",
            _ => "show log | head -50"
        };
    }

    private void AnalyzeLogs(string logs)
    {
        _analysisResults.Clear();

        var lines = logs.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        int errorCount = lines.Count(l => Regex.IsMatch(l, @"ERROR|CRIT|%.*-[0-3]-", RegexOptions.IgnoreCase));
        int warningCount = lines.Count(l => Regex.IsMatch(l, @"WARN|%.*-4-", RegexOptions.IgnoreCase));
        int totalLines = lines.Length;

        _analysisResults.Add(new LogEntry
        {
            Severity = "ℹ️  LOG SCAN",
            Message = $"Scanned: {totalLines} lines | Critical: {errorCount} | Warning: {warningCount}"
        });

        // Show actual error lines
        var errorLines = lines
            .Where(l => Regex.IsMatch(l, @"ERROR|CRIT|%.*-[0-3]-", RegexOptions.IgnoreCase))
            .Take(5)
            .ToList();

        if (errorLines.Any())
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "🔴 CRITICAL ERRORS",
                Message = string.Join("\n  ", errorLines.Select((e, i) => $"[{i+1}] {e.Substring(0, Math.Min(95, e.Length))}"))
            });
        }
        else
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "🔴 CRITICAL ERRORS",
                Message = "None detected"
            });
        }

        // Show warning lines
        var warnLines = lines
            .Where(l => Regex.IsMatch(l, @"WARN|%.*-4-", RegexOptions.IgnoreCase))
            .Take(3)
            .ToList();

        if (warnLines.Any())
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "🟡 WARNINGS",
                Message = string.Join("\n  ", warnLines.Select((w, i) => $"[{i+1}] {w.Substring(0, Math.Min(95, w.Length))}"))
            });
        }
        else
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "🟡 WARNINGS",
                Message = "None detected"
            });
        }

        // Critical patterns
        var criticalPatterns = new[] {
            (@"interface.*down|port.*down|link down", "⚠️  INTERFACE DOWN"),
            (@"memory|buffer|overfl", "⚠️  MEMORY ISSUE"),
            (@"cpu.*high", "⚠️  CPU HIGH"),
            (@"restart|reload", "⚠️  DEVICE RESTART"),
        };

        bool hasCritical = false;
        foreach (var (pattern, label) in criticalPatterns)
        {
            if (lines.Any(l => Regex.IsMatch(l, pattern, RegexOptions.IgnoreCase)))
            {
                _analysisResults.Add(new LogEntry { Severity = label, Message = "Detected in logs" });
                hasCritical = true;
            }
        }

        // Status
        if (!hasCritical && errorCount == 0 && warningCount == 0)
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "✓ DEVICE STATUS",
                Message = "HEALTHY - No errors, warnings or critical patterns found"
            });
        }
        else if (errorCount > 0)
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "⚡ RECOMMEND",
                Message = "Review critical errors above immediately. Check logs for root cause and take corrective action."
            });
        }
        else
        {
            _analysisResults.Add(new LogEntry
            {
                Severity = "⚡ RECOMMEND",
                Message = "Monitor warnings and critical patterns. Review device health metrics."
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
