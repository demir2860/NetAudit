using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;
using Serilog;

namespace NetAudit.UI;

public partial class MainWindow : Window
{
    private ICaptureDevice? _currentDevice;
    private ObservableCollection<PacketRow> _packets = new();
    private List<PacketRow> _allPackets = new();
    private int _packetCount = 0;
    private Dictionary<string, int> _protocolStats = new();
    private ObservableCollection<ProtocolStatItem> _protocolStatsObservable = new();
    private bool _isCapturing = false;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize packet grid
        PacketGrid.ItemsSource = _packets;
        PacketGrid.SelectionChanged += OnPacketSelected;
        ProtocolStats.ItemsSource = _protocolStatsObservable;
        LoadDevices();

        // Show capture tab by default
        OnTabCapture(null, null);
    }

    private void OnTabCapture(object sender, RoutedEventArgs e)
    {
        LoadCaptureTab();
        UpdateTabButtons("Capture");
    }

    private void OnTabNetworkDiag(object sender, RoutedEventArgs e)
    {
        CaptureTabGrid.Visibility = Visibility.Collapsed;
        var page = new NetworkDiagPage();
        ContentFrame.Content = page;
        UpdateTabButtons("NetDiag");
    }


    private void UpdateTabButtons(string activeTab)
    {
        BtnCapture.Background = activeTab == "Capture" ? new SolidColorBrush(Color.FromArgb(255, 21, 101, 192)) : new SolidColorBrush(Color.FromArgb(255, 224, 224, 224));
        BtnCapture.Foreground = activeTab == "Capture" ? Brushes.White : Brushes.Black;

        BtnNetDiag.Background = activeTab == "NetDiag" ? new SolidColorBrush(Color.FromArgb(255, 21, 101, 192)) : new SolidColorBrush(Color.FromArgb(255, 224, 224, 224));
        BtnNetDiag.Foreground = activeTab == "NetDiag" ? Brushes.White : Brushes.Black;
    }

    private void LoadCaptureTab()
    {
        // Show capture tab, hide frame content
        CaptureTabGrid.Visibility = Visibility.Visible;
        ContentFrame.Content = null;
    }

    private void LoadDevices()
    {
        DeviceSelector.Items.Clear();
        foreach (var device in CaptureDeviceList.Instance)
        {
            DeviceSelector.Items.Add($"{device.Name} - {device.Description}");
        }
        if (DeviceSelector.Items.Count > 0) DeviceSelector.SelectedIndex = 0;
    }

    private void OnStartCapture(object sender, RoutedEventArgs e)
    {
        if (DeviceSelector.SelectedIndex < 0) return;

        _isCapturing = true;
        _currentDevice = CaptureDeviceList.Instance[DeviceSelector.SelectedIndex];
        _currentDevice.Open();

        _currentDevice.OnPacketArrival += (s, ea) =>
        {
            if (!_isCapturing) return;

            var rawCapture = ea.GetPacket();
            var packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);

            Dispatcher.Invoke(() =>
            {
                _packetCount++;
                var ipPacket = packet.Extract<IPPacket>();
                var tcpPacket = packet.Extract<TcpPacket>();
                var udpPacket = packet.Extract<UdpPacket>();

                string protocol = "Other";
                string info = "";

                if (tcpPacket != null)
                {
                    protocol = "TCP";
                    info = $"TCP {tcpPacket.SourcePort} → {tcpPacket.DestinationPort}";
                }
                else if (udpPacket != null)
                {
                    protocol = "UDP";
                    info = $"UDP {udpPacket.SourcePort} → {udpPacket.DestinationPort}";
                }
                else if (ipPacket != null)
                {
                    protocol = ipPacket.Protocol.ToString();
                    info = protocol;
                }

                lock (_protocolStats)
                {
                    if (!_protocolStats.ContainsKey(protocol))
                        _protocolStats[protocol] = 0;
                    _protocolStats[protocol]++;
                }

                UpdateProtocolStats();

                var packetRow = new PacketRow
                {
                    No = _packetCount,
                    Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                    Source = ipPacket?.SourceAddress.ToString() ?? "N/A",
                    Destination = ipPacket?.DestinationAddress.ToString() ?? "N/A",
                    Protocol = protocol,
                    Length = rawCapture.Data.Length.ToString(),
                    Info = info,
                    RawPacket = packet,
                    RawCapture = rawCapture
                };

                _allPackets.Add(packetRow);
                _packets.Add(packetRow);
                PacketCount.Text = $"Packets: {_packetCount}";
            });
        };

        _currentDevice.StartCapture();
        Log.Information("Capture started on {Device}", _currentDevice.Name);
    }

    private void OnStopCapture(object sender, RoutedEventArgs e)
    {
        if (_currentDevice != null)
        {
            _isCapturing = false;
            _currentDevice.StopCapture();
            _currentDevice.Close();
            Log.Information("Capture stopped. Total packets: {Count}", _packetCount);

            GenerateAnalysisReport();
        }
    }

    private void GenerateAnalysisReport()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("\n═══════════════════════════════════════════════════════════════════");
        report.AppendLine("                   NETWORK ANALYSIS REPORT");
        report.AppendLine("═══════════════════════════════════════════════════════════════════\n");

        // 1. Summary
        report.AppendLine($"📊 ÖZET");
        report.AppendLine($"├─ Toplam Paket: {_packetCount}");
        report.AppendLine($"├─ Tarama Süresi: {(_allPackets.Count > 0 ? "~" + (_packetCount / 100) + " saniye" : "0")}");
        report.AppendLine($"├─ Protokol Çeşidi: {_protocolStats.Count}");
        report.AppendLine($"├─ Benzersiz IP'ler: {_allPackets.Select(p => p.Source).Distinct().Count()}");
        report.AppendLine();

        // 2. Protocol Distribution
        report.AppendLine($"🔄 PROTOKOL DAĞILIMI");
        var sortedProtocols = _protocolStats.OrderByDescending(kv => kv.Value).ToList();
        int totalCount = _protocolStats.Values.Sum();
        foreach (var proto in sortedProtocols)
        {
            float percent = (float)proto.Value / totalCount * 100;
            string bar = new string('█', (int)(percent / 5));
            report.AppendLine($"   {proto.Key,-8} │ {bar,-20} {percent:F1}% ({proto.Value} paket)");
        }
        report.AppendLine();

        // 3. Port Analysis
        report.AppendLine($"🔌 EN ÇIKIŞ PORTLAR (Top 10)");
        var portStats = new Dictionary<int, int>();
        foreach (var packet in _allPackets)
        {
            if (packet.Info.Contains("→"))
            {
                var parts = packet.Info.Split('→');
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int port))
                {
                    if (!portStats.ContainsKey(port)) portStats[port] = 0;
                    portStats[port]++;
                }
            }
        }
        var topPorts = portStats.OrderByDescending(kv => kv.Value).Take(10).ToList();
        int portIndex = 1;
        foreach (var port in topPorts)
        {
            string service = GetPortService(port.Key);
            report.AppendLine($"   {portIndex}. Port {port.Key,-5} ({service,-10}): {port.Value,-3} paket");
            portIndex++;
        }
        report.AppendLine();

        // 4. Advanced Risk Analysis
        report.AppendLine($"⚠️  ANOMALI ANALİZİ (GELİŞMİŞ)");
        bool hasRisk = false;

        // SSH Brute Force
        var sshPackets = _allPackets.Where(p => p.Info.Contains("22")).Count();
        if (sshPackets > 50)
        {
            report.AppendLine($"   🚨 SSH Brute Force: {sshPackets} paket (PORT 22) - UYARI!");
            hasRisk = true;
        }

        // DNS Exfiltration
        var dnsPackets = _allPackets.Where(p => p.Info.Contains("53") || p.Info.Contains("5353")).Count();
        if (dnsPackets > _packetCount * 0.2)
        {
            report.AppendLine($"   ⚠️  DNS Anomali: {dnsPackets} paket ({(float)dnsPackets / _packetCount * 100:F1}%) - UYARI!");
            hasRisk = true;
        }

        // UDP Flood
        var udpPackets = _protocolStats.ContainsKey("UDP") ? _protocolStats["UDP"] : 0;
        if (udpPackets > _packetCount * 0.5)
        {
            report.AppendLine($"   🔴 UDP Flood Şüphesi: {udpPackets} paket ({(float)udpPackets / _packetCount * 100:F1}%) - YÜKSEK RİSK!");
            hasRisk = true;
        }
        else if (udpPackets > _packetCount * 0.3)
        {
            report.AppendLine($"   🟡 UDP Yoğunluğu: {udpPackets} paket ({(float)udpPackets / _packetCount * 100:F1}%) - DİKKAT!");
            hasRisk = true;
        }

        // Rapid Connections
        if (portStats.Values.Any(v => v > 100))
        {
            report.AppendLine($"   ⚠️  Hızlı Bağlantı: Tek porta {portStats.Values.Max()} paket - MONITORING GEREKLI");
        }

        // ICMP (Ping Sweep Check)
        var icmpPackets = _allPackets.Where(p => p.Protocol.Contains("ICMP")).Count();
        if (icmpPackets > 20)
        {
            report.AppendLine($"   🟡 ICMP Yoğunluğu: {icmpPackets} paket - Network Keşif İhtimali");
        }

        if (!hasRisk)
        {
            report.AppendLine($"   ✅ TEMIZ - Anormal aktivite tespit edilmedi");
        }
        report.AppendLine();

        // 5. Risk Level & Recommendations
        report.AppendLine($"🎯 RİSK DEĞERLENDİRMESİ");
        string riskLevel;
        if (hasRisk && (sshPackets > 50 || udpPackets > _packetCount * 0.5))
            riskLevel = "🔴 YÜKSEK RİSK";
        else if (hasRisk)
            riskLevel = "🟡 ORTA RİSK";
        else
            riskLevel = "🟢 DÜŞÜK RİSK";
        report.AppendLine($"   Seviye: {riskLevel}");
        report.AppendLine();

        report.AppendLine($"💡 EYLEM ÖNERİLERİ");
        if (sshPackets > 50)
            report.AppendLine($"   ✓ SSH brute force karşı önlem: fail2ban, IP whitelist, port değiştir");
        if (udpPackets > _packetCount * 0.3)
            report.AppendLine($"   ✓ UDP trafiği kontrol et: DDoS, DNS exfil ihtimali");
        if (icmpPackets > 20)
            report.AppendLine($"   ✓ ICMP kontrol et: Network keşif taraması olabilir");
        report.AppendLine($"   ✓ Bilinmeyen portlar için log analizi yap");
        report.AppendLine($"   ✓ Regular monitoring ve alerting sistemi kur");
        report.AppendLine();

        report.AppendLine("═══════════════════════════════════════════════════════════════════");
        report.AppendLine($"📅 Rapor Tarihi: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine("═══════════════════════════════════════════════════════════════════\n");

        DetailView.Text = report.ToString();

        Log.Information("Analysis report generated. Total packets: {Count}, Risk Level: {Risk}", _packetCount, riskLevel);
    }

    private string GetPortService(int port)
    {
        return port switch
        {
            20 => "FTP-DATA",
            21 => "FTP",
            22 => "SSH",
            23 => "Telnet",
            25 => "SMTP",
            53 => "DNS",
            80 => "HTTP",
            110 => "POP3",
            143 => "IMAP",
            443 => "HTTPS",
            3306 => "MySQL",
            5432 => "PostgreSQL",
            5353 => "mDNS",
            8080 => "HTTP-Alt",
            _ => "Unknown"
        };
    }

    private void OnClearPackets(object sender, RoutedEventArgs e)
    {
        _packets.Clear();
        _allPackets.Clear();
        _packetCount = 0;
        _protocolStats.Clear();
        _protocolStatsObservable.Clear();
        PacketCount.Text = "Packets: 0";
        DetailView.Text = "";
    }

    private void UpdateProtocolStats()
    {
        _protocolStatsObservable.Clear();
        var sorted = _protocolStats.OrderByDescending(kv => kv.Value).ToList();
        foreach (var kv in sorted)
        {
            _protocolStatsObservable.Add(new ProtocolStatItem
            {
                Protocol = kv.Key,
                Count = kv.Value,
                CountText = $"{kv.Key}: {kv.Value}"
            });
        }
    }

    private void OnProtocolDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProtocolStats.SelectedItem is not ProtocolStatItem item) return;
        FilterBox.Text = item.Protocol;
        OnApplyFilter(sender, null);
    }

    private void OnApplyFilter(object sender, RoutedEventArgs e)
    {
        string filterText = FilterBox.Text.ToLower().Trim();
        if (filterText.StartsWith("filter:")) filterText = filterText.Substring(7).Trim();

        if (string.IsNullOrWhiteSpace(filterText))
        {
            _packets.Clear();
            foreach (var p in _allPackets) _packets.Add(p);
            return;
        }

        var filters = filterText.Split('|').Select(f => f.Trim()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();

        var filtered = _allPackets.Where(p =>
            filters.Any(f =>
                p.Source.ToLower().Contains(f) ||
                p.Destination.ToLower().Contains(f) ||
                p.Protocol.ToLower().Contains(f) ||
                p.Info.ToLower().Contains(f)
            )
        ).ToList();

        _packets.Clear();
        foreach (var p in filtered) _packets.Add(p);

        Log.Information("Filter applied: {Filter} - {Count} packets matched", filterText, filtered.Count);
    }

    private void OnPacketSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PacketGrid.SelectedItem is not PacketRow packet || packet.RawPacket == null) return;

        DetailView.Text = FormatPacketDetails(packet);
    }

    private string FormatPacketDetails(PacketRow packet)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Packet #{packet.No} ===");
        sb.AppendLine($"Timestamp: {packet.Time}");
        sb.AppendLine($"Length: {packet.Length} bytes");
        sb.AppendLine();

        sb.AppendLine("=== Network Layer ===");
        var ipPacket = packet.RawPacket.Extract<IPPacket>();
        if (ipPacket != null)
        {
            sb.AppendLine($"Source IP: {ipPacket.SourceAddress}");
            sb.AppendLine($"Destination IP: {ipPacket.DestinationAddress}");
            sb.AppendLine($"Protocol: {ipPacket.Protocol}");
            sb.AppendLine($"TTL: {ipPacket.TimeToLive}");
            sb.AppendLine();
        }

        sb.AppendLine("=== Transport Layer ===");
        var tcpPacket = packet.RawPacket.Extract<TcpPacket>();
        if (tcpPacket != null)
        {
            sb.AppendLine($"TCP Source Port: {tcpPacket.SourcePort}");
            sb.AppendLine($"TCP Dest Port: {tcpPacket.DestinationPort}");
            sb.AppendLine($"Sequence: {tcpPacket.SequenceNumber}");
            sb.AppendLine($"Acknowledgment: {tcpPacket.AcknowledgmentNumber}");
            sb.AppendLine();
        }

        var udpPacket = packet.RawPacket.Extract<UdpPacket>();
        if (udpPacket != null)
        {
            sb.AppendLine($"UDP Source Port: {udpPacket.SourcePort}");
            sb.AppendLine($"UDP Dest Port: {udpPacket.DestinationPort}");
            sb.AppendLine($"Length: {udpPacket.Length}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("=== Raw Hex (first 256 bytes) ===");
        if (packet.RawCapture?.Data != null)
        {
            var data = packet.RawCapture.Data.Take(256).ToArray();
            for (int i = 0; i < data.Length; i += 16)
            {
                var chunk = data.Skip(i).Take(16);
                sb.Append($"{i:X4}: ");
                sb.Append(string.Join(" ", chunk.Select(b => $"{b:X2}")));
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private void OnGenerateReportFile(object sender, RoutedEventArgs e)
    {
        if (_allPackets.Count == 0)
        {
            System.Windows.MessageBox.Show("Tarama yapılmadı. Önce paket yakala.", "Hata", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var reportContent = GenerateReportContent();

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"NetAudit_Report_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = ".txt",
            Filter = "Text Report (*.txt)|*.txt|All Files (*.*)|*.*",
            InitialDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop)
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                System.IO.File.WriteAllText(saveDialog.FileName, reportContent);
                System.Windows.MessageBox.Show($"Rapor başarıyla kaydedildi:\n{saveDialog.FileName}",
                    "Başarılı", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                Log.Information("Report generated and saved to {Path}", saveDialog.FileName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hata: {ex.Message}", "Hata", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Log.Error(ex, "Error saving report");
            }
        }
    }

    private string GenerateReportContent()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("\n═══════════════════════════════════════════════════════════════════");
        report.AppendLine("                   NETWORK ANALYSIS REPORT");
        report.AppendLine("═══════════════════════════════════════════════════════════════════\n");

        // 1. Summary
        report.AppendLine($"📊 ÖZET");
        report.AppendLine($"├─ Toplam Paket: {_packetCount}");
        report.AppendLine($"├─ Tarama Süresi: {(_allPackets.Count > 0 ? "~" + (_packetCount / 100) + " saniye" : "0")}");
        report.AppendLine($"├─ Protokol Çeşidi: {_protocolStats.Count}");
        report.AppendLine($"├─ Benzersiz IP'ler: {_allPackets.Select(p => p.Source).Distinct().Count()}");
        report.AppendLine($"├─ Rapor Tarihi: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine();

        // 2. Protocol Distribution
        report.AppendLine($"🔄 PROTOKOL DAĞILIMI");
        var sortedProtocols = _protocolStats.OrderByDescending(kv => kv.Value).ToList();
        int totalCount = _protocolStats.Values.Sum();
        foreach (var proto in sortedProtocols)
        {
            float percent = (float)proto.Value / totalCount * 100;
            report.AppendLine($"   {proto.Key,-8} │ {percent:F1}% ({proto.Value} paket)");
        }
        report.AppendLine();

        // 3. Port Analysis
        report.AppendLine($"🔌 EN ÇIKIŞ PORTLAR (Top 10)");
        var portStats = new Dictionary<int, int>();
        foreach (var packet in _allPackets)
        {
            if (packet.Info.Contains("→"))
            {
                var parts = packet.Info.Split('→');
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int port))
                {
                    if (!portStats.ContainsKey(port)) portStats[port] = 0;
                    portStats[port]++;
                }
            }
        }
        var topPorts = portStats.OrderByDescending(kv => kv.Value).Take(10).ToList();
        int portIndex = 1;
        foreach (var port in topPorts)
        {
            string service = GetPortService(port.Key);
            report.AppendLine($"   {portIndex}. Port {port.Key,-5} ({service,-10}): {port.Value,-3} paket");
            portIndex++;
        }
        report.AppendLine();

        // 4. Risk Analysis
        report.AppendLine($"⚠️  ANOMALI ANALİZİ");
        bool hasRisk = false;

        var sshPackets = _allPackets.Where(p => p.Info.Contains("22")).Count();
        if (sshPackets > 50)
        {
            report.AppendLine($"   🚨 SSH Brute Force: {sshPackets} paket (PORT 22)");
            hasRisk = true;
        }

        var dnsPackets = _allPackets.Where(p => p.Info.Contains("53") || p.Info.Contains("5353")).Count();
        if (dnsPackets > _packetCount * 0.2)
        {
            report.AppendLine($"   ⚠️  DNS Anomali: {dnsPackets} paket ({(float)dnsPackets / _packetCount * 100:F1}%)");
            hasRisk = true;
        }

        var udpPackets = _protocolStats.ContainsKey("UDP") ? _protocolStats["UDP"] : 0;
        if (udpPackets > _packetCount * 0.5)
        {
            report.AppendLine($"   🔴 UDP Flood: {udpPackets} paket ({(float)udpPackets / _packetCount * 100:F1}%)");
            hasRisk = true;
        }

        if (!hasRisk)
        {
            report.AppendLine($"   ✅ Anormal aktivite tespit edilmedi");
        }
        report.AppendLine();

        // 5. Risk Level
        report.AppendLine($"🎯 RİSK SEVİYESİ");
        string riskLevel = hasRisk ? "🔴 YÜKSEK" : udpPackets > 100 ? "🟡 ORTA" : "🟢 DÜŞÜK";
        report.AppendLine($"   {riskLevel}");
        report.AppendLine();

        report.AppendLine("═══════════════════════════════════════════════════════════════════");
        report.AppendLine("NetAudit Pro - Wireshark Edition");
        report.AppendLine("═══════════════════════════════════════════════════════════════════\n");

        return report.ToString();
    }
}

public class PacketRow
{
    public int No { get; set; }
    public string Time { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string Length { get; set; } = "";
    public string Info { get; set; } = "";
    public Packet? RawPacket { get; set; }
    public RawCapture? RawCapture { get; set; }
}

public class ProtocolStatItem
{
    public string Protocol { get; set; } = "";
    public int Count { get; set; }
    public string CountText { get; set; } = "";
}
