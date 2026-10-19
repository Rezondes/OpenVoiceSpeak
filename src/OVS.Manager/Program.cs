using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OVS.Manager;
using OVS.Manager.Accounts;
using OVS.Manager.Appearance;
using OVS.Manager.Audit;
using OVS.Manager.Components;
using OVS.Manager.Data;
using OVS.Manager.Localization;
using OVS.Shared;

// Package M1: the manager's composition root (CODING-RULES rule 14)
var builder = WebApplication.CreateBuilder(args);
var options = ManagerOptions.Read(builder.Configuration);
Directory.CreateDirectory(options.DataDir);
builder.Services.AddSingleton(options);
builder.Services.AddDbContext<ManagerDbContext>(database => database.UseSqlite($"Data Source={options.DatabasePath}"));
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
builder.Services.AddAuthorization(authorization => authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<FirstAdmin>();
builder.Services.AddSingleton<LoginLimits>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Database.MigrateAsync();
await app.Services.GetRequiredService<FirstAdmin>().InitializeAsync(app.Configuration);

app.UseRequestLocalization(UiCulture.CreateOptions());
app.UseMiddleware<SetupGate>();
app.UseAuthentication();
app.UseMiddleware<PasswordChangeGate>();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets().AllowAnonymous();
app.MapGet("/healthz", () => Results.Ok(new { Status = "ok", BuildInfo.Current.Version })).AllowAnonymous();
AppearanceEndpoints.Map(app);
AccountEndpoints.Map(app);
AuditEndpoints.Map(app);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
