namespace FlowDesk.Web.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DefaultSlaHours { get; set; } = 24;

    public ICollection<ServiceRequest> Requests { get; set; } = [];
    public ICollection<SlaPolicy> SlaPolicies { get; set; } = [];
    public ICollection<AssignmentRule> AssignmentRules { get; set; } = [];
}
