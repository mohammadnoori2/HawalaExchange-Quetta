using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;             // ✅ For IHostEnvironment
using HawalaExchange.Infrastructure.Services;

namespace HawalaExchange.Application.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHostEnvironment _environment;   // ✅ Changed from IWebHostEnvironment
        private readonly IMapper _mapper;
        private readonly ISubscriptionAccessService _subscriptionAccess;

        public DocumentService(ApplicationDbContext context, IHostEnvironment environment, IMapper mapper, ISubscriptionAccessService subscriptionAccess)
        {
            _context = context;
            _environment = environment;
            _mapper = mapper;
            _subscriptionAccess = subscriptionAccess;
        }

        // ===== متدهای جدید =====
        public async Task<IEnumerable<DocumentDto>> GetAllAsync()
        {
            var docs = await _context.Documents
                .OrderByDescending(d => d.Id)
                .ToListAsync();

            return _mapper.Map<IEnumerable<DocumentDto>>(docs);
        }

        public async Task<DocumentDto> UploadDocumentAsync(UploadDocumentDto uploadDto)
        {
            if (uploadDto.FileContent == null || uploadDto.FileContent.Length == 0)
                throw new ArgumentException("File content is required.");

            if (string.IsNullOrWhiteSpace(uploadDto.FileName))
                throw new ArgumentException("File name is required.");

            UploadContentPolicy.Validate(uploadDto.FileName, uploadDto.FileContent);
            if (_context.CurrentTenantId <= 0) throw new UnauthorizedAccessException();
            // The client chooses an entity kind, never a directory or a foreign tenant's id.
            var exists = uploadDto.EntityType switch
            {
                "Account" => await _context.Accounts.AnyAsync(x => x.Id == uploadDto.EntityId),
                "Customer" => await _context.Customers.AnyAsync(x => x.Id == uploadDto.EntityId),
                "Correspondent" => await _context.Correspondents.AnyAsync(x => x.Id == uploadDto.EntityId),
                "Transaction" => await _context.Transactions.AnyAsync(x => x.Id == uploadDto.EntityId),
                _ => throw new ArgumentException("نوع موجودیت مجاز نیست.")
            };
            if (!exists) throw new ArgumentException("موجودیت متعلق به این صرافی یافت نشد.");

            await _subscriptionAccess.EnsureDocumentCapacityAsync(_context.CurrentTenantId, uploadDto.FileContent.LongLength);

            var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(uploadDto.FileName).ToLowerInvariant();
            var logical = $"uploads/{uploadDto.EntityType}/tenant_{_context.CurrentTenantId}/{fileName}";
            var filePath = UploadStorage.Physical(UploadStorage.PrivateRoot(_environment.ContentRootPath), logical[8..]);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var document = new Document
            {
                AccountId = uploadDto.EntityType == "Account" ? uploadDto.EntityId : null,
                CustomerId = uploadDto.EntityType == "Customer" ? uploadDto.EntityId : null,
                CorrespondentId = uploadDto.EntityType == "Correspondent" ? uploadDto.EntityId : null,
                TransactionId = uploadDto.EntityType == "Transaction" ? uploadDto.EntityId : null,
                FileName = uploadDto.FileName,
                FilePath = "/" + logical,
                ContentType = UploadContentPolicy.ContentType(uploadDto.FileName),
                FileSizeBytes = uploadDto.FileContent.LongLength,
                UploadedAt = DateTime.UtcNow
            };

            try
            {
                await using (var output = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write))
                    await output.WriteAsync(uploadDto.FileContent);
                await _context.Documents.AddAsync(document);
                await _context.SaveChangesAsync();
            }
            catch
            {
                if (File.Exists(filePath)) File.Delete(filePath);
                _context.Entry(document).State = EntityState.Detached;
                throw;
            }

            return _mapper.Map<DocumentDto>(document);
        }

        public async Task<IEnumerable<DocumentDto>> GetDocumentsByEntityAsync(string entityType, long entityId)
        {
            var docs = await _context.Documents
                .Where(d =>
                    (entityType == "Transaction" && d.TransactionId == entityId) ||
                    (entityType == "Customer" && d.CustomerId == entityId) ||
                    (entityType == "Correspondent" && d.CorrespondentId == entityId) ||
                    (entityType == "Account" && d.AccountId == entityId))
                .ToListAsync();
            return _mapper.Map<IEnumerable<DocumentDto>>(docs);
        }

        public async Task<DocumentDto?> GetDocumentByIdAsync(long id)
        {
            var doc = await _context.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (doc == null) return null;
            var result = _mapper.Map<DocumentDto>(doc);
            // Do not trust ContentType supplied by old uploads when opening a blob in the browser.
            result.ContentType = UploadContentPolicy.ContentType(doc.FileName);
            return result;
        }

        public async Task DeleteDocumentAsync(long id)
        {
            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) throw new KeyNotFoundException($"Document with ID {id} not found.");

            var physicalPath = ResolveDocumentPath(doc);
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);

            _context.Documents.Remove(doc);
            await _context.SaveChangesAsync();
        }

        public async Task<byte[]> DownloadDocumentAsync(long id)
        {
            var doc = await _context.Documents.FindAsync(id);
            if (doc == null) throw new KeyNotFoundException($"Document with ID {id} not found.");

            var physicalPath = ResolveDocumentPath(doc);
            if (!File.Exists(physicalPath))
                throw new FileNotFoundException("File not found.");
            if (new FileInfo(physicalPath).Length > UploadContentPolicy.MaximumBytes)
                throw new ArgumentException("فایل بیش از اندازه مجاز است.");

            var bytes = await File.ReadAllBytesAsync(physicalPath);
            UploadContentPolicy.Validate(doc.FileName, bytes);
            return bytes;
        }

        public async Task<IEnumerable<DocumentDto>> GetDocumentsByTypeAsync(string entityType)
        {
            var docs = await _context.Documents
                .Where(d =>
                    (entityType == "Transaction" && d.TransactionId != null) ||
                    (entityType == "Customer" && d.CustomerId != null) ||
                    (entityType == "Correspondent" && d.CorrespondentId != null) ||
                    (entityType == "Account" && d.AccountId != null))
                .ToListAsync();
            return _mapper.Map<IEnumerable<DocumentDto>>(docs);
        }

        private string ResolveDocumentPath(Document document)
        {
            var path = UploadStorage.Normalize(document.FilePath);
            var tenant = UploadStorage.PathTenant(path);
            if (document.TenantId != _context.CurrentTenantId || tenant.HasValue && tenant != _context.CurrentTenantId)
                throw new UnauthorizedAccessException();
            return UploadStorage.ExistingPath(_environment.ContentRootPath, Path.Combine(_environment.ContentRootPath, "wwwroot"), path);
        }

    }
}
