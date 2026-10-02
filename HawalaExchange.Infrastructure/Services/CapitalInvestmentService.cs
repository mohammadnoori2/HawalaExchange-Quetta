using AutoMapper;
using AutoMapper.QueryableExtensions;
using System.Data;
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
        // Do not let an interactive form mutate a request while validation awaits database reads.
        dto = new CreateCapitalInvestmentDto
        {
            IsWithdrawal = dto.IsWithdrawal, CurrencyId = dto.CurrencyId, Amount = dto.Amount,
            ProfitCurrencyId = dto.ProfitCurrencyId, ProfitCurrencyAmount = dto.ProfitCurrencyAmount,
            ReceivingAccountId = dto.ReceivingAccountId, CapitalAccountId = dto.CapitalAccountId,
            InvestmentDate = dto.InvestmentDate, Description = dto.Description
        };
        if (dto.IsWithdrawal)
        {
            if (dto.Amount != Math.Round(dto.Amount, 4))
                throw new InvalidOperationException("مبلغ برداشت حداکثر چهار رقم اعشار داشته باشد.");
            dto.ProfitCurrencyId = await _context.CompanySettings.AsNoTracking()
                .Select(x => x.DefaultProfitCurrencyId).FirstOrDefaultAsync() ?? dto.ProfitCurrencyId;
            if (dto.ProfitCurrencyId <= 0) dto.ProfitCurrencyId = dto.CurrencyId;
            // The cost service derives the actual carrying value; this is not user-entered income.
            dto.ProfitCurrencyAmount = dto.Amount;
        }
        Validate(dto.CurrencyId, dto.Amount, dto.ReceivingAccountId, dto.CapitalAccountId,
            dto.ProfitCurrencyId, dto.ProfitCurrencyAmount);

        using var dbTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            await ValidateAccountsAsync(
                dto.ReceivingAccountId,
                dto.CapitalAccountId,
                dto.CurrencyId);
            await ValidateProfitCurrencyAsync(dto.ProfitCurrencyId);
            if (dto.IsWithdrawal)
                await ValidateWithdrawalBalanceAsync(dto.ReceivingAccountId, dto.CurrencyId, dto.Amount, dto.InvestmentDate);

            var capitalInvestment = _mapper.Map<CapitalInvestment>(dto);
            if (dto.IsWithdrawal)
                capitalInvestment.CapitalAccountId = await GetWithdrawalAccountAsync(dto.CapitalAccountId);

            capitalInvestment.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? dto.IsWithdrawal ? "برداشت مالک" : "ثبت سرمایه مالک"
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
                $"{(capitalInvestment.IsWithdrawal ? "برداشت مالک" : "ثبت سرمایه")} به مبلغ {AmountValueHelper.Format(capitalInvestment.Amount)}",
                GetCurrentUserId());

            await dbTransaction.CommitAsync();

            return capitalInvestment.Id;
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task UpdateAsync(long id, UpdateCapitalInvestmentDto dto)
    {
        Validate(dto.CurrencyId, dto.Amount, dto.ReceivingAccountId, dto.CapitalAccountId,
            dto.ProfitCurrencyId, dto.ProfitCurrencyAmount);

        using var dbTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var capitalInvestment = await _context.CapitalInvestments
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (capitalInvestment == null)
                throw new InvalidOperationException("ثبت سرمایه یافت نشد.");
            if (capitalInvestment.IsWithdrawal)
                throw new InvalidOperationException("برداشت مالک قابل ویرایش نیست؛ آن را لغو و برداشت جدید ثبت کنید.");
            var oldAccountId = capitalInvestment.ReceivingAccountId;
            var oldCurrencyId = capitalInvestment.CurrencyId;

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
            await ProtectExistingWithdrawalsAsync(oldAccountId, oldCurrencyId);
            await ProtectExistingWithdrawalsAsync(capitalInvestment.ReceivingAccountId, capitalInvestment.CurrencyId);
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
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task DeleteAsync(long id)
    {
        using var dbTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var capitalInvestment = await _context.CapitalInvestments
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

            if (capitalInvestment == null)
                throw new InvalidOperationException("ثبت سرمایه یافت نشد.");

            if (capitalInvestment.IsWithdrawal)
            {
                if (capitalInvestment.CancelledAt.HasValue)
                    throw new InvalidOperationException("این برداشت قبلاً لغو شده است.");
                var now = DateTime.UtcNow;
                if (capitalInvestment.InvestmentDate > now)
                    throw new InvalidOperationException("برداشت آینده قابل لغو نیست.");
                if (await _context.CashDailyBalances.AnyAsync(x => x.AccountId == capitalInvestment.ReceivingAccountId &&
                    x.CurrencyId == capitalInvestment.CurrencyId && x.IsClosed && x.JournalDate >= DateTime.Today))
                    throw new InvalidOperationException("روز صندوق بسته شده است؛ لغو برداشت در این روز مجاز نیست.");
                var originals = await _context.LedgerEntries.AsNoTracking()
                    .Where(x => x.CapitalInvestmentId == id).ToListAsync();
                if (originals.Count != 2)
                    throw new InvalidOperationException("ثبت حسابداری برداشت معتبر نیست.");
                var branchId = await _context.Users.Where(x => x.Id == GetCurrentUserId())
                    .Select(x => x.BranchId).SingleAsync() ??
                    await _context.Branches.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
                var reversal = new Transaction
                {
                    TransactionNo = $"OW-REV-{id}", TransactionType = "OwnerWithdrawalReversal",
                    BranchId = branchId, Status = "Paid", CreatedBy = GetCurrentUserId(), CreatedAt = now,
                    Remarks = $"لغو برداشت مالک شماره {id}"
                };
                _context.Transactions.Add(reversal);
                await _context.SaveChangesAsync();
                foreach (var entry in originals)
                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        TransactionId = reversal.Id, AccountId = entry.AccountId, CurrencyId = entry.CurrencyId,
                        BadehKar = entry.TalabKar, TalabKar = entry.BadehKar,
                        CreatedAt = now, Description = $"لغو برداشت مالک شماره {id}"
                    });
                capitalInvestment.CancelledAt = now;
            }
            else
            {
                await DeleteLedgerEntriesAsync(id);
                capitalInvestment.IsDeleted = true;
            }
            capitalInvestment.ModifiedAt = DateTime.UtcNow;
            capitalInvestment.ModifiedBy = GetCurrentUserId();

            await _context.SaveChangesAsync();
            if (!capitalInvestment.IsWithdrawal)
                await ProtectExistingWithdrawalsAsync(capitalInvestment.ReceivingAccountId, capitalInvestment.CurrencyId);
            await _currencyCostService.RebuildAsync();

            await _auditLogService.LogAsync(
                capitalInvestment.IsWithdrawal ? "REVERSE" : "DELETE",
                "CapitalInvestments",
                capitalInvestment.Id,
                null,
                $"{(capitalInvestment.IsWithdrawal ? "لغو برداشت مالک" : "حذف ثبت سرمایه")} به مبلغ {AmountValueHelper.Format(capitalInvestment.Amount)}",
                GetCurrentUserId());

            await dbTransaction.CommitAsync();
        }
        catch
        {
            await dbTransaction.RollbackAsync();
            _context.ChangeTracker.Clear();
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
            TalabKar = capitalInvestment.IsWithdrawal ? capitalInvestment.Amount : 0,
            BadehKar = capitalInvestment.IsWithdrawal ? 0 : capitalInvestment.Amount,

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
            TalabKar = capitalInvestment.IsWithdrawal ? 0 : capitalInvestment.Amount,
            BadehKar = capitalInvestment.IsWithdrawal ? capitalInvestment.Amount : 0,

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

    private async Task<long> GetWithdrawalAccountAsync(long ownerAccountId)
    {
        var owner = await _context.Accounts.SingleAsync(x => x.Id == ownerAccountId);
        if (owner.AccountCode.StartsWith("OWNER-DRAW-", StringComparison.Ordinal))
            throw new InvalidOperationException("حساب سرمایه مالک را انتخاب کنید، نه حساب برداشت.");
        var code = $"OWNER-DRAW-{ownerAccountId}";
        var account = await _context.Accounts.FirstOrDefaultAsync(x => x.AccountCode == code);
        if (account != null)
        {
            if (account.IsArchived || account.AccountType != "Equity")
                throw new InvalidOperationException("حساب برداشت مالک معتبر یا فعال نیست.");
            return account.Id;
        }
        account = new Account
        {
            AccountCode = code, AccountName = $"برداشت مالک — {owner.AccountName}"[..Math.Min(200, $"برداشت مالک — {owner.AccountName}".Length)],
            AccountType = "Equity", CreatedAt = DateTime.UtcNow
        };
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account.Id;
    }

    private async Task ValidateWithdrawalBalanceAsync(long accountId, long currencyId, decimal amount, DateTime date)
    {
        if (date > DateTime.UtcNow)
            throw new InvalidOperationException("تاریخ برداشت نمی‌تواند در آینده باشد.");
        var localDate = date.Kind == DateTimeKind.Utc ? date.ToLocalTime().Date : date.Date;
        if (await _context.CashDailyBalances.AnyAsync(x => x.AccountId == accountId && x.CurrencyId == currencyId &&
            x.IsClosed && x.JournalDate >= localDate))
            throw new InvalidOperationException("روز صندوق بسته شده است؛ برداشت در آن روز یا قبل از آن مجاز نیست.");
        var movements = await _context.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.CurrencyId == currencyId)
            .GroupBy(x => x.CreatedAt)
            .Select(g => new { Date = g.Key, Amount = g.Sum(x => x.BadehKar - x.TalabKar) })
            .OrderBy(x => x.Date).ToListAsync();
        var balance = movements.Where(x => x.Date <= date).Sum(x => x.Amount);
        if (balance < amount)
            throw new InvalidOperationException("موجودی صندوق/بانک در تاریخ برداشت کافی نیست.");
        foreach (var movement in movements.Where(x => x.Date > date))
        {
            balance += movement.Amount;
            if (balance < amount)
                throw new InvalidOperationException("این برداشتِ گذشته، موجودی صندوق/بانک را در عملیات بعدی منفی می‌کند.");
        }
    }

    private async Task ProtectExistingWithdrawalsAsync(long accountId, long currencyId)
    {
        var firstWithdrawal = await _context.CapitalInvestments.Where(x => !x.IsDeleted && x.IsWithdrawal &&
            x.ReceivingAccountId == accountId && x.CurrencyId == currencyId)
            .Select(x => (DateTime?)x.InvestmentDate).MinAsync();
        if (!firstWithdrawal.HasValue) return;
        var movements = await _context.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.CurrencyId == currencyId)
            .GroupBy(x => x.CreatedAt).Select(g => new { Date = g.Key, Amount = g.Sum(x => x.BadehKar - x.TalabKar) })
            .OrderBy(x => x.Date).ToListAsync();
        decimal balance = 0;
        foreach (var movement in movements)
        {
            balance += movement.Amount;
            if (movement.Date >= firstWithdrawal.Value && balance < 0)
                throw new InvalidOperationException("این تغییر سرمایه، موجودی برداشت‌های ثبت‌شده را منفی می‌کند؛ ابتدا عملیات وابسته را اصلاح کنید.");
        }
    }
}
