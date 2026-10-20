using Microsoft.AspNetCore.Hosting;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Web.Services;

/// <summary>Terminates every /uploads request before either static-file mechanism can serve it.</summary>
public sealed class ProtectedUploadsMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!http.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase)) { await next(http); return; }
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        http.Response.Headers["X-Frame-Options"] = "DENY";
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)) { http.Response.StatusCode = 405; return; }
        try
        {
            var path = UploadStorage.Normalize(http.Request.Path.Value!);
            var kind = path.Split('/')[1].ToLowerInvariant();
            byte[]? bytes;
            if (kind == "company-logo")
            {
                var physical = UploadStorage.Physical(Path.Combine(environment.WebRootPath, "uploads"), path[8..]);
                if (!File.Exists(physical) || new FileInfo(physical).Length > UploadContentPolicy.MaximumBytes) { http.Response.StatusCode = 404; return; }
                bytes = await File.ReadAllBytesAsync(physical, http.RequestAborted);
                UploadContentPolicy.Validate(Path.GetFileName(path), bytes, imagesOnly: true);
            }
            else
            {
                if (http.User.Identity?.IsAuthenticated != true) { http.Response.StatusCode = 401; return; }
                var permissions = http.RequestServices.GetRequiredService<IUserPermissionService>();
                await permissions.RefreshAsync(true);
                var allowed = kind switch
                {
                    "tazkira" => Any("Hawalas.View", "Hawalas.Create", "Hawalas.Edit", "HawalaExecution.View", "HawalaExecution.Execute"),
                    "customers" => Any("Customers.View", "Customers.Create", "Customers.Edit"),
                    "profiles" => Any("Users.View"),
                    "account" or "customer" or "correspondent" or "transaction" => Any("Documents.Export"),
                    _ => false
                };
                if (!allowed && kind == "profiles")
                {
                    var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var ownNew = UploadStorage.PathTenant(path) == db.CurrentTenantId
                        && Path.GetFileName(path).StartsWith(db.CurrentUserId + "_", StringComparison.Ordinal);
                    allowed = ownNew || await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == db.CurrentUserId
                        && (x.ProfileImagePath == path || x.ProfileImagePath == "/" + path), http.RequestAborted);
                }
                if (!allowed) { http.Response.StatusCode = 404; return; }
                bytes = await http.RequestServices.GetRequiredService<IFileService>().GetFileBytesAsync(path);
                bool Any(params string[] keys) => keys.Any(permissions.Can);
            }
            if (bytes is null) { http.Response.StatusCode = 404; return; }
            var name = Path.GetFileName(path);
            var contentType = UploadContentPolicy.ContentType(name);
            http.Response.ContentType = contentType;
            // Only validated raster images render inline. Other formats download in a sandbox.
            if (!contentType.StartsWith("image/", StringComparison.Ordinal))
                http.Response.Headers.ContentDisposition = "attachment; filename=\"document" + Path.GetExtension(name) + "\"";
            http.Response.ContentLength = bytes.Length;
            if (!HttpMethods.IsHead(http.Request.Method)) await http.Response.Body.WriteAsync(bytes, http.RequestAborted);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        { http.Response.StatusCode = 404; }
    }
}
