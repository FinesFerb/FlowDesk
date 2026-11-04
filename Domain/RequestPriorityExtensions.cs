namespace FlowDesk.Web.Domain;

public static class RequestPriorityExtensions
{
    public static string ToRussian(this RequestPriority priority) => priority switch
    {
        RequestPriority.Low => "Низкий",
        RequestPriority.Normal => "Обычный",
        RequestPriority.High => "Высокий",
        RequestPriority.Critical => "Критический",
        _ => priority.ToString()
    };
}
