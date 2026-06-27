using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class TransferService : ITransferService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILedgerService _ledgerService;
        private readonly IMapper _mapper;

        public TransferService(ApplicationDbContext context, ILedgerService ledgerService, IMapper mapper)
        {
            _context = context;
            _ledgerService = ledgerService;
            _mapper = mapper;
        }

        public async Task<TransferDto> CreateTransferAsync(CreateTransferDto createDto)
        {
            var transfer = _mapper.Map<Transfer>(createDto);

            if (transfer.FromAccountId == transfer.ToAccountId)
                throw new InvalidOperationException("From account and To account cannot be the same.");
            if (transfer.Amount <= 0)
                throw new InvalidOperationException("Transfer amount must be greater than zero.");

            await _context.Transfers.AddAsync(transfer);
            await _context.SaveChangesAsync();

            // Create ledger entries
            await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
            {
                AccountId = transfer.FromAccountId,
                CurrencyId = transfer.CurrencyId,
                TalabKar = transfer.Amount,
                BadehKar = 0,
                Description = $"Transfer to Account {transfer.ToAccountId}"
            });
            await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
            {
                AccountId = transfer.ToAccountId,
                CurrencyId = transfer.CurrencyId,
                TalabKar = 0,
                BadehKar = transfer.Amount,
                Description = $"Transfer from Account {transfer.FromAccountId}"
            });

            return _mapper.Map<TransferDto>(transfer);
        }

        public async Task<IEnumerable<TransferDto>> GetTransfersByTransactionAsync(long transactionId)
        {
            var transfers = await _context.Transfers
                .Where(t => t.TransactionId == transactionId)
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransferDto>>(transfers);
        }

        public async Task<IEnumerable<TransferDto>> GetTransfersByAccountAsync(long accountId)
        {
            var transfers = await _context.Transfers
                .Where(t => t.FromAccountId == accountId || t.ToAccountId == accountId)
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransferDto>>(transfers);
        }

        public async Task<IEnumerable<TransferDto>> GetTransfersByMethodAsync(string transferMethod)
        {
            var transfers = await _context.Transfers
                .Where(t => t.TransferMethod == transferMethod)
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransferDto>>(transfers);
        }

        public async Task<TransferDto?> GetTransferByIdAsync(long id)
        {
            var transfer = await _context.Transfers
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .FirstOrDefaultAsync(t => t.Id == id);
            return transfer == null ? null : _mapper.Map<TransferDto>(transfer);
        }

        public async Task<IEnumerable<TransferDto>> GetTransfersByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            var transfers = await _context.Transfers
                .Include(t => t.Transaction)
                .Where(t => t.Transaction != null && t.Transaction.CreatedAt >= fromDate && t.Transaction.CreatedAt <= toDate)
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransferDto>>(transfers);
        }
    }
}