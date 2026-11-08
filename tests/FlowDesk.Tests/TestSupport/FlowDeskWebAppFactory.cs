using FlowDesk.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowDesk.Tests.TestSupport;

/// <summary>
/// Фабрика веб-приложения для интеграционных тестов: PostgreSQL заменяется
/// на изолированную SQLite in-memory БД, схема создаётся через EnsureCreated
/// и наполняется демо-данными штатным DbSeeder.
/// </summary>
public class FlowDeskWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Локальное файловое хранилище в отдельной временной папке.
        builder.UseSetting("FileStorage:Root", Path.Combine(
            Path.GetTempPath(), "flowdesk-int-tests", Guid.NewGuid().ToString("N")));

        // ConfigureTestServices выполняется после всех регистраций Program.cs —
        // поэтому Npgsql-регистрация удаляется до добавления SQLite.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<FlowDeskDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FlowDeskDbContext>>();
            services.AddDbContext<FlowDeskDbContext>(options => options.UseSqlite(_connection));

            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Первый запрос запускает хост: Program выполнит миграции-он-провайдер
        // (EnsureCreated для SQLite) и наполнит БД через DbSeeder.
        using var client = CreateDefaultClient();
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public IServiceScope CreateServicesScope() => Services.CreateScope();
}
