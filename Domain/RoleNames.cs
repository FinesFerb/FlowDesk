namespace FlowDesk.Web.Domain;

public static class RoleNames
{
    public const string Client = "Client";
    public const string Dispatcher = "Dispatcher";
    public const string Executor = "Executor";
    public const string Manager = "Manager";
    public const string Admin = "Admin";

    public static readonly string[] All = [Client, Dispatcher, Executor, Manager, Admin];
}
