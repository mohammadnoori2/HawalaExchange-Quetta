using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

/// <summary>Canonical logical upload paths, private storage, and legacy tenant ownership.</summary>
public static class UploadStorage
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1000 || path.StartsWith("//") || path.StartsWith("\\\\"))
            throw new ArgumentException("مسیر فایل معتبر نیست.");
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/');
        if (segments.Length < 3 || !segments[0].Equals("uploads", StringComparison.OrdinalIgnoreCase)
            || segments.Any(x => x.Length == 0 || x is "." or ".." || x.Length > 255 || x.EndsWith('.') || x.EndsWith(' ')
                || x.Any(c => char.IsControl(c) || ":%?#<>\"|*".Contains(c)) || IsDeviceName(x)))
            throw new ArgumentException("مسیر فایل معتبر نیست.");
        return string.Join('/', segments);
    }

    private static bool IsDeviceName(string value)
    {
        var stem=value.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length==4
            && (stem.StartsWith("COM",StringComparison.Ordinal) || stem.StartsWith("LPT",StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9';
    }

    public static string DirectoryName(string directory)
    {
        var normalized = Normalize("uploads/" + directory + "/probe.txt");
        return normalized[8..^10];
    }

    public static string Physical(string root, string relative)
    {
        var absoluteRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(absoluteRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        // Never follow a pre-existing junction/symlink out of the controlled tree.
        var boundary=Directory.GetParent(absoluteRoot.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
        for (var node = full; node is not null; node = Path.GetDirectoryName(node))
        {
            if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("پیوند فایل مجاز نیست.");
            if(node.Equals(boundary,StringComparison.OrdinalIgnoreCase))break;
        }
        return full;
    }

    public static string PrivateRoot(string contentRoot) => Path.Combine(contentRoot, "App_Data", "PrivateUploads");
    public static long? PathTenant(string normalized)
    {
        var parts = normalized.Split('/');
        return parts.Length >= 4 && parts[^2].StartsWith("tenant_", StringComparison.Ordinal)
            && long.TryParse(parts[^2][7..], out var tenant) && tenant > 0 ? tenant : null;
    }

    public static async Task<bool> IsOwnedAsync(ApplicationDbContext db, string path, CancellationToken ct = default)
    {
        path = Normalize(path);
        if (db.CurrentTenantId <= 0) return await OwnPlatformProfile(db,path).AnyAsync(ct).ConfigureAwait(false);
        var tenant = PathTenant(path);
        if (tenant.HasValue) return tenant == db.CurrentTenantId;
        return !await References(db,path,foreign:true).AnyAsync(ct).ConfigureAwait(false)
            && await References(db,path,foreign:false).AnyAsync(ct).ConfigureAwait(false);
    }

    public static bool IsOwned(ApplicationDbContext db, string path)
    {
        path=Normalize(path);
        if(db.CurrentTenantId<=0)return OwnPlatformProfile(db,path).Any();
        var tenant=PathTenant(path);
        if(tenant.HasValue)return tenant==db.CurrentTenantId;
        return !References(db,path,foreign:true).Any() && References(db,path,foreign:false).Any();
    }

    private static IQueryable<int> OwnPlatformProfile(ApplicationDbContext db,string path)
    {
        var tenant=PathTenant(path);
        var isProfile=path.StartsWith("uploads/profiles/",StringComparison.OrdinalIgnoreCase);
        var ownName=Path.GetFileName(path).StartsWith(db.CurrentUserId+"_",StringComparison.Ordinal);
        return db.Users.IgnoreQueryFilters().Where(x=>isProfile && x.Id==db.CurrentUserId && x.IsActive && x.IsPlatformUser
            && (!tenant.HasValue || x.TenantId==tenant) && (ownName || x.ProfileImagePath==path || x.ProfileImagePath=="/"+path)).Select(x=>1);
    }

    private static IQueryable<int> References(ApplicationDbContext db,string path,bool foreign)
    {
        var slash="/"+path;var tenant=db.CurrentTenantId;
        // A legacy path linked to another tenant is ambiguous and always denied.
        // New references are validated before SaveChanges, so a client cannot claim an old foreign path.
        return db.Documents.IgnoreQueryFilters().Where(x=>(x.TenantId!=tenant)==foreign && (x.FilePath==path || x.FilePath==slash)).Select(x=>1)
            .Concat(db.Hawalas.IgnoreQueryFilters().Where(x=>(x.TenantId!=tenant)==foreign &&
                (x.SenderTazkiraImagePath==path || x.SenderTazkiraImagePath==slash || x.ReceiverTazkiraImagePath==path || x.ReceiverTazkiraImagePath==slash)).Select(x=>1))
            .Concat(db.Customers.IgnoreQueryFilters().Where(x=>(x.TenantId!=tenant)==foreign &&
                (x.PhotoPath==path || x.PhotoPath==slash || x.TazkiraImagePath==path || x.TazkiraImagePath==slash)).Select(x=>1))
            .Concat(db.CompanySettings.IgnoreQueryFilters().Where(x=>(x.TenantId!=tenant)==foreign && (x.LogoPath==path || x.LogoPath==slash)).Select(x=>1))
            .Concat(db.Users.IgnoreQueryFilters().Where(x=>(x.TenantId!=tenant)==foreign && (x.ProfileImagePath==path || x.ProfileImagePath==slash)).Select(x=>1));
    }

    public static string ExistingPath(string contentRoot, string webRoot, string path)
    {
        path = Normalize(path);
        var privatePath = Physical(PrivateRoot(contentRoot), path[8..]);
        return File.Exists(privatePath) ? privatePath : Physical(Path.Combine(webRoot, "uploads"), path[8..]);
    }
}
