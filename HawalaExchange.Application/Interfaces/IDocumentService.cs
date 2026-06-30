using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IDocumentService
    {

        Task<IEnumerable<DocumentDto>> GetAllAsync();
        Task<DocumentDto> UploadDocumentAsync(UploadDocumentDto uploadDto);
        Task<IEnumerable<DocumentDto>> GetDocumentsByEntityAsync(string entityType, long entityId);
        Task<DocumentDto?> GetDocumentByIdAsync(long id);
        Task DeleteDocumentAsync(long id);
        Task<byte[]> DownloadDocumentAsync(long id);
        Task<IEnumerable<DocumentDto>> GetDocumentsByTypeAsync(string entityType);
    }
}