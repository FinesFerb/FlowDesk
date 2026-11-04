using System.Net;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Web.Application.Services;

public class NotificationService(
    FlowDeskDbContext db,
    UserManager<ApplicationUser> userManager,
    IAppEmailSender emailSender,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task SendStatusChangedAsync(Guid requestId, string newStatus, CancellationToken ct = default)
    {
        var request = await db.Requests
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct);

        if (request is null)
        {
            return;
        }

        await NotifyAsync(
            request.ClientId,
            requestId,
            "status_changed",
            $"Статус заявки «{request.Title}» изменён на «{newStatus}».",
            $"FlowDesk: статус заявки изменён на «{newStatus}»",
            ct);
    }

    public async Task SendNewRequestToDispatcherAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await db.Requests
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct);

        if (request is null)
        {
            return;
        }

        var dispatchers = await userManager.GetUsersInRoleAsync(RoleNames.Dispatcher);
        foreach (var dispatcher in dispatchers.Where(x => x.IsActive))
        {
            await NotifyAsync(
                dispatcher.Id,
                requestId,
                "new_request",
                $"Новая заявка «{request.Title}» в категории «{request.Category.Name}».",
                "FlowDesk: новая заявка",
                ct);
        }
    }

    public async Task SendAssignmentToExecutorAsync(Guid requestId, Guid executorId, CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (request is null)
        {
            return;
        }

        await NotifyAsync(
            executorId,
            requestId,
            "assignment",
            $"Вам назначена заявка «{request.Title}». SLA: {request.SlaDeadline:dd.MM.yyyy HH:mm}.",
            "FlowDesk: назначена заявка",
            ct);
    }

    public async Task SendCommentAsync(Guid requestId, Guid authorId, string text, CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (request is null)
        {
            return;
        }

        var recipients = new HashSet<Guid> { request.ClientId };
        if (request.ExecutorId is { } executorId)
        {
            recipients.Add(executorId);
        }
        if (request.DispatcherId is { } dispatcherId)
        {
            recipients.Add(dispatcherId);
        }
        recipients.Remove(authorId);

        var preview = text.Length > 160 ? text[..160] + "…" : text;
        foreach (var recipient in recipients)
        {
            await NotifyAsync(
                recipient,
                requestId,
                "comment",
                $"Новый комментарий к заявке «{request.Title}»: {preview}",
                "FlowDesk: новый комментарий",
                ct);
        }
    }

    private async Task NotifyAsync(
        Guid userId,
        Guid? requestId,
        string type,
        string message,
        string emailSubject,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive)
        {
            return;
        }

        db.Notifications.Add(new Notification
        {
            UserId = userId,
            RequestId = requestId,
            Type = type,
            Message = message
        });
        await db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        try
        {
            var safeMessage = WebUtility.HtmlEncode(message);
            await emailSender.SendAsync(
                user.Email,
                emailSubject,
                $"<p>Здравствуйте, {WebUtility.HtmlEncode(user.FullName)}!</p><p>{safeMessage}</p><p>— FlowDesk</p>",
                ct);
        }
        catch (Exception ex)
        {
            // Email delivery must not break the business operation.
            logger.LogWarning(ex, "Не удалось отправить email пользователю {Email}", user.Email);
        }
    }
}
