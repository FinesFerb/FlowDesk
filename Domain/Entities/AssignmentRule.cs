using FlowDesk.Web.Data;

namespace FlowDesk.Web.Domain.Entities;

public class AssignmentRule
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public Guid ExecutorId { get; set; }
    public ApplicationUser Executor { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
