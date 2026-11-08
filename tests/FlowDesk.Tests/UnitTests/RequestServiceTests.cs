using System.Text;
using FlowDesk.Tests.TestSupport;
using FlowDesk.Web.Application;
using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.UnitTests;

public class RequestServiceTests : ServiceTestBase
{
    [Fact]
    public async Task CreateRequest_CreatesWithNewStatusDeadlineAuditAndNotification()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "Ремонт техники");

        var started = DateTime.UtcNow;
        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Монитор не включается",
                Description = "Офисный монитор не подаёт признаков жизни.",
                CategoryId = category.Id,
                Priority = RequestPriority.High
            },
            Users["client"].Id);

        // Результат запроса: статус «Новая», SLA по политике High = 8 ч, клиент подставлен из контекста.
        Assert.Equal("Новая", created.StatusName);
        Assert.Equal("Иван Клиентов", created.ClientName);
        Assert.Equal(SlaStates.OnTrack, created.SlaState);
        Assert.InRange(created.SlaDeadline!.Value.Ticks, started.AddHours(8).AddSeconds(-5).Ticks, DateTime.UtcNow.AddHours(8).AddSeconds(5).Ticks);

        // Проверка записи в БД.
        using var verifyScope = Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var saved = await verifyDb.Requests.Include(x => x.Status).FirstAsync(x => x.Id == created.Id);
        Assert.Equal("Новая", saved.Status.Name);
        Assert.True(await verifyDb.AuditLogs.AnyAsync(x => x.RequestId == created.Id && x.Action == "Заявка создана"));

        // Уведомление диспетчеру о новой заявке.
        var notification = await verifyDb.Notifications.SingleAsync(x => x.Type == "new_request");
        Assert.Equal(Users["dispatcher"].Id, notification.UserId);
        Assert.Equal(created.Id, notification.RequestId);
        var email = Assert.Single(EmailSender.Messages);
        Assert.Equal(Users["dispatcher"].Email, email.To);
        Assert.Equal("FlowDesk: новая заявка", email.Subject);
        Assert.Contains(created.Title, email.Body);
    }

    [Fact]
    public async Task CreateRequest_UnknownCategory_Throws()
    {
        using var scope = Provider.CreateScope();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();

        var dto = new CreateRequestDto
        {
            Title = "Заявка без категории",
            Description = "Несуществующая категория.",
            CategoryId = 999999,
            Priority = RequestPriority.Normal
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => requestService.CreateRequestAsync(dto, Users["client"].Id));
        Assert.Equal("Категория не найдена.", exception.Message);
        Assert.Empty(EmailSender.Messages);
    }

    [Fact]
    public async Task AssignExecutor_SetsStatusExecutorDeadlineAndAudit()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "IT-поддержка");

        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Не работает VPN",
                Description = "Нет подключения к корпоративной сети.",
                CategoryId = category.Id,
                Priority = RequestPriority.Normal
            },
            Users["client"].Id);

        var deadline = DateTime.UtcNow.AddHours(6);
        await requestService.AssignExecutorAsync(created.Id, Users["executor"].Id, deadline, Users["dispatcher"].Id);

        using var verifyScope = Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var assigned = await verifyDb.Requests
            .Include(x => x.Status)
            .Include(x => x.Executor)
            .FirstAsync(x => x.Id == created.Id);

        Assert.Equal("Назначена", assigned.Status.Name);
        Assert.Equal(Users["executor"].Id, assigned.ExecutorId);
        Assert.Equal(Users["dispatcher"].Id, assigned.DispatcherId);
        Assert.Equal(deadline, assigned.SlaDeadline);
        Assert.True(await verifyDb.AuditLogs.AnyAsync(x => x.RequestId == created.Id && x.Action == "Назначен исполнитель"));

        var assignmentNotification = await verifyDb.Notifications.SingleAsync(x => x.Type == "assignment");
        Assert.Equal(Users["executor"].Id, assignmentNotification.UserId);
        Assert.Equal(Users["client"].Id, Assert.Single(verifyDb.Notifications.Where(x => x.Type == "status_changed")).UserId);
        Assert.Contains(EmailSender.Messages, x => x.Subject == "FlowDesk: назначена заявка" && x.To == Users["executor"].Email);
    }

    [Fact]
    public async Task AssignExecutor_UserWithoutExecutorRole_Throws()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "IT-поддержка");

        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Проблема со сканером",
                Description = "Сканер не распознаётся системой.",
                CategoryId = category.Id,
                Priority = RequestPriority.Low
            },
            Users["client"].Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => requestService.AssignExecutorAsync(
                created.Id, Users["client2"].Id, DateTime.UtcNow.AddHours(4), Users["dispatcher"].Id));
        Assert.Contains("не является исполнителем", exception.Message);
    }

    [Fact]
    public async Task UpdateStatus_TransitionsToCompleted_SetsClosedAt()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "IT-поддержка");

        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Установить драйвер принтера",
                Description = "Принтер не печатает после обновления Windows.",
                CategoryId = category.Id,
                Priority = RequestPriority.Normal
            },
            Users["client"].Id);
        await requestService.AssignExecutorAsync(created.Id, Users["executor2"].Id, DateTime.UtcNow.AddHours(5), Users["dispatcher"].Id);

        var statuses = await requestService.GetStatusesAsync();
        await requestService.UpdateStatusAsync(created.Id, statuses.First(x => x.Name == "В работе").Id, Users["executor2"].Id);
        await requestService.UpdateStatusAsync(created.Id, statuses.First(x => x.Name == "Выполнено").Id, Users["executor2"].Id);

        using var verifyScope = Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var completed = await verifyDb.Requests.Include(x => x.Status).FirstAsync(x => x.Id == created.Id);

        Assert.Equal("Выполнено", completed.Status.Name);
        Assert.NotNull(completed.ClosedAt);
        Assert.Equal(Users["executor2"].Id, completed.ExecutorId);
        Assert.True(await verifyDb.AuditLogs.AnyAsync(x => x.RequestId == created.Id && x.Action == "Изменён статус"));
    }

    [Fact]
    public async Task UpdateStatus_ByOutOfScopeClient_ThrowsUnauthorized()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "IT-поддержка");

        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Заявка чужого клиента",
                Description = "Попытка изменить чужую заявку.",
                CategoryId = category.Id,
                Priority = RequestPriority.Normal
            },
            Users["client"].Id);

        var statuses = await requestService.GetStatusesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => requestService.UpdateStatusAsync(created.Id, statuses.First(x => x.Name == "В работе").Id, Users["client2"].Id));
    }

    [Fact]
    public async Task AddAttachment_SavesFileMetadataAndAudit()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "Ремонт техники");

        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Скриншот ошибки",
                Description = "Приложен скриншот проблемы.",
                CategoryId = category.Id,
                Priority = RequestPriority.Normal
            },
            Users["client"].Id);

        var content = Encoding.UTF8.GetBytes("Скриншот ошибки, содержимое тестового файла.");
        await using var file = new MemoryStream(content);
        var attachment = await requestService.AddAttachmentAsync(
            created.Id, Users["client"].Id, file, "screenshot.png", "image/png");

        Assert.Equal("screenshot.png", attachment.FileName);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(content.Length, attachment.FileSize);
        Assert.Equal("Иван Клиентов", attachment.UploadedByName);

        Assert.Single(FileStorage.Files);
        var (storedName, storedBytes) = FileStorage.Files.First();
        Assert.Equal(".png", Path.GetExtension(storedName));
        Assert.Equal(content, storedBytes);

        using var verifyScope = Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        Assert.True(await verifyDb.Attachments.AnyAsync(x => x.Id == attachment.Id && x.StoredName == storedName));
        Assert.True(await verifyDb.AuditLogs.AnyAsync(x => x.RequestId == created.Id && x.Action == "Добавлено вложение"));
    }

    [Fact]
    public async Task GetAttachment_ForeignClientGetsNull_ManagerGetsAccess()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "Ремонт техники");

        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Документ к заявке",
                Description = "Проверка разграничения доступа.",
                CategoryId = category.Id,
                Priority = RequestPriority.Normal
            },
            Users["client"].Id);
        await using var file = new MemoryStream([1, 2, 3, 4]);
        var attachment = await requestService.AddAttachmentAsync(
            created.Id, Users["client"].Id, file, "doc.txt", "text/plain");

        Assert.Null(await requestService.GetAttachmentAsync(attachment.Id, Users["client2"].Id, RoleNames.Client));
        Assert.Null(await requestService.GetAttachmentAsync(attachment.Id, Users["client2"].Id, RoleNames.Executor));

        var managerDownload = await requestService.GetAttachmentAsync(attachment.Id, Users["manager"].Id, RoleNames.Manager);
        Assert.NotNull(managerDownload);
        Assert.Equal("doc.txt", managerDownload.FileName);
        Assert.Equal("text/plain", managerDownload.ContentType);
    }
}
