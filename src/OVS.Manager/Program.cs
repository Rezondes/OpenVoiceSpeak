using Microsoft.EntityFrameworkCore;
using OVS.Manager;
using OVS.Manager.Appearance;
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

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Database.MigrateAsync();

app.UseRequestLocalization(UiCulture.CreateOptions());
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/healthz", () => Results.Ok(new { Status = "ok", BuildInfo.Current.Version }));
AppearanceEndpoints.Map(app);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
