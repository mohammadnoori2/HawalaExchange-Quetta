using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;             // ✅ For IHostEnvironment

namespace HawalaExchange.Application.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHostEnvironment _environment;   // ✅ Changed from IWebHostEnvironment
        private readonly IMapper _mapper;

        public DocumentService(ApplicationDbContext context, IHostEnvironment environment, IMapper mapper)
        {
            _context = context;
            _environment = environment;
            _mapper = mapper;
        }

        public async Task<DocumentDto> UploadDocumentAsync(UploadDocumentDto uploadDto)
        {
            if (uploadDto.FileContent == null || uploadDto.FileContent.Length == 0)
                throw new ArgumentException("File content is required.");

            if (string.IsNullOrWhiteSpace(uploadDto.FileName))
                throw new ArgumentException("File name is required.");

            var fileName = $"{Guid.NewGuid()}_{uploadDto.FileName}";
            // ✅ Build the wwwroot path from ContentRootPath
            var webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
            var uploadPath = Path.Combine(webRootPath, "uploads", uploadDto.EntityType);

            if (!Directory.Exists(uploadPath))
                Directory.CreateDirectory(uploadPath);

            var filePath = Path.Combine(uploadPath, fileName);

            await File.WriteAllBytesAsync(filePath, uploadDto.FileContent);

            var document = new Document
            {
                EntityType = uploadDto.EntityType,
                EntityId = uploadDto.EntityId,
                FileName = uploadDto.FileName,
                FilePath = $"/uploads/{uploadDto.EntityType}/{fileName}",
                ContentType = uploadDto.ContentType ?? GetContentType(uploadDto.FileName),
                UploadedAt = DateTime.UtcNow
            };

            await _context.Documents.AddAsync(document);
            await _context.SaveChangesAsync();

            return _mapper.Map<DocumentDto>(document);
        }

        public async Task<IEnumerable<DocumentDto>> GetDocumentsByEntityAsync(string entityType, long entityId)
        {
            var docs = await _context.Documents
                .Where(d => d.EntityType == entityType && d.EntityId == entityId)
                .ToListAsync();
            return _mapper.Map<IEnumerable<DocumentDto>>(docs);
        }

        public async Task<DocumentDto?> GetDocumentByIdAsync(long id)
        {
            var doc = await _context.Documents.FindAsync(id);
            return doc == null ? null : _mapper.Map<DocumentDto>(doc);
        }

        public async Task DeleteDocumentAsync(long id)
        {
            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) throw new KeyNotFoundException($"Document with ID {id} not found.");

            var webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
            var physicalPath = Path.Combine(webRootPath, doc.FilePath.TrimStart('/'));
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);

            _context.Documents.Remove(doc);
            await _context.SaveChangesAsync();
        }

        public async Task<byte[]> DownloadDocumentAsync(long id)
        {
            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) throw new KeyNotFoundException($"Document with ID {id} not found.");

            var webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
            var physicalPath = Path.Combine(webRootPath, doc.FilePath.TrimStart('/'));
            if (!File.Exists(physicalPath))
                throw new FileNotFoundException("File not found.");

            return await File.ReadAllBytesAsync(physicalPath);
        }

        public async Task<IEnumerable<DocumentDto>> GetDocumentsByTypeAsync(string entityType)
        {
            var docs = await _context.Documents
                .Where(d => d.EntityType == entityType)
                .ToListAsync();
            return _mapper.Map<IEnumerable<DocumentDto>>(docs);
        }

        private static string GetContentType(string fileName)
        {
            var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                ".xml" => "application/xml",
                ".json" => "application/json",
                _ => "application/octet-stream"
            };
        }
    }
}