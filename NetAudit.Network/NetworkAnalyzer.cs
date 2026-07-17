using NetAudit.Core.Interfaces;

namespace NetAudit.Network;

public class NetworkAnalyzer : IScanService
{
    public string ServiceName => "Network Analysis";
    public event EventHandler<ScanProgressEventArgs>? ProgressChanged;

    public Task<ScanResult> ExecuteAsync(CancellationToken ct)
    {
        return Task.FromResult(new ScanResult
        {
            ServiceName = ServiceName,
            Success = true,
            Message = "Placeholder implementation",
            Timestamp = DateTime.Now
        });
    }
}
