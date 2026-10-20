using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public class FileService(IWebHostEnvironment environment, ApplicationDbContext context, IUserPermissionService permissions) : IFileService
{
    public async Task<FileUploadResult> UploadFileAsync(IFormFile file, string subDirectory = "uploads")
    {
        if (file is null || file.Length is <= 0 or > UploadContentPolicy.MaximumBytes)
            return new() { ErrorMessage = "فایل یا اندازه آن مجاز نیست." };
        await using var stream = file.OpenReadStream();
        return await UploadFileAsync(stream, file.FileName, subDirectory);
    }

    public async Task<FileUploadResult> UploadFileAsync(Stream fileStream, string fileName, string subDirectory = "uploads")
    {
        try
        {
            var directory = UploadStorage.DirectoryName(subDirectory);
            if (directory is not ("tazkira" or "profiles" or "company-logo" or "customers/photo" or "customers/tazkira"))
                throw new ArgumentException("پوشه فایل مجاز نیست.");
            var tenantId=context.CurrentTenantId;
            if(directory=="profiles" && tenantId<=0 && context.CurrentUserId>0)
                tenantId=await context.Users.IgnoreQueryFilters().Where(x=>x.Id==context.CurrentUserId && x.IsActive && x.IsPlatformUser).Select(x=>x.TenantId).SingleOrDefaultAsync();
            if (tenantId <= 0 || context.CurrentUserId <= 0) throw new UnauthorizedAccessException();
            await RequireWriteAsync(directory, deleting: false);
            UploadContentPolicy.ValidateName(fileName);
            await using var buffer = new MemoryStream();
            var chunk = new byte[81920]; int read;
            while ((read = await fileStream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > UploadContentPolicy.MaximumBytes) throw new ArgumentException("حداکثر اندازه فایل 10 MB است.");
                await buffer.WriteAsync(chunk.AsMemory(0, read));
            }
            var bytes = buffer.ToArray();
            UploadContentPolicy.Validate(fileName, bytes, imagesOnly: true);
            var name = await GenerateUniqueFileNameAsync(fileName);
            if (directory == "profiles") name = context.CurrentUserId + "_" + name;
            var logical = $"uploads/{directory}/tenant_{tenantId}/{name}";
            // Branding is intentionally public; personal/financial attachments are private.
            var root = directory == "company-logo" ? Path.Combine(environment.WebRootPath, "uploads") : UploadStorage.PrivateRoot(environment.ContentRootPath);
            var physical = UploadStorage.Physical(root, logical[8..]);
            Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
            await using (var output = new FileStream(physical, FileMode.CreateNew, FileAccess.Write))
                await output.WriteAsync(bytes);
            return new() { Success = true, FilePath = logical, FileName = name, OriginalFileName = fileName, FileSize = bytes.Length };
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or IOException)
        { return new() { ErrorMessage = "فایل ذخیره نشد؛ نوع، اندازه و مسیر فایل را بررسی کنید." }; }
    }

    public async Task<bool> DeleteFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return true;
        try
        {
            var path = UploadStorage.Normalize(filePath);
            var parts = path.Split('/');
            var directory = parts[1] == "customers" && parts.Length > 3 ? "customers/" + parts[2] : parts[1];
            await RequireWriteAsync(directory, deleting: true);
            if (!await UploadStorage.IsOwnedAsync(context, path)) return false;
            if (path.StartsWith("uploads/profiles/", StringComparison.OrdinalIgnoreCase))
            {
                var ownNew = UploadStorage.PathTenant(path).HasValue && (context.CurrentTenantId==0 || UploadStorage.PathTenant(path) == context.CurrentTenantId)
                    && Path.GetFileName(path).StartsWith(context.CurrentUserId + "_", StringComparison.Ordinal);
                if (!ownNew && !await context.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == context.CurrentUserId
                    && (x.ProfileImagePath == path || x.ProfileImagePath == "/" + path))) return false;
            }
            var physical = UploadStorage.ExistingPath(environment.ContentRootPath, environment.WebRootPath, path);
            if (File.Exists(physical)) File.Delete(physical);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or IOException) { return false; }
    }

    public async Task<byte[]?> GetFileBytesAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        var path = UploadStorage.Normalize(filePath);
        if (!await UploadStorage.IsOwnedAsync(context, path)) throw new UnauthorizedAccessException();
        var physical = UploadStorage.ExistingPath(environment.ContentRootPath, environment.WebRootPath, path);
        if (!File.Exists(physical)) return null;
        if (new FileInfo(physical).Length > UploadContentPolicy.MaximumBytes) throw new ArgumentException("فایل بیش از اندازه مجاز است.");
        return await File.ReadAllBytesAsync(physical);
    }
    public string GetFileUrl(string filePath) => string.IsNullOrWhiteSpace(filePath) ? "" : "/" + UploadStorage.Normalize(filePath);
    public async Task<bool> FileExistsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var path = UploadStorage.Normalize(filePath);
        return await UploadStorage.IsOwnedAsync(context, path) && File.Exists(UploadStorage.ExistingPath(environment.ContentRootPath, environment.WebRootPath, path));
    }
    public Task<string> GenerateUniqueFileNameAsync(string originalFileName)
    {
        UploadContentPolicy.ValidateName(originalFileName);
        return Task.FromResult(Guid.NewGuid().ToString("N") + Path.GetExtension(originalFileName).ToLowerInvariant());
    }
    public bool IsValidFileType(string name, string[] allowedExtensions) => allowedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    public bool IsValidFileSize(long size, long maximum) => size > 0 && size <= maximum;

    private async Task RequireWriteAsync(string directory, bool deleting)
    {
        if (context.CurrentUserId <= 0) throw new UnauthorizedAccessException();
        if (directory == "profiles") return; // Own-profile endpoint and deletion owner checks apply.
        if (context.CurrentTenantId <= 0) throw new UnauthorizedAccessException();
        await permissions.RefreshAsync(true);
        var keys = directory switch
        {
            "company-logo" => new[]{"Settings.Edit"},
            "tazkira" => deleting ? new[]{"Hawalas.Create","Hawalas.Edit","Hawalas.Delete","HawalaExecution.Execute"} : new[]{"Hawalas.Create","Hawalas.Edit","HawalaExecution.Execute"},
            "customers/photo" or "customers/tazkira" => deleting ? new[]{"Customers.Create","Customers.Edit","Customers.Delete"} : new[]{"Customers.Create","Customers.Edit"},
            _ => Array.Empty<string>()
        };
        if (!keys.Any(permissions.Can)) throw new UnauthorizedAccessException();
    }
}
