namespace NetAudit.Reporting;

public class ReportGenerator
{
    public string GenerateReport(string format = "pdf")
    {
        return $"Report generated in {format} format";
    }
}
