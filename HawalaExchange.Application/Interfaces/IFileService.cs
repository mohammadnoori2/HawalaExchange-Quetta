using Microsoft.AspNetCore.Http;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IFileService
    {
        Task<FileUploadResult> UploadFileAsync(IFormFile file, string subDirectory = "uploads");
        Task<FileUploadResult> UploadFileAsync(Stream fileStream, string fileName, string subDirectory = "uploads");
        Task<bool> DeleteFileAsync(string filePath);
        Task<byte[]?> GetFileBytesAsync(string filePath);
        string GetFileUrl(string filePath);
        Task<bool> FileExistsAsync(string filePath);
        Task<string> GenerateUniqueFileNameAsync(string originalFileName);
        bool IsValidFileType(string fileName, string[] allowedExtensions);
        bool IsValidFileSize(long fileSize, long maxSizeInBytes);
    }

    public class FileUploadResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? ErrorMessage { get; set; }
    }
}