using NetAudit.Core.Interfaces;

namespace NetAudit.PacketCapture;

/// <summary>Packet capture service implementation (placeholder)</summary>
public class PacketCaptureService : IScanService
{
    public string ServiceName => "Packet Capture";

    public event EventHandler<ScanProgressEventArgs>? ProgressChanged;

    public Task<ScanResult> ExecuteAsync(CancellationToken ct)
    {
        return Task.FromResult(new ScanResult
        {
            ServiceName = ServiceName,
            Success = true,
            Message = "Placeholder: Packet capture not yet implemented",
            Timestamp = DateTime.Now
        });
    }
}
