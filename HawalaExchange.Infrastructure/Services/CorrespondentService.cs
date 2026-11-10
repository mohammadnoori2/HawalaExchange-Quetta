using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace HawalaExchange.Application.Services
{
    public partial class CorrespondentService : BaseService<Correspondent, CorrespondentDto, CreateCorrespondentDto, UpdateCorrespondentDto>, ICorrespondentService
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
                        RequiresManualExecution = x.RequiresManualExecution,
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
            if(_context.Database.IsNpgsql())
            {
                await using var native=await PostgresProcedures.AccountOperationsAsync(_context,new()
                {
                    ["AccountId"]=null,["PageNumber"]=1,["PageSize"]=10,["CorrespondentId"]=id,["IncludeStatusHeader"]=true,["IncludeBalances"]=true
                },cancellationToken);
                return await ReadStatusPageAsync(native,cancellationToken);
            }
            var connection = (SqlConnection)_context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            var timer = Stopwatch.StartNew();
            var stage = "Connect";
            long previous = 0;
            void RecordStage(string completed)
            {
                var elapsed = timer.ElapsedMilliseconds;
                _logger.LogInformation("Correspondent status Tenant={TenantId} Correspondent={CorrespondentId} Stage={Stage} DurationMs={DurationMs} TotalMs={TotalMs}",
                    _context.CurrentTenantId, id, completed, elapsed - previous, elapsed);
                previous = elapsed;
            }
            try
            {
                if (shouldClose) await connection.OpenAsync(cancellationToken);
                RecordStage(stage);
                stage = "ExecuteProcedure";
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
                RecordStage(stage);
                stage = "ReadResults";
                return await ReadStatusPageAsync(reader,cancellationToken, RecordStage);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Correspondent status cancelled Tenant={TenantId} Correspondent={CorrespondentId} Stage={Stage} TotalMs={TotalMs}", _context.CurrentTenantId, id, stage, timer.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Correspondent status failed Tenant={TenantId} Correspondent={CorrespondentId} Stage={Stage} TotalMs={TotalMs}", _context.CurrentTenantId, id, stage, timer.ElapsedMilliseconds);
                throw;
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private static async Task<CorrespondentStatusPageDto?> ReadStatusPageAsync(DbDataReader reader,CancellationToken cancellationToken, Action<string>? recordStage = null)
        {
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

                recordStage?.Invoke("Header");
                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Balances.Add(new BalanceDto
                    {
                        CurrencyId = reader.GetInt64(0),
                        CurrencyCode = reader.GetString(1),
                        Balance = reader.GetDecimal(2),
                        PendingCommissionDebit = reader.FieldCount > 3 ? reader.GetDecimal(3) : 0m,
                        PendingCommissionCredit = reader.FieldCount > 4 ? reader.GetDecimal(4) : 0m
                    });
                }

                recordStage?.Invoke("Balances");
                await reader.NextResultAsync(cancellationToken);
                result.Operations = await JournalService.ReadAccountOperationsPageAsync(
                    reader, 1, 10, cancellationToken);
                recordStage?.Invoke("Operations");
                return result;
        }

        private static string? ReadNullableString(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

        public override async Task<IEnumerable<CorrespondentDto>> GetAllAsync()
        {
            var entities = await _dbSet.AsNoTracking().Include(x => x.SettlementCurrency).ToListAsync();
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
                if (createDto.HasInitialBalance)
                {
                    await CreateInitialBalancesAsync(
                        accountDto.Id,
                        createDto.InitialBalances ?? [],
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

        private async Task CreateInitialBalancesAsync(long accountId, List<InitialBalanceDto> initialBalances, string correspondentName, string correspondentCode)
        {
            if (initialBalances.Count == 0 || initialBalances.Select(x => x.CurrencyId).Distinct().Count() != initialBalances.Count)
                throw new InvalidOperationException("برای هر ارز فقط یک موجودی افتتاحیه معتبر وارد کنید.");
            var account = await _context.Accounts.SingleAsync(x => x.Id == accountId && x.CorrespondentId != null);
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context, account.CorrespondentId!.Value);
            foreach (var item in initialBalances)
            {
                var request = new CorrespondentOpeningBalanceRequestDto { CurrencyId = item.CurrencyId, Amount = item.Amount,
                    Direction = item.Direction, Description = item.Description };
                ValidateOpening(request);
                await EnsureOpeningCurrencyAvailableAsync(accountId, item.CurrencyId);
                await PostOpeningAsync(account, request, DateTime.UtcNow);
            }
        }

        public async Task<CorrespondentPeriodClosePreviewDto> GetPeriodClosePreviewAsync(long id)
        {
            var correspondent = await _context.Correspondents.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Id, x.Name, x.CreatedAt })
                .SingleOrDefaultAsync()
                ?? throw new KeyNotFoundException("نمایندگی یافت نشد.");
            var last = await _context.CorrespondentAccountPeriods.AsNoTracking()
                .Where(x => x.CorrespondentId == id)
                .OrderByDescending(x => x.PeriodNumber)
                .Select(x => new
                {
                    x.PeriodNumber,
                    x.PeriodTo,
                    Balances = x.Balances.OrderBy(balance => balance.Currency.Code).Select(balance =>
                        new CorrespondentPeriodBalanceDto
                        {
                            CurrencyCode = balance.Currency.Code,
                            TalabKar = balance.TalabKar,
                            BadehKar = balance.BadehKar
                        }).ToList()
                }).FirstOrDefaultAsync();
            var periodFrom = last?.PeriodTo ?? correspondent.CreatedAt;
            var periodTo = DateTime.UtcNow;
            var accountId = await _context.Accounts.AsNoTracking()
                .Where(x => x.CorrespondentId == id && !x.IsArchived)
                .Select(x => (long?)x.Id).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("حساب فعال نمایندگی یافت نشد.");
            var closingBalances = await _context.LedgerEntries.AsNoTracking()
                .Where(x => x.AccountId == accountId && x.CreatedAt <= periodTo)
                .GroupBy(x => new { x.CurrencyId, x.Currency!.Code })
                .Select(x => new
                {
                    x.Key.Code,
                    Net = x.Sum(entry => entry.TalabKar - entry.BadehKar)
                })
                .OrderBy(x => x.Code)
                .ToListAsync();
            var hawalaStats = await _context.Hawalas.AsNoTracking()
                .Where(x => x.CorrespondentId == id && x.CreatedAt <= periodTo &&
                            !_context.CorrespondentAccountPeriodHawalas.Any(closed => closed.HawalaId == x.Id))
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Count(),
                    Receive = group.Count(x => x.HawalaType == "HawalaReceive"),
                    Send = group.Count(x => x.HawalaType == "HawalaSend"),
                    Pending = group.Count(x => x.Status == "Pending"),
                    Paid = group.Count(x => x.Status == "Paid"),
                    Cancelled = group.Count(x => x.Status == "Cancel")
                }).SingleOrDefaultAsync();

            return new CorrespondentPeriodClosePreviewDto
            {
                CorrespondentName = correspondent.Name,
                PeriodNumber = (last?.PeriodNumber ?? 0) + 1,
                PeriodFrom = periodFrom,
                PeriodTo = periodTo,
                HawalaCount = hawalaStats?.Total ?? 0,
                ReceiveCount = hawalaStats?.Receive ?? 0,
                SendCount = hawalaStats?.Send ?? 0,
                PendingCount = hawalaStats?.Pending ?? 0,
                PaidCount = hawalaStats?.Paid ?? 0,
                CancelledCount = hawalaStats?.Cancelled ?? 0,
                OpeningBalances = last?.Balances ?? [],
                ClosingBalances = closingBalances.Select(x => new CorrespondentPeriodBalanceDto
                {
                    CurrencyCode = x.Code,
                    TalabKar = x.Net > 0 ? x.Net : 0,
                    BadehKar = x.Net < 0 ? -x.Net : 0
                }).ToList()
            };
        }

        public async Task<CorrespondentPeriodDto> ClosePeriodAsync(long id, CloseCorrespondentPeriodDto dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await CorrespondentCommissionPeriodLock.AcquireAsync(_context, id);
            var correspondent = await _context.Correspondents.SingleOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException("نمایندگی یافت نشد.");
            var last = await _context.CorrespondentAccountPeriods
                .Where(x => x.CorrespondentId == id).OrderByDescending(x => x.PeriodNumber).FirstOrDefaultAsync();
            var periodFrom = last?.PeriodTo ?? correspondent.CreatedAt;
            var periodTo = DateTime.UtcNow;
            var accountId = await _context.Accounts.Where(x => x.CorrespondentId == id && !x.IsArchived)
                .Select(x => (long?)x.Id).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("حساب فعال نمایندگی یافت نشد.");
            var period = new CorrespondentAccountPeriod
            {
                CorrespondentId = id, PeriodNumber = (last?.PeriodNumber ?? 0) + 1,
                PeriodFrom = periodFrom, PeriodTo = periodTo, Note = dto.Note?.Trim(),
                ClosedBy = _context.RequireCurrentUserId(), ClosedAt = periodTo
            };
            _context.CorrespondentAccountPeriods.Add(period);
            await _context.SaveChangesAsync();
            // Recognize the exact journal documents present at close, not merely their dates.
            // Subsequent backdated calculations and reversals remain pending for the next close.
            if (_context.Database.IsNpgsql()) await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public."CorrespondentCommissionRecognitions" ("TenantId", "AccountId", "TransactionId", "PeriodId")
                SELECT e."TenantId", e."AccountId", e."TransactionId", {period.Id}
                FROM public."LedgerEntries" e
                JOIN public."Transactions" t ON t."TenantId" = e."TenantId" AND t."Id" = e."TransactionId"
                WHERE e."TenantId" = {_context.CurrentTenantId} AND e."AccountId" = {accountId}
                  AND e."CreatedAt" <= {new Npgsql.NpgsqlParameter("periodTo", NpgsqlTypes.NpgsqlDbType.Timestamp) { Value=DateTime.SpecifyKind(periodTo, DateTimeKind.Unspecified) }}
                  AND t."TransactionType" IN ('PeriodicCorrespondentCommission', 'PeriodicOutgoingCommission',
                      'PeriodicForwardingCommission', 'PeriodicCorrespondentCommissionReversal')
                  AND NOT EXISTS (SELECT 1 FROM public."CorrespondentCommissionRecognitions" r
                      WHERE r."TenantId" = e."TenantId" AND r."AccountId" = e."AccountId" AND r."TransactionId" = e."TransactionId")
                GROUP BY e."TenantId", e."AccountId", e."TransactionId";
                """);
            else
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[CorrespondentCommissionRecognitions] ([TenantId], [AccountId], [TransactionId], [PeriodId])
                SELECT e.[TenantId], e.[AccountId], e.[TransactionId], {period.Id}
                FROM [dbo].[LedgerEntries] e
                JOIN [dbo].[Transactions] t ON t.[TenantId] = e.[TenantId] AND t.[Id] = e.[TransactionId]
                WHERE e.[TenantId] = {_context.CurrentTenantId} AND e.[AccountId] = {accountId}
                  AND e.[CreatedAt] <= {periodTo}
                  AND t.[TransactionType] IN (N'PeriodicCorrespondentCommission', N'PeriodicOutgoingCommission',
                      N'PeriodicForwardingCommission', N'PeriodicCorrespondentCommissionReversal')
                  AND NOT EXISTS (SELECT 1 FROM [dbo].[CorrespondentCommissionRecognitions] r
                      WHERE r.[TenantId] = e.[TenantId] AND r.[AccountId] = e.[AccountId] AND r.[TransactionId] = e.[TransactionId])
                GROUP BY e.[TenantId], e.[AccountId], e.[TransactionId];
                """);
            var balances = await _context.LedgerEntries.Where(x => x.AccountId == accountId && x.CreatedAt <= periodTo)
                .GroupBy(x => x.CurrencyId).Select(x => new
                { x.Key, Talab = x.Sum(e => e.TalabKar), Badeh = x.Sum(e => e.BadehKar) }).ToListAsync();
            _context.CorrespondentAccountPeriodBalances.AddRange(balances.Select(x =>
            {
                var net = x.Talab - x.Badeh;
                return new CorrespondentAccountPeriodBalance
                {
                    PeriodId = period.Id,
                    CurrencyId = x.Key,
                    TalabKar = net > 0 ? net : 0,
                    BadehKar = net < 0 ? -net : 0
                };
            }));
            // A newly registered bulk import can have an old hawala date. Capture
            // every not-yet-closed record instead of leaving backdated imports open forever.
            var hawalaQuery = _context.Hawalas.Where(x => x.CorrespondentId == id && x.CreatedAt <= periodTo &&
                !_context.CorrespondentAccountPeriodHawalas.Any(closed => closed.HawalaId == x.Id));
            var hawalaIds = await hawalaQuery.Select(x => x.Id).ToListAsync();
            _context.CorrespondentAccountPeriodHawalas.AddRange(hawalaIds.Select(x =>
                new CorrespondentAccountPeriodHawala { PeriodId = period.Id, HawalaId = x }));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return (await GetPeriodAsync(period.Id))!;
        }

        public async Task<IReadOnlyList<CorrespondentPeriodDto>> GetPeriodsAsync(long id) =>
            await _context.CorrespondentAccountPeriods.AsNoTracking().Where(x => x.CorrespondentId == id)
                .OrderByDescending(x => x.PeriodNumber).Select(x => new CorrespondentPeriodDto
                {
                    Id = x.Id, CorrespondentId = x.CorrespondentId, CorrespondentName = x.Correspondent.Name,
                    PeriodNumber = x.PeriodNumber, PeriodFrom = x.PeriodFrom, PeriodTo = x.PeriodTo,
                    ClosedAt = x.ClosedAt, Note = x.Note, HawalaCount = x.Hawalas.Count,
                    Balances = x.Balances.OrderBy(balance => balance.Currency.Code).Select(balance =>
                        new CorrespondentPeriodBalanceDto
                        {
                            CurrencyCode = balance.Currency.Code,
                            TalabKar = balance.TalabKar,
                            BadehKar = balance.BadehKar
                        }).ToList()
                }).ToListAsync();

        public async Task<CorrespondentPeriodDto?> GetPeriodAsync(long periodId)
        {
            var period = await _context.CorrespondentAccountPeriods.AsNoTracking()
                .Include(x => x.Correspondent).Include(x => x.Balances).ThenInclude(x => x.Currency)
                .Include(x => x.Hawalas).ThenInclude(x => x.Hawala).ThenInclude(x => x.FromCurrency)
                .SingleOrDefaultAsync(x => x.Id == periodId);
            if (period == null) return null;
            var previousPeriodId = await _context.CorrespondentAccountPeriods.AsNoTracking()
                .Where(x => x.CorrespondentId == period.CorrespondentId && x.PeriodNumber < period.PeriodNumber)
                .OrderByDescending(x => x.PeriodNumber)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync();
            var openingBalances = previousPeriodId.HasValue
                ? await _context.CorrespondentAccountPeriodBalances.AsNoTracking()
                    .Where(x => x.PeriodId == previousPeriodId.Value)
                    .OrderBy(x => x.Currency.Code)
                    .Select(balance => new CorrespondentPeriodBalanceDto
                    {
                        CurrencyCode = balance.Currency.Code,
                        TalabKar = balance.TalabKar,
                        BadehKar = balance.BadehKar
                    }).ToListAsync()
                : [];
            return new CorrespondentPeriodDto
            {
                Id = period.Id, CorrespondentId = period.CorrespondentId, CorrespondentName = period.Correspondent.Name,
                PeriodNumber = period.PeriodNumber, PeriodFrom = period.PeriodFrom, PeriodTo = period.PeriodTo,
                ClosedAt = period.ClosedAt, Note = period.Note, HawalaCount = period.Hawalas.Count,
                OpeningBalances = openingBalances,
                Balances = period.Balances.Select(x => new CorrespondentPeriodBalanceDto
                    { CurrencyCode = x.Currency.Code, TalabKar = x.TalabKar, BadehKar = x.BadehKar }).ToList(),
                Hawalas = period.Hawalas.OrderBy(x => x.Hawala.CreatedAt).Select(x => _mapper.Map<HawalaDto>(x.Hawala)).ToList()
            };
        }

    }
}
