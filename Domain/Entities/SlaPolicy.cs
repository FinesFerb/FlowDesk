namespace FlowDesk.Web.Domain.Entities;

public class SlaPolicy
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public RequestPriority Priority { get; set; }
    public int Hours { get; set; }

    public ICollection<ServiceRequest> Requests { get; set; } = [];
}
