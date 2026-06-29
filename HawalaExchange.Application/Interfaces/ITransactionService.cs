using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;   // ✅ اضافه شد – برای شناسایی موجودیت Transaction

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface ITransactionService : IBaseService<Transaction, TransactionDto, CreateTransactionDto, UpdateTransactionDto>
    {
        Task<TransactionDto?> GetByTransactionNoAsync(string transactionNo);
        Task<IEnumerable<TransactionDto>> GetByBranchAsync(long branchId);
        Task<IEnumerable<TransactionDto>> GetByCustomerAsync(long customerId);
        Task<IEnumerable<TransactionDto>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<IEnumerable<TransactionDto>> GetByStatusAsync(string status);
        Task<TransactionDto> CancelTransactionAsync(long id, CancelTransactionDto cancelDto);
        Task<TransactionDto> ReverseTransactionAsync(long id, CancelTransactionDto cancelDto);
        Task<TransactionDto> ProcessHawalaSendAsync(CreateTransactionDto createDto);
        Task<TransactionDto> ProcessHawalaReceiveAsync(CreateTransactionDto createDto);
        Task<TransactionDto> ProcessExchangeAsync(CreateTransactionDto createDto);
        Task<IEnumerable<TransactionDto>> GetPendingTransactionsAsync();
        Task<TransactionDto> MarkAsPaidAsync(long id);
        Task<string> GenerateTransactionNumberAsync(string transactionType);
    }
}