using System.Security.Claims;
using FlowDesk.Web.Application;
using FlowDesk.Web.Application.Services;
using FlowDesk.Web.Components;
using FlowDesk.Web.Components.Account;
using FlowDesk.Web.Data;
using FlowDesk.Web.Domain;
using FlowDesk.Web.Infrastructure;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<FlowDeskDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<FlowDeskDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IEmailSender<ApplicationUser>, EmailSender>();
builder.Services.AddScoped<IAppEmailSender, EmailSender>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<ISlaService, SlaService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IRequestService, RequestService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IAdminService, AdminService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "FlowDesk", utc = DateTime.UtcNow }));

app.MapGet("/api/attachments/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    IRequestService requestService,
    CancellationToken ct) =>
{
    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var file = await requestService.GetAttachmentAsync(id, userId, GetEffectiveRole(principal), ct);
    if (file is null)
    {
        return Results.NotFound();
    }

    return Results.File(file.FilePath, file.ContentType, file.FileName);
}).RequireAuthorization();

app.MapGet("/api/reports/csv", async (
    DateTime? from,
    DateTime? to,
    IReportService reportService,
    CancellationToken ct) =>
{
    var (start, end) = NormalizePeriod(from, to);
    var bytes = await reportService.ExportCsvAsync(start, end, ct);
    return Results.File(bytes, "text/csv; charset=utf-8", $"flowdesk-report-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Manager, RoleNames.Admin));

app.MapGet("/api/reports/pdf", async (
    DateTime? from,
    DateTime? to,
    IReportService reportService,
    CancellationToken ct) =>
{
    var (start, end) = NormalizePeriod(from, to);
    var bytes = await reportService.ExportPdfAsync(start, end, ct);
    return Results.File(bytes, "application/pdf", $"flowdesk-report-{start:yyyyMMdd}-{end:yyyyMMdd}.pdf");
}).RequireAuthorization(policy => policy.RequireRole(RoleNames.Manager, RoleNames.Admin));

await DbSeeder.SeedAsync(app.Services);

app.Run();

static (DateTime From, DateTime To) NormalizePeriod(DateTime? from, DateTime? to)
{
    var now = DateTime.UtcNow;
    var start = (from ?? new DateTime(now.Year, now.Month, 1)).Date;
    var end = (to ?? now).Date;
    var startUtc = DateTime.SpecifyKind(start, DateTimeKind.Local).ToUniversalTime();
    var endUtc = DateTime.SpecifyKind(end.AddDays(1), DateTimeKind.Local).ToUniversalTime().AddTicks(-1);
    return startUtc <= endUtc ? (startUtc, endUtc) : (endUtc, startUtc);
}

static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

static string GetEffectiveRole(ClaimsPrincipal principal)
{
    if (principal.IsInRole(RoleNames.Admin)) return RoleNames.Admin;
    if (principal.IsInRole(RoleNames.Manager)) return RoleNames.Manager;
    if (principal.IsInRole(RoleNames.Dispatcher)) return RoleNames.Dispatcher;
    if (principal.IsInRole(RoleNames.Executor)) return RoleNames.Executor;
    if (principal.IsInRole(RoleNames.Client)) return RoleNames.Client;
    return string.Empty;
}
