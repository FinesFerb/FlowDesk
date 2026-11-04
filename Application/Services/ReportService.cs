using System.Text;
using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FlowDesk.Web.Application.Services;

public class ReportService(FlowDeskDbContext db) : IReportService
{
    public async Task<ReportSummaryDto> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var requests = await LoadRequestsAsync(from, to, ct);
        var now = DateTime.UtcNow;
        var completed = requests.Where(x => x.ClosedAt is not null).ToList();
        var overdue = requests.Count(x => x.SlaDeadline is not null && (x.ClosedAt ?? now) > x.SlaDeadline);
        var total = requests.Count;

        return new ReportSummaryDto
        {
            From = from,
            To = to,
            TotalRequests = total,
            CompletedRequests = completed.Count,
            OverdueRequests = overdue,
            AverageCompletionHours = completed.Count == 0
                ? 0
                : Math.Round(completed.Average(x => (x.ClosedAt!.Value - x.CreatedAt).TotalHours), 1),
            SlaBreachPercent = total == 0 ? 0 : Math.Round(overdue * 100.0 / total, 1),
            ByStatus = BuildRows(requests.GroupBy(x => x.Status.Name).Select(g => (g.Key, g.Count())), total),
            ByCategory = BuildRows(requests.GroupBy(x => x.Category.Name).Select(g => (g.Key, g.Count())), total),
            ByPriority = BuildRows(requests.GroupBy(x => x.Priority.ToRussian()).Select(g => (g.Key, g.Count())), total)
        };
    }

    public async Task<byte[]> ExportCsvAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var summary = await GetSummaryAsync(from, to, ct);
        var sb = new StringBuilder();
        sb.AppendLine("FlowDesk — отчёт по заявкам");
        sb.AppendLine($"Период;{summary.From:dd.MM.yyyy};{summary.To:dd.MM.yyyy}");
        sb.AppendLine();
        sb.AppendLine("Показатель;Значение");
        sb.AppendLine($"Всего заявок;{summary.TotalRequests}");
        sb.AppendLine($"Завершено;{summary.CompletedRequests}");
        sb.AppendLine($"Просрочено;{summary.OverdueRequests}");
        sb.AppendLine($"Среднее время выполнения, ч;{summary.AverageCompletionHours}");
        sb.AppendLine($"Доля нарушений SLA, %;{summary.SlaBreachPercent}");
        AppendRows(sb, "По статусам", summary.ByStatus);
        AppendRows(sb, "По категориям", summary.ByCategory);
        AppendRows(sb, "По приоритетам", summary.ByPriority);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Encoding.UTF8.GetPreamble().Concat(bytes).ToArray();
    }

    public async Task<byte[]> ExportPdfAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var summary = await GetSummaryAsync(from, to, ct);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text("FlowDesk — отчёт по заявкам").FontSize(18).SemiBold();
                    column.Item().Text($"Период: {summary.From:dd.MM.yyyy} — {summary.To:dd.MM.yyyy}").FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Всего заявок: {summary.TotalRequests}");
                        row.RelativeItem().Text($"Завершено: {summary.CompletedRequests}");
                        row.RelativeItem().Text($"Просрочено: {summary.OverdueRequests}");
                    });
                    column.Item().Text($"Среднее время выполнения: {summary.AverageCompletionHours} ч. Доля нарушений SLA: {summary.SlaBreachPercent}%.");

                    AddTable(column, "По статусам", summary.ByStatus);
                    AddTable(column, "По категориям", summary.ByCategory);
                    AddTable(column, "По приоритетам", summary.ByPriority);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Страница ");
                    text.CurrentPageNumber();
                    text.Span(" из ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private async Task<List<Domain.Entities.ServiceRequest>> LoadRequestsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        return await db.Requests
            .AsNoTracking()
            .Include(x => x.Status)
            .Include(x => x.Category)
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(ct);
    }

    private static List<ReportRowDto> BuildRows(IEnumerable<(string Name, int Count)> values, int total)
    {
        return values
            .OrderByDescending(x => x.Count)
            .Select(x => new ReportRowDto
            {
                Name = x.Name,
                Count = x.Count,
                SharePercent = total == 0 ? 0 : Math.Round(x.Count * 100.0 / total, 1)
            })
            .ToList();
    }

    private static void AppendRows(StringBuilder sb, string title, List<ReportRowDto> rows)
    {
        sb.AppendLine();
        sb.AppendLine(title);
        sb.AppendLine("Название;Количество;Доля, %");
        foreach (var row in rows)
        {
            sb.AppendLine($"{row.Name};{row.Count};{row.SharePercent}");
        }
    }

    private static void AddTable(ColumnDescriptor column, string title, List<ReportRowDto> rows)
    {
        column.Item().PaddingTop(8).Text(title).SemiBold().FontSize(12);
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.RelativeColumn(1);
                columns.RelativeColumn(1);
            });

            table.Header(header =>
            {
                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Название").SemiBold();
                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Кол-во").SemiBold();
                header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Доля, %").SemiBold();
            });

            foreach (var row in rows)
            {
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(row.Name);
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(row.Count.ToString());
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(row.SharePercent.ToString("0.0"));
            }
        });
    }
}
