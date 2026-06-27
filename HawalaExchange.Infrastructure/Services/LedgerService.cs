using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
// If your DbContext is in Infrastructure.Data, use that instead.

namespace HawalaExchange.Application.Services
{
    public class LedgerService : ILedgerService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public LedgerService(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<LedgerEntryDto> CreateLedgerEntryAsync(CreateLedgerEntryDto createDto)
        {
            var entry = _mapper.Map<LedgerEntry>(createDto);
            entry.CreatedAt = DateTime.UtcNow;

            await _context.LedgerEntries.AddAsync(entry);
            await _context.SaveChangesAsync();

            return _mapper.Map<LedgerEntryDto>(entry);
        }

        public async Task<IEnumerable<LedgerEntryDto>> GetEntriesByTransactionAsync(long transactionId)
        {
            var entries = await _context.LedgerEntries
                .Where(e => e.TransactionId == transactionId)
                .Include(e => e.Account)
                .Include(e => e.Currency)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            return _mapper.Map<IEnumerable<LedgerEntryDto>>(entries);
        }

        public async Task<IEnumerable<LedgerEntryDto>> GetEntriesByAccountAsync(long accountId, long? currencyId = null)
        {
            // ✅ Use var – no explicit type
            var query = _context.LedgerEntries
                .Where(e => e.AccountId == accountId)
                .Include(e => e.Account)
                .Include(e => e.Currency);

            // ✅ This works – query becomes IQueryable<LedgerEntry>
            if (currencyId.HasValue)
                query = (Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<LedgerEntry, Currency?>)query.Where(e => e.CurrencyId == currencyId.Value);

            var entries = await query
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            return _mapper.Map<IEnumerable<LedgerEntryDto>>(entries);
        }

        public async Task<IEnumerable<LedgerEntryDto>> GetEntriesByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            var entries = await _context.LedgerEntries
                .Where(e => e.CreatedAt >= fromDate && e.CreatedAt <= toDate)
                .Include(e => e.Account)
                .Include(e => e.Currency)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            return _mapper.Map<IEnumerable<LedgerEntryDto>>(entries);
        }

        public async Task<decimal> GetAccountBalanceAsync(long accountId, long currencyId)
        {
            var entries = await _context.LedgerEntries
                .Where(e => e.AccountId == accountId && e.CurrencyId == currencyId)
                .ToListAsync();

            return entries.Sum(e => e.TalabKar - e.BadehKar);
        }

        public async Task<IEnumerable<BalanceDto>> GetAccountBalancesAsync(long accountId)
        {
            var entries = await _context.LedgerEntries
                .Where(e => e.AccountId == accountId)
                .Include(e => e.Currency)
                .ToListAsync();

            return entries
                .GroupBy(e => e.CurrencyId)
                .Select(g => new BalanceDto
                {
                    CurrencyId = g.Key,
                    CurrencyCode = g.First().Currency?.Code ?? "N/A",
                    Balance = g.Sum(e => e.TalabKar - e.BadehKar)
                });
        }

        public async Task<IEnumerable<LedgerEntryDto>> GetCustomerLedgerAsync(long customerId)
        {
            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Customer" && a.ReferenceId == customerId);

            if (account == null)
                return Enumerable.Empty<LedgerEntryDto>();

            return await GetEntriesByAccountAsync(account.Id);
        }

        public async Task<IEnumerable<LedgerEntryDto>> GetCorrespondentLedgerAsync(long correspondentId)
        {
            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == correspondentId);

            if (account == null)
                return Enumerable.Empty<LedgerEntryDto>();

            return await GetEntriesByAccountAsync(account.Id);
        }

        public async Task<IEnumerable<LedgerEntryDto>> GetTrialBalanceAsync(DateTime asOfDate)
        {
            var entries = await _context.LedgerEntries
                .Where(e => e.CreatedAt <= asOfDate)
                .Include(e => e.Account)
                .Include(e => e.Currency)
                .ToListAsync();

            return _mapper.Map<IEnumerable<LedgerEntryDto>>(entries);
        }
    }
}