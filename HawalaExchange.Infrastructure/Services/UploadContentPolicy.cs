using System.IO.Compression;
using System.Text;

namespace HawalaExchange.Infrastructure.Services;

public static class UploadContentPolicy
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"]="image/jpeg", [".jpeg"]="image/jpeg", [".jfif"]="image/jpeg", [".png"]="image/png",
        [".gif"]="image/gif", [".webp"]="image/webp", [".bmp"]="image/bmp", [".avif"]="image/avif",
        [".heic"]="image/heic", [".heif"]="image/heif", [".pdf"]="application/pdf",
        [".docx"]="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"]="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".doc"]="application/msword", [".xls"]="application/vnd.ms-excel", [".txt"]="text/plain", [".csv"]="text/csv"
    };
    public static string ContentType(string name) => Types.GetValueOrDefault(Path.GetExtension(name))
        ?? throw new ArgumentException("نوع فایل مجاز نیست.");
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name != Path.GetFileName(name)
            || name.IndexOfAny(['/', '\\', ':', '%', '?', '#', '\0']) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("نام فایل معتبر نیست.");
        _ = ContentType(name);
    }
    public static void Validate(string name, byte[] bytes, bool imagesOnly = false)
    {
        ValidateName(name);
        var type = ContentType(name);
        if (bytes.Length is 0 or > MaximumBytes || imagesOnly && !type.StartsWith("image/")) throw new ArgumentException("فایل یا اندازه آن مجاز نیست.");
        ReadOnlySpan<byte> data = bytes;
        var valid = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jfif" => data.StartsWith(new byte[]{255,216,255}),
            ".png" => data.StartsWith(new byte[]{137,80,78,71,13,10,26,10}),
            ".gif" => data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8),
            ".bmp" => data.StartsWith("BM"u8),
            ".webp" => data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8,4).SequenceEqual("WEBP"u8),
            ".avif" or ".heic" or ".heif" => IsIsoImage(data, Path.GetExtension(name).ToLowerInvariant()),
            ".pdf" => data.StartsWith("%PDF-"u8),
            ".doc" or ".xls" => data.StartsWith(new byte[]{208,207,17,224,161,177,26,225}),
            ".docx" or ".xlsx" => IsOffice(bytes, Path.GetExtension(name).ToLowerInvariant()),
            ".txt" or ".csv" => IsPlainText(bytes),
            _ => false
        };
        if (!valid) throw new ArgumentException("محتوای فایل با نوع مجاز آن مطابقت ندارد.");
    }
    private static bool IsPlainText(byte[] bytes)
    {
        try { var text = new UTF8Encoding(false,true).GetString(bytes); return !text.Contains('\0'); }
        catch (DecoderFallbackException) { return false; }
    }
    private static bool IsIsoImage(ReadOnlySpan<byte> bytes, string extension)
    {
        if (bytes.Length < 12 || !bytes.Slice(4,4).SequenceEqual("ftyp"u8)) return false;
        var brands = extension == ".avif" ? new[]{"avif","avis"} : new[]{"heic","heix","hevc","hevx","mif1","msf1"};
        foreach (var brand in brands)
            if (bytes.Slice(8,4).SequenceEqual(Encoding.ASCII.GetBytes(brand))) return true;
        return false;
    }
    private static bool IsOffice(byte[] bytes, string extension)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            return archive.Entries.Count <= 5000 && archive.GetEntry("[Content_Types].xml") is not null
                && archive.GetEntry(extension == ".docx" ? "word/document.xml" : "xl/workbook.xml") is not null
                && !archive.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException) { return false; }
    }
}
