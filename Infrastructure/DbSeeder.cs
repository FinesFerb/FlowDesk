using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Web.Infrastructure;

public static class DbSeeder
{
    public const string DemoPassword = "Passw0rd!";

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync();

        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                ThrowIfFailed(result, $"Создание роли {role}");
            }
        }

        var statuses = await SeedStatusesAsync(db);
        var categories = await SeedCategoriesAsync(db);
        await SeedSlaPoliciesAsync(db, categories);

        var users = new Dictionary<string, ApplicationUser>(StringComparer.OrdinalIgnoreCase);
        users["client"] = await EnsureUserAsync(userManager, "client@flowdesk.local", "Иван Клиентов", RoleNames.Client);
        users["client2"] = await EnsureUserAsync(userManager, "client2@flowdesk.local", "Ольга Заказчикова", RoleNames.Client);
        users["dispatcher"] = await EnsureUserAsync(userManager, "dispatcher@flowdesk.local", "Дарья Диспетчерова", RoleNames.Dispatcher);
        users["executor"] = await EnsureUserAsync(userManager, "executor@flowdesk.local", "Пётр Мастеров", RoleNames.Executor);
        users["executor2"] = await EnsureUserAsync(userManager, "executor2@flowdesk.local", "Анна Инженерова", RoleNames.Executor);
        users["manager"] = await EnsureUserAsync(userManager, "manager@flowdesk.local", "Мария Руководителева", RoleNames.Manager);
        users["admin"] = await EnsureUserAsync(userManager, "admin@flowdesk.local", "Алексей Админов", RoleNames.Admin);

        await SeedAssignmentRulesAsync(db, categories, users);
        await SeedRequestsAsync(db, statuses, categories, users);
    }

    private static async Task<Dictionary<string, RequestStatus>> SeedStatusesAsync(FlowDeskDbContext db)
    {
        var desired = new[]
        {
            new RequestStatus { Name = "Новая", SortOrder = 1 },
            new RequestStatus { Name = "Назначена", SortOrder = 2 },
            new RequestStatus { Name = "В работе", SortOrder = 3 },
            new RequestStatus { Name = "Выполнено", SortOrder = 4 },
            new RequestStatus { Name = "Отклонено", SortOrder = 5 }
        };

        foreach (var status in desired)
        {
            if (!await db.RequestStatuses.AnyAsync(x => x.Name == status.Name))
            {
                db.RequestStatuses.Add(status);
            }
        }
        await db.SaveChangesAsync();
        return await db.RequestStatuses.ToDictionaryAsync(x => x.Name);
    }

    private static async Task<Dictionary<string, Category>> SeedCategoriesAsync(FlowDeskDbContext db)
    {
        var desired = new[]
        {
            new Category { Name = "Ремонт техники", Description = "Диагностика и ремонт оборудования", DefaultSlaHours = 24 },
            new Category { Name = "IT-поддержка", Description = "Настройка ПО, доступы, рабочие места", DefaultSlaHours = 12 },
            new Category { Name = "Клининг", Description = "Уборка помещений и прилегающих территорий", DefaultSlaHours = 48 },
            new Category { Name = "Техническое обслуживание", Description = "Плановое обслуживание и профилактика", DefaultSlaHours = 72 }
        };

        foreach (var category in desired)
        {
            if (!await db.Categories.AnyAsync(x => x.Name == category.Name))
            {
                db.Categories.Add(category);
            }
        }
        await db.SaveChangesAsync();
        return await db.Categories.ToDictionaryAsync(x => x.Name);
    }

    private static async Task SeedSlaPoliciesAsync(FlowDeskDbContext db, Dictionary<string, Category> categories)
    {
        foreach (var category in categories.Values)
        {
            foreach (var priority in Enum.GetValues<RequestPriority>())
            {
                if (await db.SlaPolicies.AnyAsync(x => x.CategoryId == category.Id && x.Priority == priority))
                {
                    continue;
                }

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
        await db.SaveChangesAsync();
    }

    private static async Task SeedAssignmentRulesAsync(
        FlowDeskDbContext db,
        Dictionary<string, Category> categories,
        Dictionary<string, ApplicationUser> users)
    {
        if (await db.AssignmentRules.AnyAsync())
        {
            return;
        }

        var rules = new[]
        {
            new AssignmentRule { CategoryId = categories["Ремонт техники"].Id, ExecutorId = users["executor"].Id, SortOrder = 1 },
            new AssignmentRule { CategoryId = categories["IT-поддержка"].Id, ExecutorId = users["executor2"].Id, SortOrder = 1 },
            new AssignmentRule { CategoryId = categories["Клининг"].Id, ExecutorId = users["executor"].Id, SortOrder = 1 },
            new AssignmentRule { CategoryId = categories["Техническое обслуживание"].Id, ExecutorId = users["executor2"].Id, SortOrder = 1 }
        };
        db.AssignmentRules.AddRange(rules);
        await db.SaveChangesAsync();
    }

    private static async Task SeedRequestsAsync(
        FlowDeskDbContext db,
        Dictionary<string, RequestStatus> statuses,
        Dictionary<string, Category> categories,
        Dictionary<string, ApplicationUser> users)
    {
        if (await db.Requests.AnyAsync())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var request1 = new ServiceRequest
        {
            Title = "Не включается офисный принтер",
            Description = "Принтер в бухгалтерии не реагирует на кнопку питания. Проверка кабеля не помогла.",
            CategoryId = categories["Ремонт техники"].Id,
            StatusId = statuses["Новая"].Id,
            Priority = RequestPriority.High,
            ClientId = users["client"].Id,
            SlaDeadline = now.AddHours(6),
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-2)
        };
        var request2 = new ServiceRequest
        {
            Title = "Нужен доступ к корпоративной CRM",
            Description = "Новый сотрудник не может войти в CRM. Требуется создать учётную запись и выдать роль менеджера.",
            CategoryId = categories["IT-поддержка"].Id,
            StatusId = statuses["В работе"].Id,
            Priority = RequestPriority.Critical,
            ClientId = users["client"].Id,
            ExecutorId = users["executor2"].Id,
            DispatcherId = users["dispatcher"].Id,
            SlaDeadline = now.AddHours(1.5),
            CreatedAt = now.AddHours(-5),
            UpdatedAt = now.AddHours(-1)
        };
        var request3 = new ServiceRequest
        {
            Title = "Генеральная уборка переговорной",
            Description = "Требуется уборка после ремонта: пыль, следы краски на полу.",
            CategoryId = categories["Клининг"].Id,
            StatusId = statuses["Выполнено"].Id,
            Priority = RequestPriority.Normal,
            ClientId = users["client2"].Id,
            ExecutorId = users["executor"].Id,
            DispatcherId = users["dispatcher"].Id,
            SlaDeadline = now.AddDays(-1),
            CreatedAt = now.AddDays(-3),
            UpdatedAt = now.AddDays(-2),
            ClosedAt = now.AddDays(-2)
        };
        var request4 = new ServiceRequest
        {
            Title = "Плановое обслуживание кондиционеров",
            Description = "Провести сезонную чистку и диагностику двух кондиционеров в офисе.",
            CategoryId = categories["Техническое обслуживание"].Id,
            StatusId = statuses["Назначена"].Id,
            Priority = RequestPriority.Low,
            ClientId = users["client2"].Id,
            ExecutorId = users["executor2"].Id,
            DispatcherId = users["dispatcher"].Id,
            SlaDeadline = now.AddHours(-3),
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddHours(-3)
        };

        db.Requests.AddRange(request1, request2, request3, request4);
        await db.SaveChangesAsync();

        db.Comments.AddRange(
            new Comment
            {
                RequestId = request2.Id,
                AuthorId = users["client"].Id,
                Text = "Доступ нужен до конца дня, сотрудник выходит завтра.",
                CreatedAt = now.AddHours(-4)
            },
            new Comment
            {
                RequestId = request2.Id,
                AuthorId = users["executor2"].Id,
                Text = "Учётная запись создана, отправляю инструкцию по первому входу.",
                CreatedAt = now.AddHours(-1)
            });

        db.AuditLogs.AddRange(
            new AuditLog { RequestId = request1.Id, UserId = users["client"].Id, Action = "Заявка создана", NewValue = "Статус: Новая", CreatedAt = now.AddHours(-2) },
            new AuditLog { RequestId = request2.Id, UserId = users["dispatcher"].Id, Action = "Назначен исполнитель", OldValue = "Статус: Новая", NewValue = "Статус: Назначена; исполнитель: Анна Инженерова", CreatedAt = now.AddHours(-3) },
            new AuditLog { RequestId = request2.Id, UserId = users["executor2"].Id, Action = "Изменён статус", OldValue = "Назначена", NewValue = "В работе", CreatedAt = now.AddHours(-1) },
            new AuditLog { RequestId = request3.Id, UserId = users["executor"].Id, Action = "Изменён статус", OldValue = "В работе", NewValue = "Выполнено", CreatedAt = now.AddDays(-2) });

        db.Notifications.Add(new Notification
        {
            UserId = users["dispatcher"].Id,
            RequestId = request1.Id,
            Type = "new_request",
            Message = "Новая заявка «Не включается офисный принтер» в категории «Ремонт техники».",
            CreatedAt = now.AddHours(-2)
        });

        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string role)
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
            var result = await userManager.CreateAsync(user, DemoPassword);
            ThrowIfFailed(result, $"Создание пользователя {email}");
        }
        else if (string.IsNullOrWhiteSpace(user.FullName))
        {
            user.FullName = fullName;
            await userManager.UpdateAsync(user);
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
