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
        private readonly ICurrencyCostService _currencyCostService;
        private readonly IMapper _mapper;

        private static readonly HashSet<string> ValidTransferMethods = new()
        {
            "Cash", "Bank", "Hawala"
        };

        public TransferService(
            ApplicationDbContext context,
            ILedgerService ledgerService,
            ICurrencyCostService currencyCostService,
            IMapper mapper)
        {
            _context = context;
            _ledgerService = ledgerService;
            _currencyCostService = currencyCostService;
            _mapper = mapper;
        }

        public async Task<TransferDto> CreateTransferAsync(CreateTransferDto createDto)
        {
            await ValidateTransferAsync(
                createDto.FromAccountId,
                createDto.ToAccountId,
                createDto.CurrencyId,
                createDto.Amount,
                createDto.TransferMethod);
            (createDto.ProfitCurrencyId, createDto.ProfitCurrencyAmount) =
                await ResolveCostBasisAsync(
                    createDto.FromAccountId,
                    createDto.ToAccountId,
                    createDto.CurrencyId,
                    createDto.ProfitCurrencyId,
                    createDto.ProfitCurrencyAmount);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var transfer = _mapper.Map<Transfer>(createDto);
                await _context.Transfers.AddAsync(transfer);
                await _context.SaveChangesAsync();

                if (string.IsNullOrWhiteSpace(transfer.ReferenceNumber))
                {
                    transfer.ReferenceNumber = BuildReferenceNumber(transfer.Id);
                    await _context.SaveChangesAsync();
                }

                await CreateTransferLedgerEntriesAsync(transfer);
                await _currencyCostService.RebuildAsync();

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

        public async Task<TransferDto> UpdateTransferAsync(long id, UpdateTransferDto updateDto)
        {
            await ValidateTransferAsync(
                updateDto.FromAccountId,
                updateDto.ToAccountId,
                updateDto.CurrencyId,
                updateDto.Amount,
                updateDto.TransferMethod);
            (updateDto.ProfitCurrencyId, updateDto.ProfitCurrencyAmount) =
                await ResolveCostBasisAsync(
                    updateDto.FromAccountId,
                    updateDto.ToAccountId,
                    updateDto.CurrencyId,
                    updateDto.ProfitCurrencyId,
                    updateDto.ProfitCurrencyAmount);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var transfer = await _context.Transfers
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (transfer == null)
                    throw new KeyNotFoundException($"انتقال با شناسه {id} یافت نشد.");

                var ledgerEntries = await _context.LedgerEntries
                    .Where(x => x.TransferId == id)
                    .ToListAsync();
                if (ledgerEntries.Count > 0)
                    _context.LedgerEntries.RemoveRange(ledgerEntries);

                var existingReferenceNumber = transfer.ReferenceNumber;
                _mapper.Map(updateDto, transfer);
                transfer.ReferenceNumber = string.IsNullOrWhiteSpace(updateDto.ReferenceNumber)
                    ? existingReferenceNumber ?? BuildReferenceNumber(transfer.Id)
                    : updateDto.ReferenceNumber.Trim();
                await _context.SaveChangesAsync();

                await CreateTransferLedgerEntriesAsync(transfer);
                await _currencyCostService.RebuildAsync();
                await transaction.CommitAsync();

                return await GetTransferByIdAsync(id)
                    ?? throw new InvalidOperationException("انتقال ویرایش شد، اما دوباره یافت نشد.");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task DeleteTransferAsync(long id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var transfer = await _context.Transfers
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (transfer == null)
                    throw new KeyNotFoundException($"انتقال با شناسه {id} یافت نشد.");

                var ledgerEntries = await _context.LedgerEntries
                    .Where(x => x.TransferId == id)
                    .ToListAsync();
                if (ledgerEntries.Count > 0)
                    _context.LedgerEntries.RemoveRange(ledgerEntries);

                _context.Transfers.Remove(transfer);
                await _context.SaveChangesAsync();
                await _currencyCostService.RebuildAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task CreateTransferLedgerEntriesAsync(Transfer transfer)
        {
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
        }

        private static string BuildReferenceNumber(long transferId) =>
            $"TRF-{DateTime.Now:yyyyMMdd}-{transferId:D4}";

        private async Task ValidateTransferAsync(
            long fromAccountId,
            long toAccountId,
            long currencyId,
            decimal amount,
            string transferMethod)
        {
            if (fromAccountId <= 0)
                throw new InvalidOperationException("شناسه حساب مبدأ معتبر نیست.");
            if (toAccountId <= 0)
                throw new InvalidOperationException("شناسه حساب مقصد معتبر نیست.");
            if (fromAccountId == toAccountId)
                throw new InvalidOperationException("حساب مبدأ و مقصد نمی‌توانند یکسان باشند.");
            if (currencyId <= 0)
                throw new InvalidOperationException("شناسه ارز معتبر نیست.");
            if (amount <= 0)
                throw new InvalidOperationException("مبلغ انتقال باید بزرگتر از صفر باشد.");
            if (string.IsNullOrWhiteSpace(transferMethod) ||
                !ValidTransferMethods.Contains(transferMethod))
                throw new InvalidOperationException("روش انتقال معتبر نیست.");
            if (!await _context.Accounts.AnyAsync(x => x.Id == fromAccountId))
                throw new InvalidOperationException("حساب مبدأ وجود ندارد.");
            if (!await _context.Accounts.AnyAsync(x => x.Id == toAccountId))
                throw new InvalidOperationException("حساب مقصد وجود ندارد.");
            if (!await _context.Currencies.AnyAsync(x => x.Id == currencyId))
                throw new InvalidOperationException("ارز انتخاب‌شده وجود ندارد.");
        }

        private async Task<(long? ProfitCurrencyId, decimal? ProfitCurrencyAmount)> ResolveCostBasisAsync(
            long fromAccountId,
            long toAccountId,
            long currencyId,
            long? submittedProfitCurrencyId,
            decimal? submittedProfitCurrencyAmount)
        {
            var accounts = await _context.Accounts
                .Where(x => x.Id == fromAccountId || x.Id == toAccountId)
                .Select(x => new { x.Id, x.AccountType, x.CorrespondentId })
                .ToListAsync();
            var from = accounts.Single(x => x.Id == fromAccountId);
            var to = accounts.Single(x => x.Id == toAccountId);
            var fromCorrespondent = from.CorrespondentId.HasValue ||
                                    string.Equals(from.AccountType, "Correspondent", StringComparison.OrdinalIgnoreCase);
            var toCorrespondent = to.CorrespondentId.HasValue ||
                                  string.Equals(to.AccountType, "Correspondent", StringComparison.OrdinalIgnoreCase);

            // Transfers inside the office (or between two correspondents) only move the location
            // of money and must never create a new currency-cost lot.
            if (fromCorrespondent == toCorrespondent)
                return (null, null);

            var profitCurrencyId = submittedProfitCurrencyId ?? await _context.CompanySettings
                .AsNoTracking()
                .Select(x => x.DefaultProfitCurrencyId)
                .FirstOrDefaultAsync();
            if (!profitCurrencyId.HasValue)
                throw new InvalidOperationException("ابتدا ارز اصلی محاسبه مفاد و ضرر را در تنظیمات شرکت تعیین کنید.");
            if (!await _context.Currencies.AnyAsync(x => x.Id == profitCurrencyId.Value && x.IsActive))
                throw new InvalidOperationException("ارز اصلی محاسبه مفاد و ضرر معتبر نیست.");

            // Moving the reporting currency itself requires no separate carrying-value lot.
            if (currencyId == profitCurrencyId.Value)
                return (profitCurrencyId, null);

            if (fromCorrespondent)
            {
                if (!submittedProfitCurrencyAmount.HasValue || submittedProfitCurrencyAmount.Value <= 0)
                    throw new InvalidOperationException("برای انتقال ارز از نمایندگی، ارزش انتقالی در ارز مفاد الزامی است.");
                return (profitCurrencyId, submittedProfitCurrencyAmount.Value);
            }

            // An outbound transfer removes inventory at its current moving-average cost.
            return (profitCurrencyId, null);
        }

        public async Task<IEnumerable<TransferDto>> GetTransfersByAccountAsync(long accountId)
        {
            var transfers = await _context.Transfers
                .Where(t => t.FromAccountId == accountId || t.ToAccountId == accountId)
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .Include(t => t.ProfitCurrency)
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
                .Include(t => t.ProfitCurrency)
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
                .Include(t => t.ProfitCurrency)
                .FirstOrDefaultAsync(t => t.Id == id);

            return transfer == null ? null : _mapper.Map<TransferDto>(transfer);
        }

        public async Task<IEnumerable<TransferDto>> GetAllAsync()
        {
            var transfers = await _context.Transfers
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Include(t => t.Currency)
                .Include(t => t.ProfitCurrency)
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
