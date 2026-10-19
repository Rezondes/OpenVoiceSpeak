using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OVS.Manager;
using OVS.Manager.Accounts;
using OVS.Manager.Appearance;
using OVS.Manager.Audit;
using OVS.Manager.Components;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Localization;
using OVS.Manager.Mail;
using OVS.Manager.Notifications;
using OVS.Manager.Servers;
using OVS.Manager.Telemetry;
using OVS.Shared;

// Package M1: the manager's composition root (CODING-RULES rule 14)
var builder = WebApplication.CreateBuilder(args);
var options = ManagerOptions.Read(builder.Configuration);
Directory.CreateDirectory(options.DataDir);
builder.Services.AddSingleton(options);
builder.Services.AddDbContext<ManagerDbContext>(database => database.UseSqlite($"Data Source={options.DatabasePath}"));
builder.Services.AddDbContext<TelemetryStore>(database => database.UseSqlite($"Data Source={options.TelemetryPath}")); // Package M20
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(TimeProvider.System);

// Package M3: accounts (MA1, D4, D5): user name and password of at least 12 characters, 5 failures lock for 15 minutes
builder.Services.AddIdentity<ManagerUser, IdentityRole>(identity =>
    {
        identity.Password.RequiredLength = 12;
        identity.Password.RequireDigit = false;
        identity.Password.RequireLowercase = false;
        identity.Password.RequireUppercase = false;
        identity.Password.RequireNonAlphanumeric = false;
        identity.Password.RequiredUniqueChars = 1;
        identity.Lockout.MaxFailedAccessAttempts = 5;
        identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        identity.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ManagerDbContext>()
    .AddDefaultTokenProviders();
// Package M26 (D5): mail over SMTP; a reset link is valid for an hour
builder.Services.AddSingleton(SmtpOptions.Read(builder.Configuration));
builder.Services.AddSingleton<IMailSender, MailSender>();
builder.Services.AddScoped<PasswordReset>();
builder.Services.Configure<DataProtectionTokenProviderOptions>(tokens => tokens.TokenLifespan = PasswordReset.LinkLifetime);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLog>(); // Package M5
builder.Services.AddHostedService<AuditRetention>();
builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<SessionCookieEvents>();
builder.Services.AddScoped<AccountService>();
builder.Services.ConfigureApplicationCookie(cookie =>
{
    cookie.EventsType = typeof(SessionCookieEvents); // Package M4: sessions, the start password, 403 for a forbidden page
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Secure behind HTTPS; the forwarded headers come with M32
    cookie.LoginPath = "/login";
    cookie.AccessDeniedPath = "/login";
});
builder.Services.AddAuthorization(authorization =>
{
    authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    authorization.AddPolicy(ServerAccessHandler.Policy, policy => policy.RequireAuthenticatedUser().AddRequirements(new ServerAccessRequirement()));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<IDockerEngine, DockerEngine>(); // Package M6: Docker only through the socket proxy
builder.Services.AddSingleton<FirstAdmin>();
// Package M7 (MA4): the servers' identities are encrypted with keys of their own in /data/keys
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.DataDir, "keys"))).SetApplicationName("OVS.Manager");
builder.Services.AddScoped<ServerIdentityStore>();
builder.Services.AddScoped<ServerFactory>();
// Package M8: the status of every server, checked in the background, and its start, stop and restart
builder.Services.AddSingleton<IServerProbe, TlsServerProbe>();
builder.Services.AddSingleton<StatusWatcher>();
builder.Services.AddHostedService(services => services.GetRequiredService<StatusWatcher>());
builder.Services.AddScoped<ServerLifecycle>();
builder.Services.AddScoped<ServerDeletion>(); // Package M9: the trash and its daily sweep
builder.Services.AddScoped<ServerAccess>(); // Package M10: a manager sees and operates the assigned servers only
builder.Services.AddScoped<IAuthorizationHandler, ServerAccessHandler>();
builder.Services.AddScoped<ServerAssignmentList>();
// Package M11: one invisible service session to each running server, with its state as a mirror
builder.Services.AddSingleton<ServerConnections>();
builder.Services.AddHostedService(services => services.GetRequiredService<ServerConnections>());
builder.Services.AddScoped<SettingsCommands>(); // Package M12
builder.Services.AddScoped<ModerationCommands>(); // Package M13
builder.Services.AddScoped<GroupCommands>();
builder.Services.AddScoped<BackupCommands>(); // Package M15
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddScoped<LogCommands>(); // Package M16
// Package M17: the OVS releases from GitHub, hourly
builder.Services.AddHttpClient(nameof(ReleaseCatalog));
builder.Services.AddSingleton(services => new ReleaseCatalog(services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ReleaseCatalog)),
    BuildInfo.Current, services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILogger<ReleaseCatalog>>()));
builder.Services.AddHostedService(services => services.GetRequiredService<ReleaseCatalog>());
builder.Services.AddScoped<ServerUpdate>(); // Package M18
builder.Services.AddSingleton<Announcement>(); // Package M19
builder.Services.AddHostedService<UpdateWindowRunner>();
// Package M21: telemetry rolled up each minute and trimmed each hour
builder.Services.AddSingleton<TelemetryLimit>();
builder.Services.AddHostedService<Rollup>();
builder.Services.AddHostedService<Retention>();
builder.Services.AddScoped<LiveFeed>(); // Package M22
builder.Services.AddScoped<HistoryQuery>(); // Package M23
builder.Services.AddScoped<DashboardQuery>();
builder.Services.AddScoped<ErrorQuery>(); // Package M24
// Package M25: limits, usage over time and the data root's free disk
builder.Services.AddScoped<ServerResources>();
builder.Services.AddScoped<ResourceQuery>();
builder.Services.AddHostedService<UsageSampler>();
builder.Services.AddSingleton<IHostDisk, DataDirDisk>();
// Package M27: notifications by mail and webhook; webhooks reach only public addresses and follow no redirect
builder.Services.AddHttpClient(Notifier.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { ConnectCallback = PublicOnly.ConnectAsync, AllowAutoRedirect = false });
builder.Services.AddSingleton<INotificationChannel, EmailChannel>();
builder.Services.AddSingleton<INotificationChannel, WebhookChannel>();
builder.Services.AddSingleton<INotificationChannel, DiscordWebhookChannel>();
builder.Services.AddSingleton<Notifier>();
builder.Services.AddHostedService(services => services.GetRequiredService<Notifier>());
builder.Services.AddScoped<ConsoleLog>();
builder.Services.AddHostedService<DeletionSweep>();
builder.Services.AddSingleton<LoginLimits>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TelemetryStore>().Database.MigrateAsync();
}
await app.Services.GetRequiredService<FirstAdmin>().InitializeAsync(app.Configuration);

app.UseMiddleware<TelemetryReceiver>(); // Package M20: port 4318 is OTLP only, before every page and gate
app.UseRequestLocalization(UiCulture.CreateOptions());
app.UseMiddleware<SetupGate>();
app.UseAuthentication();
app.UseMiddleware<PasswordChangeGate>();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets().AllowAnonymous();
// Package M6 (AC4): 503 while Docker is not reachable; the pages keep working
app.MapGet("/healthz", async (IDockerEngine docker, CancellationToken cancellation) =>
{
    bool reachable = await docker.IsReachableAsync(cancellation);
    return Results.Json(new { Status = reachable ? "ok" : "degraded", BuildInfo.Current.Version, Docker = reachable },
        statusCode: reachable ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();
AppearanceEndpoints.Map(app);
AccountEndpoints.Map(app);
AuditEndpoints.Map(app);
BackupEndpoints.Map(app);
LogEndpoints.Map(app);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
