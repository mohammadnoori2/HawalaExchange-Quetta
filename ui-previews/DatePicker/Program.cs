using HawalaExchange.Application.Interfaces;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Services;
using HawalaExchange.DatePickerPreview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

// Standalone host: never invokes the main application's startup, migrations or seeders.
// Tenant 0 makes AppDateDisplay return before constructing or opening a database context.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped(_ => new AppDateDisplay(
    new DbContextOptionsBuilder<ApplicationDbContext>().Options, new PreviewTenant()));
var app = builder.Build();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(Path.Combine(
        app.Environment.ContentRootPath, "..", "..", "HawalaExchange.Web", "wwwroot"))),
    RequestPath = "/assets"
});
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<PreviewApp>().AddInteractiveServerRenderMode();
app.Run();

sealed class PreviewTenant : ICurrentTenant
{
    public long TenantId => 0;
    public long UserId => 0;
    public bool HasTenant => false;
}
