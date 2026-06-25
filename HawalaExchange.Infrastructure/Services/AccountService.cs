using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using YourNamespace.Data;
using YourNamespace.Entities;

namespace HawalaExchange.Infrastructure.Services
{
    public class AccountService: IAccountService
    {
        private readonly ApplicationDbContext _context;
        public AccountService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<AccountDtos>> GetAllAsync()
        {
            return await _context.Accounts
                .AsNoTracking()
                .OrderBy(x => x.AccountName)
                .Select(x => new AccountDtos
                {
                    Id = x.Id,
                    AccountCode = x.AccountCode,
                    AccountName = x.AccountName,
                    AccountType = x.AccountType,
                    ReferenceType = x.ReferenceType,
                    ReferenceId = x.ReferenceId,
                    IsArchived = x.IsArchived,
                    DateTime = x.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<AccountDtos?> GetByIdAsync(long id)
        {
            return await _context.Accounts
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new AccountDtos
                {
                    Id = x.Id,
                    AccountCode = x.AccountCode,
                    AccountName = x.AccountName,
                    AccountType = x.AccountType,
                    ReferenceType = x.ReferenceType,
                    ReferenceId = x.ReferenceId,
                    IsArchived = x.IsArchived,
                    DateTime = x.CreatedAt
                })
                .FirstOrDefaultAsync();
        }

        public async Task<long> CreateAsync(CreateAccountRequest request)
        {
            var exists = await _context.Accounts
                .AnyAsync(x => x.AccountCode == request.AccountCode);
            if (exists)
            {
                throw new InvalidOperationException("Account code already exists.");
            }

            var account = new Account
            {
                AccountCode = request.AccountCode.Trim(),
                AccountName = request.AccountName.Trim(),
                AccountType = request.AccountType,
                ReferenceType = request.ReferenceType,
                ReferenceId = request.ReferenceId,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();
            return account.Id;
        }

        public async Task UpdateAsync(long id, UpdateAccountRequest request)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
            {
                throw new InvalidOperationException("Account not found.");
            }

            account.AccountCode = request.AccountCode.Trim();
            account.AccountName = request.AccountName.Trim();
            account.AccountType = request.AccountType;
            account.ReferenceType = request.ReferenceType;
            account.ReferenceId = request.ReferenceId;

            await _context.SaveChangesAsync();
        }

        public async Task ArchiveAsync(long id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
            {
                throw new InvalidOperationException("Account not found.");
            }

            account.IsArchived = true;
            await _context.SaveChangesAsync();
        }
    }
}
