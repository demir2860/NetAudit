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
    private bool _isCapturing = false;

    public MainWindow()
    {
        InitializeComponent();
        PacketGrid.ItemsSource = _packets;
        PacketGrid.SelectionChanged += OnPacketSelected;
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
        }
    }

    private void OnClearPackets(object sender, RoutedEventArgs e)
    {
        _packets.Clear();
        _allPackets.Clear();
        _packetCount = 0;
        _protocolStats.Clear();
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
