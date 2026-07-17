using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NetAudit.Core.Interfaces;

/// <summary>Base interface for all scan services</summary>
public interface IScanService
{
    string ServiceName { get; }
    Task<ScanResult> ExecuteAsync(CancellationToken ct);
    event EventHandler<ScanProgressEventArgs>? ProgressChanged;
}

/// <summary>Scan result data model</summary>
public class ScanResult
{
    public string ServiceName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>Progress event arguments</summary>
public class ScanProgressEventArgs : EventArgs
{
    public int PercentComplete { get; set; }
    public string Message { get; set; } = string.Empty;
}
