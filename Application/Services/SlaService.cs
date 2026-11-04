using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Web.Application.Services;

public class SlaService(FlowDeskDbContext db) : ISlaService
{
    public async Task<(DateTime Deadline, int? PolicyId)> CalculateDeadlineAsync(
        int categoryId,
        RequestPriority priority,
        CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CategoryId == categoryId && x.Priority == priority, ct);

        if (policy is not null)
        {
            return (DateTime.UtcNow.AddHours(policy.Hours), policy.Id);
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == categoryId, ct);
        var fallbackHours = category?.DefaultSlaHours ?? 24;
        return (DateTime.UtcNow.AddHours(fallbackHours), null);
    }

    public async Task<SlaStatusDto> GetSlaStatusAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await db.Requests
            .AsNoTracking()
            .Include(x => x.Status)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct);

        if (request?.SlaDeadline is null)
        {
            return new SlaStatusDto { State = SlaStates.NoSla };
        }

        if (request.ClosedAt is not null || request.Status.Name is "Выполнено" or "Отклонено")
        {
            return new SlaStatusDto
            {
                Deadline = request.SlaDeadline,
                Remaining = request.SlaDeadline - (request.ClosedAt ?? DateTime.UtcNow),
                State = SlaStates.Completed
            };
        }

        var remaining = request.SlaDeadline.Value - DateTime.UtcNow;
        var state = remaining < TimeSpan.Zero
            ? SlaStates.Overdue
            : remaining <= TimeSpan.FromHours(2)
                ? SlaStates.AtRisk
                : SlaStates.OnTrack;

        return new SlaStatusDto
        {
            Deadline = request.SlaDeadline,
            Remaining = remaining,
            State = state
        };
    }
}
