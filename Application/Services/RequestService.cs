using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Web.Application.Services;

public class RequestService(
    FlowDeskDbContext db,
    ISlaService slaService,
    INotificationService notificationService,
    IFileStorage fileStorage,
    UserManager<ApplicationUser> userManager) : IRequestService
{
    public async Task<RequestDto> CreateRequestAsync(CreateRequestDto dto, Guid clientId, CancellationToken ct = default)
    {
        var client = await db.Users.FirstOrDefaultAsync(x => x.Id == clientId, ct)
            ?? throw new InvalidOperationException("Клиент не найден.");
        var category = await db.Categories.FirstOrDefaultAsync(x => x.Id == dto.CategoryId, ct)
            ?? throw new InvalidOperationException("Категория не найдена.");
        var status = await db.RequestStatuses.FirstOrDefaultAsync(x => x.Name == "Новая", ct)
            ?? throw new InvalidOperationException("Статус «Новая» не настроен.");
        var (deadline, policyId) = await slaService.CalculateDeadlineAsync(category.Id, dto.Priority, ct);

        var request = new ServiceRequest
        {
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            CategoryId = category.Id,
            StatusId = status.Id,
            Priority = dto.Priority,
            ClientId = client.Id,
            SlaDeadline = deadline,
            SlaPolicyId = policyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Requests.Add(request);
        db.AuditLogs.Add(new AuditLog
        {
            RequestId = request.Id,
            UserId = client.Id,
            Action = "Заявка создана",
            NewValue = $"Статус: {status.Name}; приоритет: {request.Priority}"
        });
        await db.SaveChangesAsync(ct);

        await notificationService.SendNewRequestToDispatcherAsync(request.Id, ct);

        return await GetByIdAsync(request.Id, clientId, RoleNames.Client, ct)
            ?? throw new InvalidOperationException("Не удалось прочитать созданную заявку.");
    }

    public async Task<PagedResult<RequestListItemDto>> GetRequestsAsync(
        RequestFilter filter,
        Guid userId,
        string role,
        CancellationToken ct = default)
    {
        var query = db.Requests
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Status)
            .Include(x => x.Client)
            .Include(x => x.Executor)
            .AsQueryable();

        query = role switch
        {
            RoleNames.Client => query.Where(x => x.ClientId == userId),
            RoleNames.Executor => query.Where(x => x.ExecutorId == userId),
            RoleNames.Dispatcher or RoleNames.Manager or RoleNames.Admin => query,
            _ => query.Where(_ => false)
        };

        if (filter.StatusId is { } statusId)
        {
            query = query.Where(x => x.StatusId == statusId);
        }
        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(x => x.CategoryId == categoryId);
        }
        if (filter.Priority is { } priority)
        {
            query = query.Where(x => x.Priority == priority);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(x =>
                x.Title.ToLower().Contains(search) ||
                x.Description.ToLower().Contains(search) ||
                x.Client.FullName.ToLower().Contains(search));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 5, 100);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new RequestListItemDto
            {
                Id = x.Id,
                Title = x.Title,
                CategoryId = x.CategoryId,
                CategoryName = x.Category.Name,
                StatusName = x.Status.Name,
                Priority = x.Priority,
                ClientName = x.Client.FullName,
                ExecutorName = x.Executor == null ? null : x.Executor.FullName,
                SlaDeadline = x.SlaDeadline,
                CreatedAt = x.CreatedAt,
                ClosedAt = x.ClosedAt
            })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.SlaState = GetSlaState(item.SlaDeadline, item.ClosedAt);
        }

        return new PagedResult<RequestListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<RequestDto?> GetByIdAsync(Guid id, Guid userId, string role, CancellationToken ct = default)
    {
        var request = await db.Requests
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Category)
            .Include(x => x.Status)
            .Include(x => x.Client)
            .Include(x => x.Executor)
            .Include(x => x.Dispatcher)
            .Include(x => x.Comments).ThenInclude(x => x.Author)
            .Include(x => x.Attachments).ThenInclude(x => x.UploadedBy)
            .Include(x => x.AuditLogs).ThenInclude(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (request is null || !CanAccess(request, userId, role))
        {
            return null;
        }

        return MapDetails(request);
    }

    public async Task AssignExecutorAsync(
        Guid requestId,
        Guid executorId,
        DateTime slaDeadline,
        Guid dispatcherId,
        CancellationToken ct = default)
    {
        var request = await db.Requests
            .Include(x => x.Status)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct)
            ?? throw new InvalidOperationException("Заявка не найдена.");

        var executor = await db.Users.FirstOrDefaultAsync(x => x.Id == executorId, ct)
            ?? throw new InvalidOperationException("Исполнитель не найден.");
        if (!await userManager.IsInRoleAsync(executor, RoleNames.Executor))
        {
            throw new InvalidOperationException("Выбранный пользователь не является исполнителем.");
        }

        var assignedStatus = await db.RequestStatuses.FirstOrDefaultAsync(x => x.Name == "Назначена", ct)
            ?? throw new InvalidOperationException("Статус «Назначена» не настроен.");

        var oldValue = $"Статус: {request.Status.Name}; исполнитель: {request.ExecutorId}";
        request.ExecutorId = executorId;
        request.DispatcherId = dispatcherId;
        request.StatusId = assignedStatus.Id;
        request.SlaDeadline = slaDeadline;
        request.UpdatedAt = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            RequestId = request.Id,
            UserId = dispatcherId,
            Action = "Назначен исполнитель",
            OldValue = oldValue,
            NewValue = $"Статус: {assignedStatus.Name}; исполнитель: {executor.FullName}; SLA: {slaDeadline:dd.MM.yyyy HH:mm}"
        });

        await db.SaveChangesAsync(ct);
        await notificationService.SendAssignmentToExecutorAsync(request.Id, executorId, ct);
        await notificationService.SendStatusChangedAsync(request.Id, assignedStatus.Name, ct);
    }

    public async Task UpdateStatusAsync(Guid requestId, int statusId, Guid userId, CancellationToken ct = default)
    {
        var request = await db.Requests
            .Include(x => x.Status)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct)
            ?? throw new InvalidOperationException("Заявка не найдена.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new InvalidOperationException("Пользователь не найден.");

        var isStaff = await userManager.IsInRoleAsync(user, RoleNames.Dispatcher)
            || await userManager.IsInRoleAsync(user, RoleNames.Manager)
            || await userManager.IsInRoleAsync(user, RoleNames.Admin);
        if (request.ExecutorId != userId && !isStaff)
        {
            throw new UnauthorizedAccessException("Недостаточно прав для изменения статуса заявки.");
        }

        var status = await db.RequestStatuses.FirstOrDefaultAsync(x => x.Id == statusId, ct)
            ?? throw new InvalidOperationException("Статус не найден.");

        var oldStatus = request.Status.Name;
        request.StatusId = status.Id;
        request.UpdatedAt = DateTime.UtcNow;
        request.ClosedAt = status.Name is "Выполнено" or "Отклонено" ? DateTime.UtcNow : null;

        db.AuditLogs.Add(new AuditLog
        {
            RequestId = request.Id,
            UserId = userId,
            Action = "Изменён статус",
            OldValue = oldStatus,
            NewValue = status.Name
        });

        await db.SaveChangesAsync(ct);
        await notificationService.SendStatusChangedAsync(request.Id, status.Name, ct);
    }

    public async Task AddCommentAsync(Guid requestId, Guid authorId, string text, CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct)
            ?? throw new InvalidOperationException("Заявка не найдена.");
        if (!await CanAccessAsync(request, authorId))
        {
            throw new UnauthorizedAccessException("Нет доступа к заявке.");
        }

        db.Comments.Add(new Comment
        {
            RequestId = requestId,
            AuthorId = authorId,
            Text = text.Trim()
        });
        db.AuditLogs.Add(new AuditLog
        {
            RequestId = requestId,
            UserId = authorId,
            Action = "Добавлен комментарий",
            NewValue = text.Length > 300 ? text[..300] + "…" : text
        });
        await db.SaveChangesAsync(ct);
        await notificationService.SendCommentAsync(requestId, authorId, text, ct);
    }

    public async Task<AttachmentDto> AddAttachmentAsync(
        Guid requestId,
        Guid userId,
        Stream file,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct)
            ?? throw new InvalidOperationException("Заявка не найдена.");
        if (!await CanAccessAsync(request, userId))
        {
            throw new UnauthorizedAccessException("Нет доступа к заявке.");
        }

        var (storedName, size) = await fileStorage.SaveAsync(file, fileName, contentType, ct);
        var attachment = new Attachment
        {
            RequestId = requestId,
            UploadedById = userId,
            FileName = Path.GetFileName(fileName),
            StoredName = storedName,
            ContentType = contentType,
            FileSize = size
        };
        db.Attachments.Add(attachment);
        db.AuditLogs.Add(new AuditLog
        {
            RequestId = requestId,
            UserId = userId,
            Action = "Добавлено вложение",
            NewValue = attachment.FileName
        });
        await db.SaveChangesAsync(ct);

        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        return new AttachmentDto
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            FileSize = attachment.FileSize,
            UploadedByName = user.FullName,
            CreatedAt = attachment.CreatedAt
        };
    }

    public async Task<FileDownloadDto?> GetAttachmentAsync(Guid attachmentId, Guid userId, string role, CancellationToken ct = default)
    {
        var attachment = await db.Attachments
            .AsNoTracking()
            .Include(x => x.Request)
            .FirstOrDefaultAsync(x => x.Id == attachmentId, ct);

        if (attachment is null || !CanAccess(attachment.Request, userId, role))
        {
            return null;
        }

        return new FileDownloadDto
        {
            FilePath = fileStorage.GetPath(attachment.StoredName),
            FileName = attachment.FileName,
            ContentType = attachment.ContentType
        };
    }

    public async Task<IReadOnlyList<ApplicationUser>> GetExecutorsAsync(CancellationToken ct = default)
    {
        var executors = await userManager.GetUsersInRoleAsync(RoleNames.Executor);
        return executors.Where(x => x.IsActive).OrderBy(x => x.FullName).ToList();
    }

    public async Task<IReadOnlyList<RequestStatus>> GetStatusesAsync(CancellationToken ct = default)
    {
        return await db.RequestStatuses.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct);
    }

    public async Task MarkNotificationsReadAsync(Guid userId, CancellationToken ct = default)
    {
        await db.Notifications
            .Where(x => x.UserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true), ct);
    }

    private static bool CanAccess(ServiceRequest request, Guid userId, string role) => role switch
    {
        RoleNames.Client => request.ClientId == userId,
        RoleNames.Executor => request.ExecutorId == userId,
        RoleNames.Dispatcher or RoleNames.Manager or RoleNames.Admin => true,
        _ => false
    };

    private async Task<bool> CanAccessAsync(ServiceRequest request, Guid userId)
    {
        if (request.ClientId == userId || request.ExecutorId == userId || request.DispatcherId == userId)
        {
            return true;
        }

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        return user is not null
            && (await userManager.IsInRoleAsync(user, RoleNames.Dispatcher)
                || await userManager.IsInRoleAsync(user, RoleNames.Manager)
                || await userManager.IsInRoleAsync(user, RoleNames.Admin));
    }

    private static RequestDto MapDetails(ServiceRequest request) => new()
    {
        Id = request.Id,
        Title = request.Title,
        Description = request.Description,
        CategoryId = request.CategoryId,
        CategoryName = request.Category.Name,
        StatusName = request.Status.Name,
        Priority = request.Priority,
        ClientId = request.ClientId,
        ClientName = request.Client.FullName,
        ExecutorId = request.ExecutorId,
        ExecutorName = request.Executor?.FullName,
        DispatcherId = request.DispatcherId,
        DispatcherName = request.Dispatcher?.FullName,
        SlaDeadline = request.SlaDeadline,
        SlaState = GetSlaState(request.SlaDeadline, request.ClosedAt),
        CreatedAt = request.CreatedAt,
        UpdatedAt = request.UpdatedAt,
        ClosedAt = request.ClosedAt,
        Comments = request.Comments
            .OrderBy(x => x.CreatedAt)
            .Select(x => new CommentDto
            {
                Id = x.Id,
                AuthorName = x.Author.FullName,
                Text = x.Text,
                CreatedAt = x.CreatedAt
            })
            .ToList(),
        Attachments = request.Attachments
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AttachmentDto
            {
                Id = x.Id,
                FileName = x.FileName,
                ContentType = x.ContentType,
                FileSize = x.FileSize,
                UploadedByName = x.UploadedBy.FullName,
                CreatedAt = x.CreatedAt
            })
            .ToList(),
        History = request.AuditLogs
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AuditLogDto
            {
                Action = x.Action,
                OldValue = x.OldValue,
                NewValue = x.NewValue,
                UserName = x.User?.FullName,
                CreatedAt = x.CreatedAt
            })
            .ToList()
    };

    private static string GetSlaState(DateTime? deadline, DateTime? closedAt)
    {
        if (closedAt is not null)
        {
            return SlaStates.Completed;
        }
        if (deadline is null)
        {
            return SlaStates.NoSla;
        }

        var remaining = deadline.Value - DateTime.UtcNow;
        return remaining < TimeSpan.Zero
            ? SlaStates.Overdue
            : remaining <= TimeSpan.FromHours(2)
                ? SlaStates.AtRisk
                : SlaStates.OnTrack;
    }
}
