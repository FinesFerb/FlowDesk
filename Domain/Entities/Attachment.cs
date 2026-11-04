using FlowDesk.Web.Data;

namespace FlowDesk.Web.Domain.Entities;

public class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public ServiceRequest Request { get; set; } = null!;

    public Guid UploadedById { get; set; }
    public ApplicationUser UploadedBy { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string StoredName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
