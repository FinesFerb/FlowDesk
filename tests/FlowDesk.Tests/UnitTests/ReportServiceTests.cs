using System.Text;
using FlowDesk.Tests.TestSupport;
using FlowDesk.Web.Application;
using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.UnitTests;

public class ReportServiceTests : ServiceTestBase
{
    [Fact]
    public async Task GetSummary_CountsRequestsInPeriodCorrectly()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

        await SeedRequestsAsync(db);

        var now = DateTime.UtcNow;
        var summary = await reportService.GetSummaryAsync(now.AddDays(-7), now);

        Assert.Equal(2, summary.TotalRequests);
        Assert.Equal(1, summary.CompletedRequests);
        Assert.Equal(1, summary.OverdueRequests);
        Assert.Equal(12.0, summary.AverageCompletionHours);
        Assert.Equal(50.0, summary.SlaBreachPercent);
        Assert.Contains(summary.ByStatus, x => x.Name == "Новая" && x.Count == 1);
        Assert.Contains(summary.ByStatus, x => x.Name == "Выполнено" && x.Count == 1);
    }

    [Fact]
    public async Task ExportCsv_WritesHeaderAndTotals()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
        await SeedRequestsAsync(db);

        var now = DateTime.UtcNow;
        var bytes = await reportService.ExportCsvAsync(now.AddDays(-7), now);
        var csv = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

        Assert.StartsWith("FlowDesk — отчёт по заявкам", csv);
        Assert.Contains("Период;", csv);
        Assert.Contains("Всего заявок;2", csv);
        Assert.Contains("Завершено;1", csv);
        Assert.Contains("Просрочено;1", csv);
        Assert.Contains("Доля нарушений SLA, %;50", csv);
    }

    private async Task SeedRequestsAsync(FlowDeskDbContext db)
    {
        var now = DateTime.UtcNow;
        var repair = await db.Categories.FirstAsync(x => x.Name == "Ремонт техники");
        var newStatus = await db.RequestStatuses.FirstAsync(x => x.Name == "Новая");
        var completedStatus = await db.RequestStatuses.FirstAsync(x => x.Name == "Выполнено");
        var client = Users["client"];

        db.Requests.AddRange(
            // Завершена в срок в периоде: закрыта раньше дедлайна, 12 ч от создания до закрытия.
            new ServiceRequest
            {
                Title = "Заявка завершённая в срок",
                Description = "Диагностика ноутбука.",
                CategoryId = repair.Id,
                StatusId = completedStatus.Id,
                Priority = RequestPriority.Normal,
                ClientId = client.Id,
                SlaDeadline = now.AddHours(-10),
                ClosedAt = now.AddHours(-12),
                CreatedAt = now.AddDays(-1)
            },
            // Просрочена и не закрыта, в периоде.
            new ServiceRequest
            {
                Title = "Просроченная заявка",
                Description = "Не обработана вовремя.",
                CategoryId = repair.Id,
                StatusId = newStatus.Id,
                Priority = RequestPriority.High,
                ClientId = client.Id,
                SlaDeadline = now.AddHours(-1),
                CreatedAt = now.AddDays(-1)
            },
            // Вне периода — не попадает в отчёт.
            new ServiceRequest
            {
                Title = "Слишком старая заявка",
                Description = "Создана сорок дней назад.",
                CategoryId = repair.Id,
                StatusId = newStatus.Id,
                Priority = RequestPriority.Low,
                ClientId = client.Id,
                SlaDeadline = now.AddDays(-10),
                CreatedAt = now.AddDays(-40)
            });
        await db.SaveChangesAsync();
    }
}
