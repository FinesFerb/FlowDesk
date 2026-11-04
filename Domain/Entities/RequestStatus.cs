namespace FlowDesk.Web.Domain.Entities;

public class RequestStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<ServiceRequest> Requests { get; set; } = [];
}
