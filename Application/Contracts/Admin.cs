using System.ComponentModel.DataAnnotations;
using FlowDesk.Web.Domain;

namespace FlowDesk.Web.Application.Contracts;

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DefaultSlaHours { get; set; }
}

public class SaveCategoryDto
{
    [Required(ErrorMessage = "Укажите название")]
    [StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(1, 720, ErrorMessage = "SLA должно быть от 1 до 720 часов")]
    public int DefaultSlaHours { get; set; } = 24;
}

public class SlaPolicyDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public RequestPriority Priority { get; set; }
    public int Hours { get; set; }
}

public class SaveSlaPolicyDto
{
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    public RequestPriority Priority { get; set; } = RequestPriority.Normal;

    [Range(1, 720, ErrorMessage = "SLA должно быть от 1 до 720 часов")]
    public int Hours { get; set; } = 24;
}

public class AssignmentRuleDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public Guid ExecutorId { get; set; }
    public string ExecutorName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public class SaveAssignmentRuleDto
{
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    public Guid ExecutorId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}
