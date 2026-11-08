using FlowDesk.Tests.TestSupport;
using FlowDesk.Web.Application;
using FlowDesk.Web.Application.Contracts;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.UnitTests;

public class NotificationServiceTests : ServiceTestBase
{
    [Fact]
    public async Task SendComment_NotifiesParticipantsExceptAuthor()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "IT-поддержка");

        // Клиент создаёт заявку, диспетчер назначает исполнителя — у заявки три участника.
        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Ошибка в отчётной системе",
                Description = "Не формируется отчёт за месяц.",
                CategoryId = category.Id,
                Priority = RequestPriority.High
            },
            Users["client"].Id);
        await requestService.AssignExecutorAsync(created.Id, Users["executor"].Id, DateTime.UtcNow.AddHours(4), Users["dispatcher"].Id);

        EmailSender.Messages.Clear();

        // Комментарий оставляет исполнитель — уведомления получают клиент и диспетчер.
        await requestService.AddCommentAsync(created.Id, Users["executor"].Id, "Проверяю проблему, нужна информация.");

        using var verifyScope = Provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();

        var notifications = await verifyDb.Notifications
            .Where(x => x.RequestId == created.Id && x.Type == "comment")
            .ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, x => x.UserId == Users["client"].Id);
        Assert.Contains(notifications, x => x.UserId == Users["dispatcher"].Id);
        Assert.DoesNotContain(notifications, x => x.UserId == Users["executor"].Id);

        Assert.Equal(2, EmailSender.Messages.Count);
        Assert.All(EmailSender.Messages, x => Assert.Equal("FlowDesk: новый комментарий", x.Subject));
        Assert.Contains(EmailSender.Messages, x => x.To == Users["client"].Email);
        Assert.Contains(EmailSender.Messages, x => x.To == Users["dispatcher"].Email);
        Assert.DoesNotContain(EmailSender.Messages, x => x.To == Users["executor"].Email);
        Assert.Contains("Новый комментарий к заявке", EmailSender.Messages[0].Body);
    }

    [Fact]
    public async Task SendStatusChanged_InactiveUserIsSkipped()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();
        var category = await db.Categories.FirstAsync(x => x.Name == "Ремонт техники");

        var created = await requestService.CreateRequestAsync(
            new CreateRequestDto
            {
                Title = "Заявка неактивного клиента",
                Description = "Клиент деактивирован администратором.",
                CategoryId = category.Id,
                Priority = RequestPriority.Normal
            },
            Users["client2"].Id);

        var user = await db.Users.FindAsync(Users["client2"].Id);
        user!.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EmailSender.Messages.Clear();
        await notificationService.SendStatusChangedAsync(created.Id, "В работе");

        Assert.Empty(EmailSender.Messages);
        Assert.Empty(await db.Notifications
            .Where(x => x.RequestId == created.Id && x.Type == "status_changed")
            .ToListAsync());
    }
}
