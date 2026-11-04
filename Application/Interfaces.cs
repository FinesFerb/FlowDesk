using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain.Entities;

namespace FlowDesk.Web.Application;

public interface IRequestService
{
    Task<RequestDto> CreateRequestAsync(CreateRequestDto dto, Guid clientId, CancellationToken ct = default);
    Task<PagedResult<RequestListItemDto>> GetRequestsAsync(RequestFilter filter, Guid userId, string role, CancellationToken ct = default);
    Task<RequestDto?> GetByIdAsync(Guid id, Guid userId, string role, CancellationToken ct = default);
    Task AssignExecutorAsync(Guid requestId, Guid executorId, DateTime slaDeadline, Guid dispatcherId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid requestId, int statusId, Guid userId, CancellationToken ct = default);
    Task AddCommentAsync(Guid requestId, Guid authorId, string text, CancellationToken ct = default);
    Task<AttachmentDto> AddAttachmentAsync(Guid requestId, Guid userId, Stream file, string fileName, string contentType, CancellationToken ct = default);
    Task<FileDownloadDto?> GetAttachmentAsync(Guid attachmentId, Guid userId, string role, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationUser>> GetExecutorsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RequestStatus>> GetStatusesAsync(CancellationToken ct = default);
    Task MarkNotificationsReadAsync(Guid userId, CancellationToken ct = default);
}

public interface ISlaService
{
    Task<(DateTime Deadline, int? PolicyId)> CalculateDeadlineAsync(int categoryId, FlowDesk.Web.Domain.RequestPriority priority, CancellationToken ct = default);
    Task<SlaStatusDto> GetSlaStatusAsync(Guid requestId, CancellationToken ct = default);
}

public interface INotificationService
{
    Task SendStatusChangedAsync(Guid requestId, string newStatus, CancellationToken ct = default);
    Task SendNewRequestToDispatcherAsync(Guid requestId, CancellationToken ct = default);
    Task SendAssignmentToExecutorAsync(Guid requestId, Guid executorId, CancellationToken ct = default);
    Task SendCommentAsync(Guid requestId, Guid authorId, string text, CancellationToken ct = default);
}

public interface IReportService
{
    Task<ReportSummaryDto> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<byte[]> ExportPdfAsync(DateTime from, DateTime to, CancellationToken ct = default);
}

public interface IAdminService
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateCategoryAsync(SaveCategoryDto dto, CancellationToken ct = default);
    Task UpdateCategoryAsync(int id, SaveCategoryDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<SlaPolicyDto>> GetSlaPoliciesAsync(CancellationToken ct = default);
    Task<SlaPolicyDto> CreateSlaPolicyAsync(SaveSlaPolicyDto dto, CancellationToken ct = default);
    Task UpdateSlaPolicyAsync(int id, SaveSlaPolicyDto dto, CancellationToken ct = default);
    Task DeleteSlaPolicyAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AssignmentRuleDto>> GetAssignmentRulesAsync(CancellationToken ct = default);
    Task<AssignmentRuleDto> CreateAssignmentRuleAsync(SaveAssignmentRuleDto dto, CancellationToken ct = default);
    Task DeleteAssignmentRuleAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default);
    Task SetUserActiveAsync(Guid userId, bool isActive, CancellationToken ct = default);
}

public interface IFileStorage
{
    Task<(string StoredName, long Size)> SaveAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default);
    Stream OpenRead(string storedName);
    string GetPath(string storedName);
    Task DeleteAsync(string storedName, CancellationToken ct = default);
}

public interface IAppEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
