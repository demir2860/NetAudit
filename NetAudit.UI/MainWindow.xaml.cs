using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    private ObservableCollection<ProtocolStat> _protocolStatsObservable = new();
    private bool _isCapturing = false;
    
    private Dictionary<string, int> _macSourceMap = new();
    private int _broadcastCount = 0;
    private int _arpCount = 0;
    private DateTime _lastSecond = DateTime.Now;
    private int _packetsThisSecond = 0;

    public MainWindow()
    {
        InitializeComponent();
        PacketGrid.ItemsSource = _packets;
        PacketGrid.SelectionChanged += OnPacketSelected;
        ProtocolStats.ItemsSource = _protocolStatsObservable;
        LoadDevices();
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
                _packetsThisSecond++;
                
                var ipPacket = packet.Extract<IPPacket>();
                var tcpPacket = packet.Extract<TcpPacket>();
                var udpPacket = packet.Extract<UdpPacket>();
                var arpPacket = packet.Extract<ARPPacket>();
                var icmpV4Packet = packet.Extract<ICMPv4Packet>();
                var ethernetPacket = packet as EthernetPacket;

                string protocol = "Other";
                string info = "";
                bool isBroadcast = false;
                bool isMulticast = false;

                if (ethernetPacket != null)
                {
                    var destMac = ethernetPacket.DestinationHwAddress.ToString();
                    isBroadcast = destMac.ToUpper() == "FF:FF:FF:FF:FF:FF";
                    isMulticast = ethernetPacket.DestinationHwAddress.Bytes[0] % 2 == 1;
                }

                if (tcpPacket != null)
                {
                    protocol = "TCP";
                    info = $"TCP {tcpPacket.SourcePort} → {tcpPacket.DestinationPort}";
                }
                else if (udpPacket != null)
                {
                    protocol = "UDP";
                    info = $"UDP {udpPacket.SourcePort} → {udpPacket.DestinationPort}";
                    
                    if (udpPacket.DestinationPort == 53 || udpPacket.SourcePort == 53)
                        protocol = "DNS";
                }
                else if (icmpV4Packet != null)
                {
                    protocol = "ICMP";
                    info = $"ICMP Type {icmpV4Packet.TypeCode}";
                }
                else if (arpPacket != null)
                {
                    protocol = "ARP";
                    info = $"ARP {arpPacket.SenderProtocolAddress} - {arpPacket.TargetProtocolAddress}";
                    _arpCount++;
                }
                else if (ipPacket != null)
                {
                    protocol = ipPacket.Protocol.ToString();
                    info = protocol;
                }

                if (isBroadcast || isMulticast)
                    _broadcastCount++;

                lock (_protocolStats)
                {
                    if (!_protocolStats.ContainsKey(protocol))
                        _protocolStats[protocol] = 0;
                    _protocolStats[protocol]++;
                }

                if (ipPacket != null)
                {
                    string macKey = ipPacket.SourceAddress.ToString();
                    if (ethernetPacket != null)
                        macKey = ethernetPacket.SourceHwAddress.ToString();
                    
                    if (!_macSourceMap.ContainsKey(macKey))
                        _macSourceMap[macKey] = 0;
                    _macSourceMap[macKey]++;
                }

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
                
                UpdateProtocolStats();
                CheckAnomalies();
            });
        };

        _currentDevice.StartCapture();
        Log.Information("Capture started on {Device}", _currentDevice.Name);
    }

    private void CheckAnomalies()
    {
        var now = DateTime.Now;
        if ((now - _lastSecond).TotalSeconds >= 1)
        {
            var pps = _packetsThisSecond;
            var issues = new List<string>();

            if (pps > 1000)
                issues.Add($"⚠ HIGH RATE: {pps} pps (loop risk)");
            
            if (_broadcastCount > (pps * 0.15))
                issues.Add($"⚠ BROADCAST STORM: {_broadcastCount} broadcast");
            
            if (_arpCount > (pps * 0.10))
                issues.Add($"⚠ ARP FLOOD: {_arpCount} ARP packets");

            var macFlaps = _macSourceMap.Where(x => x.Value > 5).ToList();
            if (macFlaps.Count > 0)
                issues.Add($"⚠ MAC FLAPPING: {macFlaps.Count} duplicate sources");

            if (issues.Count > 0)
            {
                DetailView.Text = string.Join("\n", issues) + "\n\n=== NETWORK DIAGNOSTICS ===\n" +
                    $"Packets/sec: {pps}\nBroadcast: {_broadcastCount}\nARP: {_arpCount}\nUnique MACs: {_macSourceMap.Count}";
            }

            _packetsThisSecond = 0;
            _broadcastCount = 0;
            _arpCount = 0;
            _lastSecond = now;
        }
    }

    private void UpdateProtocolStats()
    {
        _protocolStatsObservable.Clear();
        var sorted = _protocolStats.OrderByDescending(x => x.Value);
        foreach (var stat in sorted)
        {
            _protocolStatsObservable.Add(new ProtocolStat { Protocol = stat.Key, Count = stat.Value });
        }
    }

    private void OnStopCapture(object sender, RoutedEventArgs e)
    {
        if (_currentDevice != null)
        {
            _isCapturing = false;
            _currentDevice.StopCapture();
            _currentDevice.Close();
            Log.Information("Capture stopped. Total packets: {Count}", _packetCount);
        }
    }

    private void OnClearPackets(object sender, RoutedEventArgs e)
    {
        _packets.Clear();
        _allPackets.Clear();
        _packetCount = 0;
        _protocolStats.Clear();
        _protocolStatsObservable.Clear();
        _macSourceMap.Clear();
        _broadcastCount = 0;
        _arpCount = 0;
        PacketCount.Text = "Packets: 0";
        DetailView.Text = "";
    }

    private void OnApplyFilter(object sender, RoutedEventArgs e)
    {
        string filter = FilterBox.Text.ToLower();
        if (string.IsNullOrWhiteSpace(filter))
        {
            _packets.Clear();
            foreach (var p in _allPackets) _packets.Add(p);
            return;
        }

        var filtered = _allPackets.Where(p =>
            p.Source.Contains(filter) ||
            p.Destination.Contains(filter) ||
            p.Protocol.ToLower().Contains(filter) ||
            p.Info.ToLower().Contains(filter)
        ).ToList();

        _packets.Clear();
        foreach (var p in filtered) _packets.Add(p);

        Log.Information("Filter applied: {Filter} - {Count} packets matched", filter, filtered.Count);
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

        var icmpPacket = packet.RawPacket.Extract<ICMPv4Packet>();
        if (icmpPacket != null)
        {
            sb.AppendLine($"ICMP Type: {icmpPacket.TypeCode}");
            sb.AppendLine($"ICMP Code: {icmpPacket.Code}");
            sb.AppendLine();
        }

        var arpPacket = packet.RawPacket.Extract<ARPPacket>();
        if (arpPacket != null)
        {
            sb.AppendLine($"ARP Operation: {arpPacket.Operation}");
            sb.AppendLine($"Sender IP: {arpPacket.SenderProtocolAddress}");
            sb.AppendLine($"Target IP: {arpPacket.TargetProtocolAddress}");
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

public class ProtocolStat
{
    public string Protocol { get; set; } = "";
    public int Count { get; set; }
}
