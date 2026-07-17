using System.IO;
using HawalaExchange.Application.Interfaces.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace HawalaExchange.Infrastructure.Services
{
    public class FileService : IFileService
    {
        private readonly IWebHostEnvironment _env;
        private readonly string _baseUploadPath;

        public FileService(IWebHostEnvironment env)
        {
            _env = env;
            _baseUploadPath = Path.Combine(_env.WebRootPath, "uploads");
            EnsureDirectoryExists(_baseUploadPath);
        }

        public async Task<FileUploadResult> UploadFileAsync(IFormFile file, string subDirectory = "uploads")
        {
            if (file == null || file.Length == 0)
                return new FileUploadResult { Success = false, ErrorMessage = "فایل نامعتبر است." };

            using var stream = file.OpenReadStream();
            return await UploadFileAsync(stream, file.FileName, subDirectory);
        }

        public async Task<FileUploadResult> UploadFileAsync(Stream fileStream, string fileName, string subDirectory = "uploads")
        {
            try
            {
                var uploadDir = Path.Combine(_baseUploadPath, subDirectory);
                EnsureDirectoryExists(uploadDir);

                var uniqueName = await GenerateUniqueFileNameAsync(fileName);
                var fullPath = Path.Combine(uploadDir, uniqueName);

                using var fs = new FileStream(fullPath, FileMode.Create);
                await fileStream.CopyToAsync(fs);

                var relativePath = Path.Combine("uploads", subDirectory, uniqueName).Replace('\\', '/');
                var fileInfo = new FileInfo(fullPath);

                return new FileUploadResult
                {
                    Success = true,
                    FilePath = relativePath,
                    FileName = uniqueName,
                    OriginalFileName = fileName,
                    FileSize = fileInfo.Length
                };
            }
            catch (Exception ex)
            {
                return new FileUploadResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            try
            {
                // ===== بررسی null بودن =====
                if (string.IsNullOrWhiteSpace(filePath))
                    return true;

                var fullPath = MapPath(filePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                return await Task.FromResult(true);
            }
            catch
            {
                return false;
            }
        }

        public async Task<byte[]?> GetFileBytesAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var fullPath = MapPath(filePath);
            if (!File.Exists(fullPath)) return null;
            return await File.ReadAllBytesAsync(fullPath);
        }

        public string GetFileUrl(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            return $"/{filePath.Replace('\\', '/')}";
        }

        public async Task<bool> FileExistsAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return await Task.FromResult(false);

            var fullPath = MapPath(filePath);
            return await Task.FromResult(File.Exists(fullPath));
        }

        public async Task<string> GenerateUniqueFileNameAsync(string originalFileName)
        {
            var ext = Path.GetExtension(originalFileName);
            var name = Path.GetFileNameWithoutExtension(originalFileName);
            var cleanName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var guid = Guid.NewGuid().ToString("N").Substring(0, 8);
            return await Task.FromResult($"{cleanName}_{timestamp}_{guid}{ext}");
        }

        public bool IsValidFileType(string fileName, string[] allowedExtensions)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return allowedExtensions.Contains(ext);
        }

        public bool IsValidFileSize(long fileSize, long maxSizeInBytes)
        {
            return fileSize <= maxSizeInBytes;
        }

        private string MapPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;

            return Path.Combine(_env.WebRootPath, relativePath.Replace('/', '\\'));
        }

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}