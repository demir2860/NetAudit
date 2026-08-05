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
            var type = GetDeviceType(device.Description);
            var displayName = $"[{type}] {device.Description}";
            DeviceSelector.Items.Add(displayName);
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

                if (tcpPacket != null)
                {
                    protocol = "TCP";
                    info = $"{tcpPacket.SourcePort} → {tcpPacket.DestinationPort}";
                }
                else if (udpPacket != null)
                {
                    protocol = "UDP";
                    info = $"{udpPacket.SourcePort} → {udpPacket.DestinationPort}";
                }
                else if (ipPacket != null)
                {
                    protocol = ipPacket.Protocol.ToString();
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
    }

    private void OnPacketSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PacketGrid.SelectedItem is not PacketRow packet || packet.RawPacket == null) return;
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Packet #{packet.No} ===");
        sb.AppendLine($"Time: {packet.Time}");
        sb.AppendLine($"Length: {packet.Length} bytes");
        sb.AppendLine();

        var ipPacket = packet.RawPacket.Extract<IPPacket>();
        if (ipPacket != null)
        {
            sb.AppendLine($"Source IP: {ipPacket.SourceAddress}");
            sb.AppendLine($"Dest IP: {ipPacket.DestinationAddress}");
            sb.AppendLine($"TTL: {ipPacket.TimeToLive}");
            sb.AppendLine();
        }

        var tcpPacket = packet.RawPacket.Extract<TcpPacket>();
        if (tcpPacket != null)
        {
            sb.AppendLine($"TCP Source Port: {tcpPacket.SourcePort}");
            sb.AppendLine($"TCP Dest Port: {tcpPacket.DestinationPort}");
            sb.AppendLine($"Seq: {tcpPacket.SequenceNumber}");
            sb.AppendLine($"Ack: {tcpPacket.AcknowledgmentNumber}");
            sb.AppendLine();
        }

        var udpPacket = packet.RawPacket.Extract<UdpPacket>();
        if (udpPacket != null)
        {
            sb.AppendLine($"UDP Source Port: {udpPacket.SourcePort}");
            sb.AppendLine($"UDP Dest Port: {udpPacket.DestinationPort}");
            sb.AppendLine();
        }

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
