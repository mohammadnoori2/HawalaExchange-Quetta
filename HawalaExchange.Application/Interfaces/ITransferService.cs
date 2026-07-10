using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface ITransferService
    {
        Task<TransferDto> CreateTransferAsync(CreateTransferDto createDto);
        Task<IEnumerable<TransferDto>> GetTransfersByAccountAsync(long accountId);
        Task<IEnumerable<TransferDto>> GetTransfersByMethodAsync(string transferMethod);
        Task<TransferDto?> GetTransferByIdAsync(long id);
        Task<IEnumerable<TransferDto>> GetTransfersByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<IEnumerable<TransferDto>> GetAllAsync();
    }
}