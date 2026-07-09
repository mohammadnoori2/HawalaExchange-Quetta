using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Infrastructure.Services
{
    public class CapitalInvestmentService : ICapitalInvestmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public CapitalInvestmentService(
            ApplicationDbContext context,
            IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
        }

        public async Task CreateAsync(CreateCapitalInvestmentDto dto)
        {
            if (dto.CurrencyId <= 0)
                throw new InvalidOperationException("انتخاب ارز الزامی است.");

            if (dto.Amount <= 0)
                throw new InvalidOperationException("مبلغ سرمایه باید بزرگتر از صفر باشد.");

            if (dto.ReceivingAccountId <= 0)
                throw new InvalidOperationException("انتخاب حساب دریافت‌کننده الزامی است.");

            if (dto.CapitalAccountId <= 0)
                throw new InvalidOperationException("انتخاب حساب سرمایه الزامی است.");

            if (dto.ReceivingAccountId == dto.CapitalAccountId)
                throw new InvalidOperationException("حساب دریافت‌کننده و حساب سرمایه نمی‌تواند یکی باشد.");

            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var receivingAccount = await _context.Accounts
                    .FirstOrDefaultAsync(x => x.Id == dto.ReceivingAccountId && !x.IsArchived);

                if (receivingAccount == null)
                    throw new InvalidOperationException("حساب دریافت‌کننده معتبر نیست.");

                var capitalAccount = await _context.Accounts
                    .FirstOrDefaultAsync(x => x.Id == dto.CapitalAccountId && !x.IsArchived);

                if (capitalAccount == null)
                    throw new InvalidOperationException("حساب سرمایه معتبر نیست.");

                if (capitalAccount.AccountType != "Equity")
                    throw new InvalidOperationException("حساب سرمایه باید از نوع Equity باشد.");

                var currencyExists = await _context.Currencies
                    .AnyAsync(x => x.Id == dto.CurrencyId && x.IsActive);

                if (!currencyExists)
                    throw new InvalidOperationException("ارز انتخاب‌شده معتبر نیست.");

                var description = string.IsNullOrWhiteSpace(dto.Description)
                    ? "ثبت سرمایه مالک"
                    : dto.Description.Trim();

                var ledgerEntries = new List<LedgerEntry>
            {
                new LedgerEntry
                {
                    TransactionId = null,
                    AccountId = dto.ReceivingAccountId,
                    CurrencyId = dto.CurrencyId,
                    TalabKar = 0,
                    BadehKar = dto.Amount,
                    Description = description,
                    CreatedAt = dto.InvestmentDate
                },
                new LedgerEntry
                {
                    TransactionId = null,
                    AccountId = dto.CapitalAccountId,
                    CurrencyId = dto.CurrencyId,
                    TalabKar = dto.Amount,
                    BadehKar = 0,
                    Description = description,
                    CreatedAt = dto.InvestmentDate
                }
            };

                await _context.LedgerEntries.AddRangeAsync(ledgerEntries);
                await _context.SaveChangesAsync();

                await _auditLogService.LogAsync(
                    "CREATE",
                    "LedgerEntries",
                    0,
                    null,
                    $"ثبت سرمایه به مبلغ {dto.Amount}",
                    GetCurrentUserId());

                await dbTransaction.CommitAsync();
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        private long GetCurrentUserId() => 1;
    }
}
