using System.Security.Claims;
using System.Text.Encodings.Web;
using FlowDesk.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowDesk.Tests.TestSupport;

/// <summary>
/// Схема аутентификации для интеграционных тестов: пользователь и роли задаются
/// заголовком X-Testing-Auth в формате «email|Роль1,Роль2».
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Testing";
    public const string HeaderName = "X-Testing-Auth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = header.ToString().Split('|', 2);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        using var scope = Context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDeskDbContext>();
        var email = parts[0].Trim();
        var user = db.Users.AsNoTracking().FirstOrDefault(x => x.Email == email);
        if (user is null)
        {
            throw new InvalidOperationException($"Тестовый пользователь {email} не найден в БД.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Email, email)
        };
        claims.AddRange(parts[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
