using FlowDesk.Tests.TestSupport;
using FlowDesk.Web.Application;
using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.UnitTests;

public class SlaServiceTests : ServiceTestBase
{
    [Fact]
    public async Task CalculateDeadline_UsesPolicyHoursForCategoryAndPriority()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaService>();

        var category = await db.Categories.FirstAsync(x => x.Name == "Ремонт техники");

        var started = DateTime.UtcNow;
        var (deadline, policyId) = await sla.CalculateDeadlineAsync(category.Id, RequestPriority.Critical);
        var finished = DateTime.UtcNow;

        // Политика «Критический = 4 часа» для seeded-категории.
        Assert.NotNull(policyId);
        Assert.InRange(deadline.Ticks, started.AddHours(4).AddSeconds(-5).Ticks, finished.AddHours(4).AddSeconds(5).Ticks);
    }

    [Fact]
    public async Task CalculateDeadline_FallsBackToCategoryDefaultHours_WhenPolicyMissing()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaService>();

        db.Categories.Add(new Category { Name = "Поддержка зданий", Description = "Хозяйственные заявки", DefaultSlaHours = 36 });
        await db.SaveChangesAsync();
        var category = await db.Categories.FirstAsync(x => x.Name == "Поддержка зданий");

        var started = DateTime.UtcNow;
        var (deadline, policyId) = await sla.CalculateDeadlineAsync(category.Id, RequestPriority.Normal);
        var finished = DateTime.UtcNow;

        Assert.Null(policyId);
        Assert.InRange(deadline.Ticks, started.AddHours(36).AddSeconds(-5).Ticks, finished.AddHours(36).AddSeconds(5).Ticks);
    }

    [Fact]
    public async Task CalculateDeadline_UsesDefault24Hours_WhenCategoryMissing()
    {
        using var scope = Provider.CreateScope();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaService>();

        var started = DateTime.UtcNow;
        var (deadline, policyId) = await sla.CalculateDeadlineAsync(categoryId: 999999, RequestPriority.Normal);
        var finished = DateTime.UtcNow;

        Assert.Null(policyId);
        Assert.InRange(deadline.Ticks, started.AddHours(24).AddSeconds(-5).Ticks, finished.AddHours(24).AddSeconds(5).Ticks);
    }

    [Fact]
    public async Task GetSlaStatus_MarksOverdue_WhenDeadlinePassed()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaService>();

        var request = await SeedRequestAsync(db, slaDeadlineHours: -3, closedAt: null);

        var status = await sla.GetSlaStatusAsync(request.Id);

        Assert.Equal(SlaStates.Overdue, status.State);
        Assert.NotNull(status.Remaining);
        Assert.True(status.Remaining!.Value < TimeSpan.Zero);
    }

    [Fact]
    public async Task GetSlaStatus_MarksCompleted_WhenRequestClosed()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var sla = scope.ServiceProvider.GetRequiredService<ISlaService>();

        var request = await SeedRequestAsync(db, slaDeadlineHours: 2, closedAt: DateTime.UtcNow.AddMinutes(-30));

        var status = await sla.GetSlaStatusAsync(request.Id);

        Assert.Equal(SlaStates.Completed, status.State);
    }

    private async Task<ServiceRequest> SeedRequestAsync(FlowDeskDbContext db, int slaDeadlineHours, DateTime? closedAt)
    {
        var now = DateTime.UtcNow;
        var statusName = closedAt is null ? "Новая" : "Выполнено";
        var request = new ServiceRequest
        {
            Title = "Тестовая заявка SLA",
            Description = "Заявка для проверки расчёта SLA-статуса.",
            CategoryId = await db.Categories.Where(x => x.Name == "Ремонт техники").Select(x => x.Id).FirstAsync(),
            StatusId = await db.RequestStatuses
                .Where(x => x.Name == statusName)
                .Select(x => x.Id)
                .FirstAsync(),
            Priority = RequestPriority.Normal,
            ClientId = Users["client"].Id,
            SlaDeadline = now.AddHours(slaDeadlineHours),
            ClosedAt = closedAt,
            CreatedAt = now.AddHours(-30),
            UpdatedAt = now
        };
        db.Requests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }
}
