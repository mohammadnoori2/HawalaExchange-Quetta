using AutoMapper;
using AutoMapper.QueryableExtensions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

public class CapitalInvestmentService : ICapitalInvestmentService
{
    private readonly ApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLogService;
    private readonly ICurrencyCostService _currencyCostService;

    public CapitalInvestmentService(
        ApplicationDbContext context,
        IMapper mapper,
        IAuditLogService auditLogService,
        ICurrencyCostService currencyCostService)
    {
        _context = context;
        _mapper = mapper;
        _auditLogService = auditLogService;
        _currencyCostService = currencyCostService;
    }

    public async Task<List<CapitalInvestmentDto>> GetAllAsync()
    {
        return await _context.CapitalInvestments
            .AsNoTracking()
            .Include(x => x.Currency)
            .Include(x => x.ProfitCurrency)
            .Include(x => x.ReceivingAccount)
            .Include(x => x.CapitalAccount)
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.InvestmentDate)
            .ProjectTo<CapitalInvestmentDto>(_mapper.ConfigurationProvider)
            .ToListAsync();
    }

    public async Task<CapitalInvestmentDto?> GetByIdAsync(long id)
    {
        return await _context.CapitalInvestments
            .AsNoTracking()
            .Include(x => x.Currency)
            .Include(x => x.ProfitCurrency)
            .Include(x => x.ReceivingAccount)
            .Include(x => x.CapitalAccount)
            .Where(x => x.Id == id && !x.IsDeleted)
            .ProjectTo<CapitalInvestmentDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
    }

    public async Task<long> CreateAsync(CreateCapitalInvestmentDto dto)
    {
        Validate(dto.CurrencyId, dto.Amount, dto.ReceivingAccountId, dto.CapitalAccountId,
            dto.ProfitCurrencyId, dto.ProfitCurrencyAmount);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await ValidateAccountsAsync(
                dto.ReceivingAccountId,
                dto.CapitalAccountId,
                dto.CurrencyId);
            await ValidateProfitCurrencyAsync(dto.ProfitCurrencyId);

            var capitalInvestment = _mapper.Map<CapitalInvestment>(dto);

            capitalInvestment.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? "ثبت سرمایه مالک"
                : dto.Description.Trim();

            capitalInvestment.CreatedAt = DateTime.UtcNow;
            capitalInvestment.CreatedBy = GetCurrentUserId();
            capitalInvestment.IsDeleted = false;

            await _context.CapitalInvestments.AddAsync(capitalInvestment);
            await _context.SaveChangesAsync();
            await _currencyCostService.RebuildAsync();

            await CreateLedgerEntriesAsync(capitalInvestment);

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "CREATE",
                "CapitalInvestments",
                capitalInvestment.Id,
                null,
                $"ثبت سرمایه به مبلغ {AmountValueHelper.Format(capitalInvestment.Amount)}",
                GetCurrentUserId());

            await dbTransaction.CommitAsync();

            return capitalInvestment.Id;
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateAsync(long id, UpdateCapitalInvestmentDto dto)
    {
        Validate(dto.CurrencyId, dto.Amount, dto.ReceivingAccountId, dto.CapitalAccountId,
            dto.ProfitCurrencyId, dto.ProfitCurrencyAmount);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var capitalInvestment = await _context.CapitalInvestments
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (capitalInvestment == null)
                throw new InvalidOperationException("ثبت سرمایه یافت نشد.");

            await ValidateAccountsAsync(
                dto.ReceivingAccountId,
                dto.CapitalAccountId,
                dto.CurrencyId);
            await ValidateProfitCurrencyAsync(dto.ProfitCurrencyId);

            await DeleteLedgerEntriesAsync(id);

            _mapper.Map(dto, capitalInvestment);

            capitalInvestment.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? "ثبت سرمایه مالک"
                : dto.Description.Trim();

            capitalInvestment.ModifiedAt = DateTime.UtcNow;
            capitalInvestment.ModifiedBy = GetCurrentUserId();

            await CreateLedgerEntriesAsync(capitalInvestment);

            await _context.SaveChangesAsync();
            await _currencyCostService.RebuildAsync();

            await _auditLogService.LogAsync(
                "UPDATE",
                "CapitalInvestments",
                capitalInvestment.Id,
                null,
                $"ویرایش ثبت سرمایه به مبلغ {AmountValueHelper.Format(capitalInvestment.Amount)}",
                GetCurrentUserId());

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAsync(long id)
    {
        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var capitalInvestment = await _context.CapitalInvestments
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (capitalInvestment == null)
                throw new InvalidOperationException("ثبت سرمایه یافت نشد.");

            await DeleteLedgerEntriesAsync(id);

            capitalInvestment.IsDeleted = true;
            capitalInvestment.ModifiedAt = DateTime.UtcNow;
            capitalInvestment.ModifiedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();
            await _currencyCostService.RebuildAsync();

            await _auditLogService.LogAsync(
                "DELETE",
                "CapitalInvestments",
                capitalInvestment.Id,
                null,
                $"حذف ثبت سرمایه به مبلغ {AmountValueHelper.Format(capitalInvestment.Amount)}",
                GetCurrentUserId());

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            throw;
        }
    }

    private async Task CreateLedgerEntriesAsync(CapitalInvestment capitalInvestment)
    {
        var description = capitalInvestment.Description ?? "ثبت سرمایه مالک";

        var ledgerEntries = new List<LedgerEntry>
    {
        new LedgerEntry
        {
            CapitalInvestmentId = capitalInvestment.Id,
            HawalaId = null,

            AccountId = capitalInvestment.ReceivingAccountId,
            CurrencyId = capitalInvestment.CurrencyId,

            // Cash/Bank receives money, so it is badehkar / Debit
            TalabKar = 0,
            BadehKar = capitalInvestment.Amount,

            Description = $"{description} با شماره {capitalInvestment.Id}",
            CreatedAt = capitalInvestment.InvestmentDate
        },
        new LedgerEntry
        {
            CapitalInvestmentId = capitalInvestment.Id,
            HawalaId = null,

            AccountId = capitalInvestment.CapitalAccountId,
            CurrencyId = capitalInvestment.CurrencyId,

            // Owner Capital is source of capital, so it is Talabkar / Credit
            TalabKar = capitalInvestment.Amount,
            BadehKar = 0,

            Description = $"{description} با شماره {capitalInvestment.Id}",
            CreatedAt = capitalInvestment.InvestmentDate
        }
    };

        await _context.LedgerEntries.AddRangeAsync(ledgerEntries);
    }
    private async Task DeleteLedgerEntriesAsync(long capitalInvestmentId)
    {
        var ledgerEntries = await _context.LedgerEntries
            .Where(x => x.CapitalInvestmentId == capitalInvestmentId)
            .ToListAsync();

        if (ledgerEntries.Any())
        {
            _context.LedgerEntries.RemoveRange(ledgerEntries);
        }
    }

    private async Task ValidateAccountsAsync(
        long receivingAccountId,
        long capitalAccountId,
        long currencyId)
    {
        var currencyExists = await _context.Currencies
            .AnyAsync(x => x.Id == currencyId && x.IsActive);

        if (!currencyExists)
            throw new InvalidOperationException("ارز انتخاب‌شده معتبر نیست.");

        var receivingAccount = await _context.Accounts
            .FirstOrDefaultAsync(x => x.Id == receivingAccountId && !x.IsArchived);

        if (receivingAccount == null)
            throw new InvalidOperationException("حساب دریافت‌کننده معتبر نیست.");

        if (receivingAccount.AccountType != "Cash" &&
            receivingAccount.AccountType != "Bank")
        {
            throw new InvalidOperationException("حساب دریافت‌کننده باید از نوع Cash یا Bank باشد.");
        }

        var capitalAccount = await _context.Accounts
            .FirstOrDefaultAsync(x => x.Id == capitalAccountId && !x.IsArchived);

        if (capitalAccount == null)
            throw new InvalidOperationException("حساب سرمایه معتبر نیست.");

        if (capitalAccount.AccountType != "Equity")
            throw new InvalidOperationException("حساب سرمایه باید از نوع Equity باشد.");
    }

    private static void Validate(
        long currencyId,
        decimal amount,
        long receivingAccountId,
        long capitalAccountId,
        long profitCurrencyId,
        decimal profitCurrencyAmount)
    {
        if (currencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز الزامی است.");

        if (amount <= 0)
            throw new InvalidOperationException("مبلغ سرمایه باید بزرگتر از صفر باشد.");

        if (receivingAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب دریافت‌کننده الزامی است.");

        if (capitalAccountId <= 0)
            throw new InvalidOperationException("انتخاب حساب سرمایه الزامی است.");

        if (receivingAccountId == capitalAccountId)
            throw new InvalidOperationException("حساب دریافت‌کننده و حساب سرمایه نمی‌تواند یکی باشد.");

        if (profitCurrencyId <= 0)
            throw new InvalidOperationException("انتخاب ارز محاسبه سود الزامی است.");

        if (profitCurrencyAmount <= 0)
            throw new InvalidOperationException("ارزش افتتاحیه سرمایه در ارز محاسبه سود باید بزرگتر از صفر باشد.");
    }

    private async Task ValidateProfitCurrencyAsync(long profitCurrencyId)
    {
        if (!await _context.Currencies.AnyAsync(x => x.Id == profitCurrencyId && x.IsActive))
            throw new InvalidOperationException("ارز محاسبه سود معتبر نیست.");
    }

    private long GetCurrentUserId() => _context.RequireCurrentUserId();
}
