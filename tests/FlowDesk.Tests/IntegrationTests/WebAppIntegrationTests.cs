using System.Net;
using FlowDesk.Tests.TestSupport;
using FlowDesk.Web.Application;
using FlowDesk.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowDesk.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты: ASP.NET Core хост + Blazor + DI-сервисы + SQLite in-memory
/// с демо-данными штатного DbSeeder. Аутентификация эмулируется заголовком
/// X-Testing-Auth (email|роли).
/// </summary>
public class WebAppIntegrationTests(FlowDeskWebAppFactory factory) : IClassFixture<FlowDeskWebAppFactory>
{
    [Fact]
    public async Task Health_Endpoint_ReturnsOkForAnonymous()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReportsCsv_RequiresAuthentication_AndServesCsvForManager()
    {
        var client = factory.CreateClient();

        // Без заголовка аутентификации доступ запрещён.
        var anonymous = await client.GetAsync("/api/reports/csv");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Диспетчер не входит в Manager/Admin — тоже запрещено.
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, "dispatcher@flowdesk.local|Dispatcher");
        var forbidden = await client.GetAsync("/api/reports/csv");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Менеджер получает валидный CSV-отчёт.
        client.DefaultRequestHeaders.Remove(TestAuthHandler.HeaderName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, "manager@flowdesk.local|Manager");
        var ok = await client.GetAsync("/api/reports/csv");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("text/csv", ok.Content.Headers.ContentType!.MediaType);
        var text = System.Text.Encoding.UTF8.GetString(await ok.Content.ReadAsByteArrayAsync()).TrimStart('\uFEFF');
        Assert.StartsWith("FlowDesk — отчёт по заявкам", text);
        Assert.Contains("Всего заявок;", text);
        Assert.Contains("По категориям", text);
    }

    [Fact]
    public async Task AttachmentDownload_OnlyForParticipantsViaHttp()
    {
        // Вложение добавляется штатным сервисом приложения в реальное файловое хранилище.
        Guid attachmentId;
        Guid ownerId;
        using (var scope = factory.CreateServicesScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
            var requestService = scope.ServiceProvider.GetRequiredService<IRequestService>();

            var owner = db.Users.AsNoTracking().First(x => x.Email == "client@flowdesk.local");
            ownerId = owner.Id;
            var request = db.Requests.AsNoTracking().First(x => x.Title == "Не включается офисный принтер");

            var content = System.Text.Encoding.UTF8.GetBytes("Лог диагностики принтера из интеграционного теста.");
            using var file = new MemoryStream(content);
            var attachment = await requestService.AddAttachmentAsync(
                request.Id, owner.Id, file, "diagnostics.txt", "text/plain");
            attachmentId = attachment.Id;
        }

        var client = factory.CreateClient();

        // Анонимный доступ запрещён.
        var anonymous = await client.GetAsync($"/api/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Чужой клиент не имеет доступа к заявке — 404 (не раскрываем существование файла).
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, "client2@flowdesk.local|Client");
        var foreign = await client.GetAsync($"/api/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // Владелец заявки скачивает файл.
        client.DefaultRequestHeaders.Remove(TestAuthHandler.HeaderName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, "client@flowdesk.local|Client");
        var ok = await client.GetAsync($"/api/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("text/plain", ok.Content.Headers.ContentType!.MediaType);
        Assert.Contains("diagnostics.txt", ok.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal("Лог диагностики принтера из интеграционного теста.",
            await ok.Content.ReadAsStringAsync());
    }
}
