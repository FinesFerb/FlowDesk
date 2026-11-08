using FlowDesk.Web.Application;
using FlowDesk.Web.Application.Services;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.TestSupport;

/// <summary>Сборка DI-контейнера для модульных тестов: SQLite in-memory + фиктивные инфраструктурные зависимости.</summary>
public static class TestHost
{
    public const string ProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    public static ServiceProvider BuildProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<FlowDeskDbContext>(options => options.UseSqlite(connection));
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<FlowDeskDbContext>();

        services.AddScoped<ISlaService, SlaService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAdminService, AdminService>();

        services.AddSingleton<IAppEmailSender, RecordingEmailSender>();
        services.AddSingleton<IFileStorage, FakeFileStorage>();

        return services.BuildServiceProvider();
    }
}

/// <summary>Минимальный справочный набор данных для модульных тестов (без демо-заявок).</summary>
public static class TestSeed
{
    public static async Task<Dictionary<string, ApplicationUser>> SeedAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FlowDeskDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.EnsureCreatedAsync();

        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                ThrowIfFailed(result, $"Создание роли {role}");
            }
        }

        var statusNames = new[] { "Новая", "Назначена", "В работе", "Выполнено", "Отклонено" };
        for (var i = 0; i < statusNames.Length; i++)
        {
            if (!await db.RequestStatuses.AnyAsync(x => x.Name == statusNames[i]))
            {
                db.RequestStatuses.Add(new RequestStatus { Name = statusNames[i], SortOrder = i + 1 });
            }
        }

        if (!await db.Categories.AnyAsync(x => x.Name == "Ремонт техники"))
        {
            db.Categories.Add(new Category { Name = "Ремонт техники", Description = "Диагностика и ремонт оборудования", DefaultSlaHours = 24 });
        }
        if (!await db.Categories.AnyAsync(x => x.Name == "IT-поддержка"))
        {
            db.Categories.Add(new Category { Name = "IT-поддержка", Description = "Настройка ПО, доступы, рабочие места", DefaultSlaHours = 12 });
        }
        await db.SaveChangesAsync();

        var categories = await db.Categories.ToDictionaryAsync(x => x.Name);
        foreach (var category in categories.Values)
        {
            foreach (var priority in Enum.GetValues<RequestPriority>())
            {
                if (!await db.SlaPolicies.AnyAsync(x => x.CategoryId == category.Id && x.Priority == priority))
                {
                    db.SlaPolicies.Add(new SlaPolicy
                    {
                        CategoryId = category.Id,
                        Priority = priority,
                        Hours = priority switch
                        {
                            RequestPriority.Critical => 4,
                            RequestPriority.High => 8,
                            RequestPriority.Normal => 24,
                            _ => 48
                        }
                    });
                }
            }
        }
        await db.SaveChangesAsync();

        var users = new Dictionary<string, ApplicationUser>(StringComparer.OrdinalIgnoreCase)
        {
            ["client"] = await EnsureUserAsync(userManager, "client@flowdesk.local", "Иван Клиентов", RoleNames.Client),
            ["client2"] = await EnsureUserAsync(userManager, "client2@flowdesk.local", "Ольга Заказчикова", RoleNames.Client),
            ["dispatcher"] = await EnsureUserAsync(userManager, "dispatcher@flowdesk.local", "Дарья Диспетчерова", RoleNames.Dispatcher),
            ["executor"] = await EnsureUserAsync(userManager, "executor@flowdesk.local", "Пётр Мастеров", RoleNames.Executor),
            ["executor2"] = await EnsureUserAsync(userManager, "executor2@flowdesk.local", "Анна Инженерова", RoleNames.Executor),
            ["manager"] = await EnsureUserAsync(userManager, "manager@flowdesk.local", "Мария Руководителева", RoleNames.Manager),
            ["admin"] = await EnsureUserAsync(userManager, "admin@flowdesk.local", "Алексей Админов", RoleNames.Admin)
        };

        return users;
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager, string email, string fullName, string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, "Passw0rd!");
            ThrowIfFailed(result, $"Создание пользователя {email}");
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var result = await userManager.AddToRoleAsync(user, role);
            ThrowIfFailed(result, $"Назначение роли {role} пользователю {email}");
        }

        return user;
    }

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{operation}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
        }
    }
}
