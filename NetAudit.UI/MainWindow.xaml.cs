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
    private Dictionary<string, int> _protocolCounts = new();
    private ObservableCollection<ProtocolStatItem> _protocolStatsObservable = new();
    private bool _isCapturing = false;

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
            var type = GetDeviceType(device.Description);
            DeviceSelector.Items.Add($"[{type}] {device.Description}");
        }
        if (DeviceSelector.Items.Count > 0) DeviceSelector.SelectedIndex = 0;
    }

    private string GetDeviceType(string description)
    {
        if (string.IsNullOrEmpty(description)) return "Unknown";
        if (description.Contains("Wireless") || description.Contains("WiFi") || description.Contains("802.11"))
            return "WiFi";
        if (description.Contains("VPN") || description.Contains("OpenVPN") || description.Contains("Wireguard") || description.Contains("Tap"))
            return "VPN";
        if (description.Contains("Loopback") || description.Contains("Npcap Loopback"))
            return "Loop";
        return "Network";
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

                if (tcpPacket != null) { protocol = "TCP"; info = $"{tcpPacket.SourcePort} → {tcpPacket.DestinationPort}"; }
                else if (udpPacket != null) 
                { 
                    protocol = "UDP"; 
                    info = $"{udpPacket.SourcePort} → {udpPacket.DestinationPort}";
                    if (udpPacket.DestinationPort == 53 || udpPacket.SourcePort == 53) protocol = "DNS";
                }
                else if (ipPacket != null) { protocol = ipPacket.Protocol.ToString(); }

                if (!_protocolCounts.ContainsKey(protocol)) _protocolCounts[protocol] = 0;
                _protocolCounts[protocol]++;

                _allPackets.Add(new PacketRow
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
                });

                _packets.Add(_allPackets.Last());
                PacketCount.Text = $"Packets: {_packetCount}";
                UpdateProtocolStats();
            });
        };

        _currentDevice.StartCapture();
    }

    private void UpdateProtocolStats()
    {
        _protocolStatsObservable.Clear();
        foreach (var stat in _protocolCounts.OrderByDescending(x => x.Value))
        {
            _protocolStatsObservable.Add(new ProtocolStatItem 
            { 
                Protocol = stat.Key, 
                Count = stat.Value,
                CountText = $"{stat.Value} packets"
            });
        }
    }

    private void OnStopCapture(object sender, RoutedEventArgs e)
    {
        if (_currentDevice != null)
        {
            _isCapturing = false;
            _currentDevice.StopCapture();
            _currentDevice.Close();
        }
    }

    private void OnClearPackets(object sender, RoutedEventArgs e)
    {
        _packets.Clear();
        _allPackets.Clear();
        _packetCount = 0;
        _protocolCounts.Clear();
        _protocolStatsObservable.Clear();
        PacketCount.Text = "Packets: 0";
        DetailView.Text = "";
        FilterBox.Clear();
    }

    private void OnApplyFilter(object sender, RoutedEventArgs e)
    {
        string filterText = FilterBox.Text.ToLower().Trim();
        var filters = filterText.Split('|').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList();
        
        _packets.Clear();
        if (filters.Count == 0)
        {
            foreach (var p in _allPackets) _packets.Add(p);
        }
        else
        {
            var filtered = _allPackets.Where(p => filters.Any(f =>
                p.Source.ToLower().Contains(f) || p.Destination.ToLower().Contains(f) ||
                p.Protocol.ToLower().Contains(f) || p.Info.ToLower().Contains(f)
            )).ToList();
            foreach (var p in filtered) _packets.Add(p);
        }
    }

    private void OnProtocolDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProtocolStats.SelectedItem is ProtocolStatItem stat)
        {
            FilterBox.Text = stat.Protocol;
            OnApplyFilter(null, null);
        }
    }

    private void OnPacketSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PacketGrid.SelectedItem is not PacketRow packet || packet.RawPacket == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Packet #{packet.No} ===\nTime: {packet.Time}\nLength: {packet.Length} bytes\n");

        var ipPacket = packet.RawPacket.Extract<IPPacket>();
        if (ipPacket != null)
            sb.AppendLine($"Source: {ipPacket.SourceAddress}\nDest: {ipPacket.DestinationAddress}\nTTL: {ipPacket.TimeToLive}\n");

        var tcpPacket = packet.RawPacket.Extract<TcpPacket>();
        if (tcpPacket != null)
            sb.AppendLine($"TCP {tcpPacket.SourcePort}→{tcpPacket.DestinationPort}\nSeq: {tcpPacket.SequenceNumber}\nAck: {tcpPacket.AcknowledgmentNumber}\n");

        var udpPacket = packet.RawPacket.Extract<UdpPacket>();
        if (udpPacket != null)
            sb.AppendLine($"UDP {udpPacket.SourcePort}→{udpPacket.DestinationPort}\n");

        DetailView.Text = sb.ToString();
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
