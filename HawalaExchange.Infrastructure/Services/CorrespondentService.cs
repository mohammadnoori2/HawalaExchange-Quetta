using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Data;

namespace HawalaExchange.Application.Services
{
    public class CorrespondentService : BaseService<Correspondent, CorrespondentDto, CreateCorrespondentDto, UpdateCorrespondentDto>, ICorrespondentService
    {
        private readonly IAccountService _accountService;
        private readonly ILedgerService _ledgerService;
        private readonly IAuditLogService _auditLogService;
        private readonly ILogger<CorrespondentService> _logger;

        // ===== سازنده با تزریق وابستگی‌ها =====
        public CorrespondentService(
            ApplicationDbContext context,
            IMapper mapper,
            IAccountService accountService,
            ILedgerService ledgerService,
            IAuditLogService auditLogService,
            ILogger<CorrespondentService> logger)
            : base(context, mapper)
        {
            _accountService = accountService;
            _ledgerService = ledgerService;
            _auditLogService = auditLogService;
            _logger = logger;
        }

        // ===== متدهای موجود =====
        public override async Task<CorrespondentDto?> GetByIdAsync(long id)
        {
            var entity = await _dbSet
                .AsNoTracking()
                .Include(x => x.SettlementCurrency)
                .FirstOrDefaultAsync(x => x.Id == id);
            return entity == null ? null : _mapper.Map<CorrespondentDto>(entity);
        }

        public Task<CorrespondentDetailsPageDto?> GetDetailsPageAsync(
            long id,
            CancellationToken cancellationToken = default) =>
            _dbSet
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new CorrespondentDetailsPageDto
                {
                    Correspondent = new CorrespondentDto
                    {
                        Id = x.Id,
                        Code = x.Code,
                        Name = x.Name,
                        Country = x.Country,
                        City = x.City,
                        PhoneNumber = x.PhoneNumber,
                        Address = x.Address,
                        IsArchived = x.IsArchived,
                        Remarks = x.Remarks,
                        SettlementCurrencyId = x.SettlementCurrencyId,
                        SettlementCurrencyCode = x.SettlementCurrency == null
                            ? null
                            : x.SettlementCurrency.Code,
                        CommissionMethod = x.CommissionMethod,
                        CreatedAt = x.CreatedAt
                    },
                    AccountId = _context.Accounts
                        .Where(account => account.CorrespondentId == x.Id && !account.IsArchived)
                        .Select(account => (long?)account.Id)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(cancellationToken);

        public async Task<CorrespondentStatusPageDto?> GetStatusPageAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            var connection = (SqlConnection)_context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "[dbo].[usp_GetAccountOperationsPage_v1]";
                command.CommandType = CommandType.StoredProcedure;
                command.CommandTimeout = 60;
                command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = _context.CurrentTenantId;
                command.Parameters.Add("@AccountId", SqlDbType.BigInt).Value = DBNull.Value;
                command.Parameters.Add("@PageNumber", SqlDbType.Int).Value = 1;
                command.Parameters.Add("@PageSize", SqlDbType.Int).Value = 10;
                command.Parameters.Add("@CorrespondentId", SqlDbType.BigInt).Value = id;
                command.Parameters.Add("@IncludeStatusHeader", SqlDbType.Bit).Value = true;
                command.Parameters.Add("@IncludeBalances", SqlDbType.Bit).Value = true;
                if (_context.Database.CurrentTransaction?.GetDbTransaction() is SqlTransaction transaction)
                    command.Transaction = transaction;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                var result = new CorrespondentStatusPageDto
                {
                    Correspondent = new CorrespondentDto
                    {
                        Id = reader.GetInt64(0),
                        Code = reader.GetString(1),
                        Name = reader.GetString(2),
                        Country = ReadNullableString(reader, 3),
                        City = ReadNullableString(reader, 4),
                        PhoneNumber = ReadNullableString(reader, 5),
                        Address = ReadNullableString(reader, 6),
                        IsArchived = reader.GetBoolean(7),
                        Remarks = ReadNullableString(reader, 8),
                        SettlementCurrencyId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                        SettlementCurrencyCode = ReadNullableString(reader, 10),
                        CommissionMethod = reader.GetString(11),
                        CreatedAt = reader.GetDateTime(12)
                    },
                    AccountId = reader.IsDBNull(13) ? null : reader.GetInt64(13)
                };

                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Balances.Add(new BalanceDto
                    {
                        CurrencyId = reader.GetInt64(0),
                        CurrencyCode = reader.GetString(1),
                        Balance = reader.GetDecimal(2)
                    });
                }

                await reader.NextResultAsync(cancellationToken);
                result.Operations = await JournalService.ReadAccountOperationsPageAsync(
                    reader, 1, 10, cancellationToken);
                return result;
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private static string? ReadNullableString(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

        public override async Task<IEnumerable<CorrespondentDto>> GetAllAsync()
        {
            var entities = await _dbSet.Include(x => x.SettlementCurrency).ToListAsync();
            return _mapper.Map<IEnumerable<CorrespondentDto>>(entities);
        }

        public async Task<CorrespondentDto?> GetByCodeAsync(string code)
        {
            var entity = await _dbSet.Include(x => x.SettlementCurrency).FirstOrDefaultAsync(c => c.Code == code);
            return entity == null ? null : _mapper.Map<CorrespondentDto>(entity);
        }

        public async Task<IEnumerable<CorrespondentDto>> GetByCountryAsync(string country)
        {
            var entities = await _dbSet
                .Include(x => x.SettlementCurrency)
                .Where(c => c.Country == country && !c.IsArchived)
                .ToListAsync();
            return _mapper.Map<IEnumerable<CorrespondentDto>>(entities);
        }

        public async Task<IEnumerable<CorrespondentDto>> GetActiveAsync()
        {
            var entities = await _dbSet.Include(x => x.SettlementCurrency).Where(c => !c.IsArchived).ToListAsync();
            return _mapper.Map<IEnumerable<CorrespondentDto>>(entities);
        }

        public async Task<CorrespondentDto> ArchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Correspondent with ID {id} not found.");

            entity.IsArchived = true;
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                action: "ARCHIVE",
                tableName: "Correspondents",
                recordId: id,
                oldValue: null,
                newValue: $"نماینده {entity.Name} بایگانی شد"
            );

            return _mapper.Map<CorrespondentDto>(entity);
        }

        public async Task<CorrespondentDto> UnarchiveAsync(long id)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity == null) throw new KeyNotFoundException($"Correspondent with ID {id} not found.");

            entity.IsArchived = false;
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                action: "UNARCHIVE",
                tableName: "Correspondents",
                recordId: id,
                oldValue: null,
                newValue: $"نماینده {entity.Name} از بایگانی خارج شد"
            );

            return _mapper.Map<CorrespondentDto>(entity);
        }

        protected override async Task ValidateCreateAsync(Correspondent entity, CreateCorrespondentDto dto)
        {
            await ValidateSettlementCurrencyAsync(entity.SettlementCurrencyId);
            ValidateCommissionMethod(entity.CommissionMethod);
            entity.Code = await GenerateCorrespondentCodeAsync();
        }

        protected override async Task ValidateUpdateAsync(Correspondent entity, UpdateCorrespondentDto dto)
        {
            await ValidateSettlementCurrencyAsync(entity.SettlementCurrencyId);
            ValidateCommissionMethod(entity.CommissionMethod);
        }

        private static void ValidateCommissionMethod(string method)
        {
            if (method is not ("PerTransaction" or "PeriodicPerLakh"))
                throw new InvalidOperationException("روش محاسبه کمیشن معتبر نیست.");
        }

        private async Task ValidateSettlementCurrencyAsync(long? currencyId)
        {
            if (!currencyId.HasValue) return;
            if (!await _context.Currencies.AnyAsync(x => x.Id == currencyId.Value && x.IsActive))
                throw new InvalidOperationException("ارز توافقی انتخاب‌شده معتبر و فعال نیست.");
        }

        // ===== بازنویسی متد CreateAsync با پشتیبانی از موجودی اولیه =====
        public override async Task<CorrespondentDto> CreateAsync(CreateCorrespondentDto createDto)
        {
            await using var transaction = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            try
            {
                // ۱. ایجاد نماینده با استفاده از متد پایه
                var correspondentDto = await base.CreateAsync(createDto);

                // ۲. ایجاد حساب مرتبط با نماینده
                var accountDto = await CreateCorrespondentAccountAsync(correspondentDto.Id, correspondentDto.Name);

                // ۳. ثبت موجودی اولیه (در صورت وجود)
                if (createDto.HasInitialBalance && createDto.InitialBalances != null && createDto.InitialBalances.Any())
                {
                    await CreateInitialBalancesAsync(
                        accountDto.Id,
                        createDto.InitialBalances,
                        correspondentDto.Name,
                        correspondentDto.Code
                    );
                }

                // ۴. ثبت لاگ ایجاد نماینده
                await _auditLogService.LogAsync(
                    action: "CREATE",
                    tableName: "Correspondents",
                    recordId: correspondentDto.Id,
                    oldValue: null,
                    newValue: $"نماینده {correspondentDto.Name} با کد {correspondentDto.Code} ایجاد شد"
                );

                if (transaction != null)
                    await transaction.CommitAsync();

                return correspondentDto;
            }
            catch (Exception ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                _logger.LogError(ex, "خطا در ایجاد نماینده و حساب مرتبط");
                throw;
            }
        }

        // ===== متدهای کمکی خصوصی =====

        private async Task<string> GenerateCorrespondentCodeAsync()
        {
            const string prefix = "AG-";
            var existingCodes = await _dbSet
                .Where(c => c.Code.StartsWith(prefix))
                .Select(c => c.Code)
                .ToListAsync();

            var lastNumber = existingCodes
                .Select(code => int.TryParse(code[prefix.Length..], out var number) ? number : 0)
                .DefaultIfEmpty(0)
                .Max();

            return $"{prefix}{lastNumber + 1:D3}";
        }

        private async Task<AccountDto> CreateCorrespondentAccountAsync(long correspondentId, string correspondentName)
        {
            var createAccountDto = new CreateAccountDto
            {
                AccountType = "Correspondent",
                AccountName = $"نماینده: {correspondentName}",
                ReferenceType = "Correspondent",
                ReferenceId = correspondentId,
                HasInitialBalance = false,
                InitialBalances = null
            };

            var accountDto = await _accountService.CreateAsync(createAccountDto);

            await _auditLogService.LogAsync(
                action: "CREATE",
                tableName: "Accounts",
                recordId: accountDto.Id,
                oldValue: null,
                newValue: $"حساب مرتبط با نماینده {correspondentName} (کد: {accountDto.AccountCode}) ایجاد شد"
            );

            return accountDto;
        }

        private async Task CreateInitialBalancesAsync(
            long accountId,
            List<InitialBalanceDto> initialBalances,
            string correspondentName,
            string correspondentCode)
        {
            // ایجاد تراکنش OpeningBalance
            var openingTransaction = new Transaction
            {
                TransactionNo = await GenerateOpeningTransactionNumberAsync(),
                TransactionType = "OpeningBalance",
                BranchId = await _context.GetDefaultBranchIdAsync(),
                Status = "Paid",
                Remarks = $"موجودی اولیه برای نماینده {correspondentName} (کد: {correspondentCode})",
                CreatedBy = _context.RequireCurrentUserId(),
                CreatedAt = DateTime.UtcNow
            };

            await _context.Transactions.AddAsync(openingTransaction);
            await _context.SaveChangesAsync();

            foreach (var initialBalance in initialBalances)
            {
                // اعتبارسنجی حساب طرف مقابل
                var oppositeAccount = await _context.Accounts.FindAsync(initialBalance.OppositeAccountId);
                if (oppositeAccount == null)
                    throw new InvalidOperationException($"حساب طرف مقابل با شناسه {initialBalance.OppositeAccountId} یافت نشد");

                decimal correspondentTalabKar = 0, correspondentBadehKar = 0;
                decimal oppositeTalabKar = 0, oppositeBadehKar = 0;

                // منطق مشابه CustomerService
                if (initialBalance.Direction == "Debit") // بدهکار
                {
                    correspondentTalabKar = 0;
                    correspondentBadehKar = initialBalance.Amount;
                    oppositeTalabKar = initialBalance.Amount;
                    oppositeBadehKar = 0;
                }
                else // Credit (بستانکار)
                {
                    correspondentTalabKar = initialBalance.Amount;
                    correspondentBadehKar = 0;
                    oppositeTalabKar = 0;
                    oppositeBadehKar = initialBalance.Amount;
                }

                // ثبت برای حساب نماینده
                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                {
                    TransactionId = openingTransaction.Id,
                    AccountId = accountId,
                    CurrencyId = initialBalance.CurrencyId,
                    TalabKar = correspondentTalabKar,
                    BadehKar = correspondentBadehKar,
                    Description = $"موجودی اولیه نماینده: {initialBalance.Description ?? "بدون توضیح"}"
                });

                // ثبت برای حساب طرف مقابل
                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                {
                    TransactionId = openingTransaction.Id,
                    AccountId = initialBalance.OppositeAccountId,
                    CurrencyId = initialBalance.CurrencyId,
                    TalabKar = oppositeTalabKar,
                    BadehKar = oppositeBadehKar,
                    Description = $"طرف مقابل موجودی اولیه نماینده {correspondentName}"
                });
            }

            // ثبت لاگ
            await _auditLogService.LogAsync(
                action: "CREATE",
                tableName: "Transactions",
                recordId: openingTransaction.Id,
                oldValue: null,
                newValue: $"تراکنش موجودی اولیه برای نماینده {correspondentName} با {initialBalances.Count} رکورد ایجاد شد"
            );
        }

        private async Task<string> GenerateOpeningTransactionNumberAsync()
        {
            var datePart = DateTime.Now.ToString("yyyyMMdd");
            var lastTransaction = await _context.Transactions
                .Where(t => t.TransactionNo.StartsWith($"OP-{datePart}"))
                .OrderByDescending(t => t.TransactionNo)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastTransaction != null)
            {
                var parts = lastTransaction.TransactionNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
                {
                    nextNumber = lastNumber + 1;
                }
            }

            return $"OP-{datePart}-{nextNumber:D4}";
        }

    }
}
