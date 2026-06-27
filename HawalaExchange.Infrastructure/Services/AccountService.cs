using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services
{
    public class AccountService : BaseService<Account, AccountDto, CreateAccountDto, UpdateAccountDto>, IAccountService
    {
        private readonly ILedgerService _ledgerService;

        public AccountService(ApplicationDbContext context, IMapper mapper, ILedgerService ledgerService)
            : base(context, mapper)
        {
            _ledgerService = ledgerService;
        }

        public async Task<AccountDto?> GetByAccountCodeAsync(string accountCode)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
            return entity == null ? null : _mapper.Map<AccountDto>(entity);
        }

        public async Task<IEnumerable<AccountDto>> GetByAccountTypeAsync(string accountType)
        {
            var entities = await _dbSet
                .Where(a => a.AccountType == accountType && a.IsActive)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountDto>>(entities);
        }

        public async Task<IEnumerable<AccountDto>> GetByReferenceAsync(string referenceType, long referenceId)
        {
            var entities = await _dbSet
                .Where(a => a.ReferenceType == referenceType && a.ReferenceId == referenceId && a.IsActive)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountDto>>(entities);
        }

        public async Task<IEnumerable<AccountDto>> GetActiveAccountsAsync()
        {
            var entities = await _dbSet
                .Where(a => a.IsActive && !a.IsArchived)
                .ToListAsync();
            return _mapper.Map<IEnumerable<AccountDto>>(entities);
        }

        public async Task<AccountDto> ArchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Account with ID {id} not found.");

            entity.IsArchived = true;
            entity.IsActive = false;
            await _context.SaveChangesAsync();
            return _mapper.Map<AccountDto>(entity);
        }

        public async Task<AccountDto> UnarchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Account with ID {id} not found.");

            entity.IsArchived = false;
            entity.IsActive = true;
            await _context.SaveChangesAsync();
            return _mapper.Map<AccountDto>(entity);
        }

        public async Task<decimal> GetAccountBalanceAsync(long accountId, long currencyId)
        {
            return await _ledgerService.GetAccountBalanceAsync(accountId, currencyId);
        }

        public async Task<IEnumerable<BalanceDto>> GetAllAccountBalancesAsync(long accountId)
        {
            return await _ledgerService.GetAccountBalancesAsync(accountId);
        }

        protected override async Task ValidateCreateAsync(Account entity, CreateAccountDto dto)
        {
            if (await _dbSet.AnyAsync(a => a.AccountCode == entity.AccountCode))
                throw new InvalidOperationException($"Account with code '{entity.AccountCode}' already exists.");
        }
    }
}