using FlowDesk.Web.Application;
using FlowDesk.Web.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using MimeKit;

namespace FlowDesk.Web.Infrastructure;

public class EmailSender(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<EmailSender> logger) : IEmailSender<ApplicationUser>, IAppEmailSender
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        => SendAsync(email, "FlowDesk: подтверждение email",
            $"<p>Здравствуйте, {user.FullName}!</p><p>Подтвердите email: <a href=\"{confirmationLink}\">ссылка</a></p>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        => SendAsync(email, "FlowDesk: сброс пароля",
            $"<p>Здравствуйте, {user.FullName}!</p><p>Сбросить пароль: <a href=\"{resetLink}\">ссылка</a></p>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        => SendAsync(email, "FlowDesk: код сброса пароля",
            $"<p>Здравствуйте, {user.FullName}!</p><p>Код сброса пароля: <b>{resetCode}</b></p>");

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var host = configuration["Email:Host"];
        var fromAddress = configuration["Email:From"] ?? "no-reply@flowdesk.local";
        var fromName = configuration["Email:FromName"] ?? "FlowDesk";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        if (string.IsNullOrWhiteSpace(host))
        {
            await SaveToPickupAsync(message, htmlBody, ct);
            return;
        }

        using var client = new SmtpClient();
        var port = configuration.GetValue("Email:Port", 587);
        var useStartTls = configuration.GetValue("Email:UseStartTls", true);
        await client.ConnectAsync(host, port, useStartTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None, ct);

        var userName = configuration["Email:UserName"];
        if (!string.IsNullOrWhiteSpace(userName))
        {
            await client.AuthenticateAsync(userName, configuration["Email:Password"] ?? string.Empty, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        logger.LogInformation("Email '{Subject}' отправлен на {To}", subject, to);
    }

    private async Task SaveToPickupAsync(MimeMessage message, string htmlBody, CancellationToken ct)
    {
        var configuredRoot = configuration["Email:PickupDirectory"] ?? "App_Data/emails";
        var root = Path.Combine(environment.ContentRootPath, configuredRoot);
        Directory.CreateDirectory(root);

        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.html";
        var path = Path.Combine(root, fileName);
        var content = $"""
            <!-- To: {message.To} -->
            <!-- Subject: {message.Subject} -->
            {htmlBody}
            """;
        await File.WriteAllTextAsync(path, content, ct);
        logger.LogInformation("SMTP не настроен. Email сохранён в {Path}", path);
    }
}
