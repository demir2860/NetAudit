using NetAudit.Core.Interfaces;

namespace NetAudit.Windows;

public class WindowsDiagnostics : IScanService
{
    public string ServiceName => "Windows Infrastructure";
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
