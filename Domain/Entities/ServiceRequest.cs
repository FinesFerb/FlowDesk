using FlowDesk.Web.Data;

namespace FlowDesk.Web.Domain.Entities;

public class ServiceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public int StatusId { get; set; }
    public RequestStatus Status { get; set; } = null!;

    public RequestPriority Priority { get; set; } = RequestPriority.Normal;

    public Guid ClientId { get; set; }
    public ApplicationUser Client { get; set; } = null!;

    public Guid? ExecutorId { get; set; }
    public ApplicationUser? Executor { get; set; }

    public Guid? DispatcherId { get; set; }
    public ApplicationUser? Dispatcher { get; set; }

    public int? SlaPolicyId { get; set; }
    public SlaPolicy? SlaPolicy { get; set; }

    public DateTime? SlaDeadline { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public ICollection<Comment> Comments { get; set; } = [];
    public ICollection<Attachment> Attachments { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
