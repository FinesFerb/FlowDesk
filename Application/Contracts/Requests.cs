using System.ComponentModel.DataAnnotations;
using FlowDesk.Web.Domain;

namespace FlowDesk.Web.Application.Contracts;

public class RequestListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public RequestPriority Priority { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ExecutorName { get; set; }
    public DateTime? SlaDeadline { get; set; }
    public string SlaState { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class RequestDto : RequestListItemDto
{
    public string Description { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid? ExecutorId { get; set; }
    public Guid? DispatcherId { get; set; }
    public string? DispatcherName { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<CommentDto> Comments { get; set; } = [];
    public List<AttachmentDto> Attachments { get; set; } = [];
    public List<AuditLogDto> History { get; set; } = [];
}

public class CreateRequestDto
{
    [Required(ErrorMessage = "Укажите тему заявки")]
    [StringLength(200, MinimumLength = 5, ErrorMessage = "Тема должна быть от 5 до 200 символов")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Опишите проблему")]
    [StringLength(4000, MinimumLength = 10, ErrorMessage = "Описание должно быть от 10 до 4000 символов")]
    public string Description { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Выберите категорию")]
    public int CategoryId { get; set; }

    public RequestPriority Priority { get; set; } = RequestPriority.Normal;
}

public class RequestFilter
{
    public int? StatusId { get; set; }
    public int? CategoryId { get; set; }
    public RequestPriority? Priority { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class CommentDto
{
    public Guid Id { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AuditLogDto
{
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? UserName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SlaStatusDto
{
    public DateTime? Deadline { get; set; }
    public TimeSpan? Remaining { get; set; }
    public string State { get; set; } = SlaStates.NoSla;
}

public static class SlaStates
{
    public const string NoSla = "Без SLA";
    public const string OnTrack = "В срок";
    public const string AtRisk = "Риск просрочки";
    public const string Overdue = "Просрочено";
    public const string Completed = "Завершено";
}

public class FileDownloadDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}
