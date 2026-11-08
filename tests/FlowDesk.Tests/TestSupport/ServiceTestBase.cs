using FlowDesk.Web.Data;
using FlowDesk.Web.Domain.Entities;
using FlowDesk.Web.Application;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.TestSupport;

/// <summary>
/// База модульных тестов сервисов: изолированная SQLite in-memory БД на каждый тест,
/// минимальный справочный набор данных, доступ к фиктивным зависимостям.
/// </summary>
public abstract class ServiceTestBase : IAsyncLifetime
{
    protected SqliteConnection Connection { get; private set; } = null!;
    protected ServiceProvider Provider { get; private set; } = null!;
    protected RecordingEmailSender EmailSender { get; private set; } = null!;
    protected FakeFileStorage FileStorage { get; private set; } = null!;
    protected Dictionary<string, ApplicationUser> Users { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        Provider = TestHost.BuildProvider(Connection);
        EmailSender = (RecordingEmailSender)Provider.GetRequiredService<IAppEmailSender>();
        FileStorage = (FakeFileStorage)Provider.GetRequiredService<IFileStorage>();

        Users = await TestSeed.SeedAsync(Provider);
    }

    public virtual async Task DisposeAsync()
    {
        await Provider.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
