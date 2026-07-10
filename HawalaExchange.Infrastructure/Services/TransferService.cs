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

        private static readonly HashSet<string> ValidTransferMethods = new()
        {
            "Cash", "Bank", "Hawala"
        };

        public TransferService(ApplicationDbContext context, ILedgerService ledgerService, IMapper mapper)
        {
            _context = context;
            _ledgerService = ledgerService;
            _mapper = mapper;
        }

        public async Task<TransferDto> CreateTransferAsync(CreateTransferDto createDto)
        {
            if (createDto.FromAccountId == 0)
                throw new InvalidOperationException("شناسه حساب مبدأ معتبر نیست.");

            if (createDto.ToAccountId == 0)
                throw new InvalidOperationException("شناسه حساب مقصد معتبر نیست.");

            if (createDto.CurrencyId == 0)
                throw new InvalidOperationException("شناسه ارز معتبر نیست.");

            if (createDto.FromAccountId == createDto.ToAccountId)
                throw new InvalidOperationException("حساب مبدأ و مقصد نمی‌توانند یکسان باشند.");

            if (createDto.Amount <= 0)
                throw new InvalidOperationException("مبلغ انتقال باید بزرگتر از صفر باشد.");

            if (string.IsNullOrWhiteSpace(createDto.TransferMethod))
                throw new InvalidOperationException("روش انتقال الزامی است.");

            if (!ValidTransferMethods.Contains(createDto.TransferMethod))
                throw new InvalidOperationException($"روش انتقال نامعتبر است. مقادیر مجاز: {string.Join(", ", ValidTransferMethods)}");

            var fromAccountExists = await _context.Accounts.AnyAsync(a => a.Id == createDto.FromAccountId);
            if (!fromAccountExists)
                throw new InvalidOperationException($"حساب مبدأ با شناسه {createDto.FromAccountId} وجود ندارد.");

            var toAccountExists = await _context.Accounts.AnyAsync(a => a.Id == createDto.ToAccountId);
            if (!toAccountExists)
                throw new InvalidOperationException($"حساب مقصد با شناسه {createDto.ToAccountId} وجود ندارد.");

            var currencyExists = await _context.Currencies.AnyAsync(c => c.Id == createDto.CurrencyId);
            if (!currencyExists)
                throw new InvalidOperationException($"ارز با شناسه {createDto.CurrencyId} وجود ندارد.");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var transfer = _mapper.Map<Transfer>(createDto);
                await _context.Transfers.AddAsync(transfer);
                await _context.SaveChangesAsync();

                // ✅ ثبت ورودی‌های دفتر کل با TransactionId صحیح
                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                {
                    TransferId = transfer.Id,
                    AccountId = transfer.FromAccountId,
                    CurrencyId = transfer.CurrencyId,
                    TalabKar = transfer.Amount,
                    BadehKar = 0,
                    Description = $"انتقال به حساب {transfer.ToAccountId}"
                });

                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                {
                    TransferId = transfer.Id,
                    AccountId = transfer.ToAccountId,
                    CurrencyId = transfer.CurrencyId,
                    TalabKar = 0,
                    BadehKar = transfer.Amount,
                    Description = $"انتقال از حساب {transfer.FromAccountId}"
                });

                await transaction.CommitAsync();

                return _mapper.Map<TransferDto>(transfer);
            }
            catch (DbUpdateException dbEx)
            {
                await transaction.RollbackAsync();
                var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;
                throw new InvalidOperationException($"خطا در ذخیره‌سازی: {innerMessage}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"خطا در ذخیره‌سازی: {ex.Message}");
            }
        }

        

        public async Task<IEnumerable<TransferDto>> GetTransfersByAccountAsync(long accountId)
        {
            var transfers = await _context.Transfers
                .Where(t => t.FromAccountId == accountId || t.ToAccountId == accountId)
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .OrderByDescending(t => t.Id)
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
                .OrderByDescending(t => t.Id)
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

        public async Task<IEnumerable<TransferDto>> GetAllAsync()
        {
            var transfers = await _context.Transfers
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .OrderByDescending(t => t.Id)
                .ToListAsync();

            return _mapper.Map<IEnumerable<TransferDto>>(transfers);
        }

        public async Task<IEnumerable<TransferDto>> GetTransfersByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            var transfers = await _context.Transfers
                .Include(t => t.LedgerEntries)
                .Where(t => t.LedgerEntries != null
                            && t.LedgerEntries.Any(le => le.CreatedAt >= fromDate && le.CreatedAt <= toDate))
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .OrderByDescending(t => t.Id)
                .ToListAsync();

            return _mapper.Map<IEnumerable<TransferDto>>(transfers);
        }
    }
}