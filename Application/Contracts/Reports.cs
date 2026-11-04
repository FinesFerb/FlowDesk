namespace FlowDesk.Web.Application.Contracts;

public class ReportSummaryDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalRequests { get; set; }
    public int CompletedRequests { get; set; }
    public int OverdueRequests { get; set; }
    public double AverageCompletionHours { get; set; }
    public double SlaBreachPercent { get; set; }
    public List<ReportRowDto> ByStatus { get; set; } = [];
    public List<ReportRowDto> ByCategory { get; set; } = [];
    public List<ReportRowDto> ByPriority { get; set; } = [];
}

public class ReportRowDto
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public double SharePercent { get; set; }
}
