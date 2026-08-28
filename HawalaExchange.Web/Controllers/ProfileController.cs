using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Account;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Web.Controllers;

[Authorize]
[Route("api/profile")]
public sealed class ProfileController(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    IFileService fileService,
    IAntiforgery antiforgery) : Controller
{
    private const long MaximumImageSize = 3 * 1024 * 1024;
    private const long MaximumRequestSize = MaximumImageSize + (64 * 1024);
    private static readonly HashSet<string> AllowedExtensions =
        [".jpg", ".jpeg", ".jfif", ".png", ".webp", ".gif", ".bmp", ".avif", ".heic", ".heif"];
    private static readonly HashSet<string> AllowedContentTypes =
        [
            "image/jpeg", "image/png", "image/webp", "image/gif", "image/bmp", "image/x-ms-bmp",
            "image/avif", "image/heic", "image/heif", "image/heic-sequence", "image/heif-sequence"
        ];

    [HttpPost("image")]
    [RequestSizeLimit(MaximumRequestSize)]
    public async Task<IActionResult> UploadImage(IFormFile? image, CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(HttpContext);

        if (image is null || image.Length == 0)
            return RedirectWithStatus("خطا: لطفاً یک تصویر انتخاب کنید.");

        if (image.Length > MaximumImageSize)
            return RedirectWithStatus("خطا: اندازه تصویر نباید بیشتر از ۳ مگابایت باشد.");

        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) ||
            !AllowedContentTypes.Contains(image.ContentType.ToLowerInvariant()) ||
            !await HasValidImageSignatureAsync(image, extension, cancellationToken))
        {
            return RedirectWithStatus("خطا: فقط تصویر JPG، JFIF، PNG، WebP، GIF، BMP، AVIF، HEIC یا HEIF قابل استفاده است.");
        }

        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return Challenge();

        var uploaded = await fileService.UploadFileAsync(image, "profiles");
        if (!uploaded.Success)
            return RedirectWithStatus("خطا: تصویر ذخیره نشد. لطفاً دوباره تلاش کنید.");

        var oldImagePath = user.ProfileImagePath;
        user.ProfileImagePath = uploaded.FilePath;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await fileService.DeleteFileAsync(uploaded.FilePath);
            return RedirectWithStatus("خطا: تصویر پروفایل ذخیره نشد.");
        }

        if (IsOwnedProfileImage(oldImagePath) &&
            !string.Equals(oldImagePath, uploaded.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            await fileService.DeleteFileAsync(oldImagePath!);
        }

        return RedirectWithStatus("تصویر پروفایل با موفقیت ذخیره شد.");
    }

    [HttpPost("image/remove")]
    public async Task<IActionResult> RemoveImage(CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(HttpContext);

        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return Challenge();

        var oldImagePath = user.ProfileImagePath;
        if (string.IsNullOrWhiteSpace(oldImagePath))
            return RedirectWithStatus("تصویر پروفایل وجود ندارد.");

        user.ProfileImagePath = null;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return RedirectWithStatus("خطا: تصویر پروفایل حذف نشد.");

        if (IsOwnedProfileImage(oldImagePath))
            await fileService.DeleteFileAsync(oldImagePath);

        return RedirectWithStatus("تصویر پروفایل حذف شد.");
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        return long.TryParse(userId, out var parsedUserId)
            ? await context.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == parsedUserId, cancellationToken)
            : null;
    }

    private IActionResult RedirectWithStatus(string message)
    {
        Response.Cookies.Append(
            IdentityRedirectManager.StatusCookieName,
            message,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = Request.IsHttps,
                MaxAge = TimeSpan.FromSeconds(10)
            });
        return Redirect("/profile");
    }

    private static bool IsOwnedProfileImage(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.Replace('\\', '/').StartsWith("uploads/profiles/", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> HasValidImageSignatureAsync(
        IFormFile image,
        string extension,
        CancellationToken cancellationToken)
    {
        var header = new byte[32];
        await using var stream = image.OpenReadStream();
        var bytesRead = await stream.ReadAsync(header, cancellationToken);

        return extension switch
        {
            ".jpg" or ".jpeg" or ".jfif" => bytesRead >= 3 &&
                header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => bytesRead >= 8 &&
                header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" => bytesRead >= 12 &&
                header.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".gif" => bytesRead >= 6 &&
                (header.AsSpan(0, 6).SequenceEqual("GIF87a"u8) ||
                 header.AsSpan(0, 6).SequenceEqual("GIF89a"u8)),
            ".bmp" => bytesRead >= 2 && header[0] == 0x42 && header[1] == 0x4D,
            ".avif" => HasAvifSignature(header, bytesRead),
            ".heic" or ".heif" => HasHeifSignature(header, bytesRead),
            _ => false
        };
    }

    private static bool HasAvifSignature(byte[] header, int bytesRead)
    {
        if (bytesRead < 12 || !header.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            return false;

        for (var offset = 8; offset + 4 <= bytesRead; offset += 4)
        {
            var brand = header.AsSpan(offset, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
                return true;
        }

        return false;
    }

    private static bool HasHeifSignature(byte[] header, int bytesRead)
    {
        if (bytesRead < 12 || !header.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            return false;

        for (var offset = 8; offset + 4 <= bytesRead; offset += 4)
        {
            var brand = header.AsSpan(offset, 4);
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) ||
                brand.SequenceEqual("hevc"u8) || brand.SequenceEqual("hevx"u8) ||
                brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8) ||
                brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8))
                return true;
        }

        return false;
    }
}
