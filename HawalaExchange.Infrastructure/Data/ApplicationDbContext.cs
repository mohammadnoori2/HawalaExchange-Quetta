
using HawalaExchange.Domain.Entities;
using HawalaExchange.Application.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<long>, long>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ===== DbSets موجود =====
        public DbSet<Branch> Branches { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Correspondent> Correspondents { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<TransactionDetail> TransactionDetails { get; set; }
        public DbSet<LedgerEntry> LedgerEntries { get; set; }
        public DbSet<Transfer> Transfers { get; set; }
        public DbSet<ExchangeRate> ExchangeRates { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<AccountBadehkarLimit> AccountBadehkarLimits { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Hawala> Hawalas { get; set; }
        public DbSet<DailyReport> DailyReports { get; set; }
        public DbSet<TransactionReport> TransactionReports { get; set; }
        public DbSet<CommissionReport> CommissionReports { get; set; }
        public DbSet<TrialBalance> TrialBalances { get; set; }
        public DbSet<PaymentLocation> PaymentLocations { get; set; }

        public DbSet<CapitalInvestment> CapitalInvestments { get; set; }
        public DbSet<AccountMoneyOperation> AccountMoneyOperations { get; set; }
        public DbSet<MoneyExchangeOperation> MoneyExchangeOperations { get; set; }
        public DbSet<CompanySetting> CompanySettings { get; set; }
        public DbSet<CashDailyBalance> CashDailyBalances { get; set; }

        public const string CurrencyInventoryAccountCode = "1201";
        public const string CurrencySaleLiabilityAccountCode = "2101";
        public const string PendingHawalaAccountCode = "2102";

        /// <summary>
        /// Creates and repairs application-owned accounts without relying on a migration.
        /// It also separates the historical 2101 collision between currency-sale
        /// liabilities and pending incoming hawalas.
        /// </summary>
        public async Task EnsureSystemAccountsAsync(CancellationToken cancellationToken = default)
        {
            var systemCodes = new[]
            {
                CurrencyInventoryAccountCode,
                CurrencySaleLiabilityAccountCode,
                PendingHawalaAccountCode
            };
            var accounts = await Accounts
                .Where(x => systemCodes.Contains(x.AccountCode))
                .ToListAsync(cancellationToken);

            var inventoryAccount = accounts.FirstOrDefault(
                x => x.AccountCode == CurrencyInventoryAccountCode);
            var liabilityAccount = accounts.FirstOrDefault(
                x => x.AccountCode == CurrencySaleLiabilityAccountCode);
            var pendingHawalaAccount = accounts.FirstOrDefault(
                x => x.AccountCode == PendingHawalaAccountCode);

            var legacyPendingAccount = liabilityAccount != null &&
                (liabilityAccount.AccountType == "PendingHawala" ||
                 liabilityAccount.AccountName == "حواله‌های اجرا نشده");

            if (legacyPendingAccount && pendingHawalaAccount == null)
            {
                liabilityAccount!.AccountCode = PendingHawalaAccountCode;
                pendingHawalaAccount = liabilityAccount;
                liabilityAccount = null;

                // Release the unique 2101 code before creating its correct account.
                await SaveChangesAsync(cancellationToken);
            }

            inventoryAccount ??= AddSystemAccount(
                CurrencyInventoryAccountCode,
                "موجودی ارز به بهای تمام‌شده",
                "Asset");
            liabilityAccount ??= AddSystemAccount(
                CurrencySaleLiabilityAccountCode,
                "تعهد فروش ارز",
                "Liability");
            pendingHawalaAccount ??= AddSystemAccount(
                PendingHawalaAccountCode,
                "حواله‌های اجرا نشده",
                "PendingHawala");

            NormalizeSystemAccount(
                inventoryAccount,
                "موجودی ارز به بهای تمام‌شده",
                "Asset");
            NormalizeSystemAccount(
                liabilityAccount,
                "تعهد فروش ارز",
                "Liability");
            NormalizeSystemAccount(
                pendingHawalaAccount,
                "حواله‌های اجرا نشده",
                "PendingHawala");
            await SaveChangesAsync(cancellationToken);

            var hawalaEntriesOnLiability = await LedgerEntries
                .Where(x =>
                    x.AccountId == liabilityAccount.Id &&
                    x.HawalaId != null &&
                    x.MoneyExchangeOperationId == null)
                .ToListAsync(cancellationToken);
            foreach (var entry in hawalaEntriesOnLiability)
                entry.AccountId = pendingHawalaAccount.Id;

            var exchangeEntriesOnPendingHawala = await LedgerEntries
                .Where(x =>
                    x.AccountId == pendingHawalaAccount.Id &&
                    x.MoneyExchangeOperationId != null)
                .ToListAsync(cancellationToken);
            foreach (var entry in exchangeEntriesOnPendingHawala)
                entry.AccountId = liabilityAccount.Id;

            if (hawalaEntriesOnLiability.Count > 0 ||
                exchangeEntriesOnPendingHawala.Count > 0)
            {
                await SaveChangesAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Creates the daily cash-balance snapshot table for existing installations.
        /// This intentionally lives in the DbContext startup repair path so pulling the
        /// project does not depend on adding a hand-written migration.
        /// </summary>
        public async Task EnsureCashDailyBalanceSchemaAsync(
            CancellationToken cancellationToken = default)
        {
            if (!Database.IsRelational() ||
                !string.Equals(
                    Database.ProviderName,
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    StringComparison.Ordinal))
            {
                return;
            }

            await Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[dbo].[CashDailyBalances]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[CashDailyBalances]
                    (
                        [Id] BIGINT IDENTITY(1,1) NOT NULL,
                        [JournalDate] DATE NOT NULL,
                        [AccountId] BIGINT NOT NULL,
                        [CurrencyId] BIGINT NOT NULL,
                        [OpeningBalance] DECIMAL(18,4) NOT NULL,
                        [ClosingBalance] DECIMAL(18,4) NULL,
                        [IsClosed] BIT NOT NULL
                            CONSTRAINT [DF_CashDailyBalances_IsClosed] DEFAULT (0),
                        [ClosedAt] DATETIME2 NULL,
                        [CreatedAt] DATETIME2 NOT NULL
                            CONSTRAINT [DF_CashDailyBalances_CreatedAt] DEFAULT (GETUTCDATE()),
                        [ModifiedAt] DATETIME2 NULL,
                        CONSTRAINT [PK_CashDailyBalances] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_CashDailyBalances_Accounts_AccountId]
                            FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
                        CONSTRAINT [FK_CashDailyBalances_Currencies_CurrencyId]
                            FOREIGN KEY ([CurrencyId]) REFERENCES [dbo].[Currencies] ([Id])
                    );

                    CREATE UNIQUE INDEX [IX_CashDailyBalances_JournalDate_AccountId_CurrencyId]
                        ON [dbo].[CashDailyBalances] ([JournalDate], [AccountId], [CurrencyId]);
                END;
                """,
                cancellationToken);
        }

        private Account AddSystemAccount(
            string accountCode,
            string accountName,
            string accountType)
        {
            var account = new Account
            {
                AccountCode = accountCode,
                AccountName = accountName,
                AccountType = accountType,
                ReferenceType = null,
                ReferenceId = null,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow
            };
            Accounts.Add(account);
            return account;
        }

        private static void NormalizeSystemAccount(
            Account account,
            string accountName,
            string accountType)
        {
            account.AccountName = accountName;
            account.AccountType = accountType;
            account.ReferenceType = null;
            account.ReferenceId = null;
            account.IsArchived = false;
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ValidateAccountDebtLimits();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            await ValidateAccountDebtLimitsAsync(cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ValidateAccountDebtLimits()
        {
            var changes = GetPendingLedgerBalanceChanges();
            if (changes.Count == 0)
                return;

            var accountIds = changes.Select(x => x.AccountId).Distinct().ToList();
            var accounts = Accounts
                .AsNoTracking()
                .Where(x => accountIds.Contains(x.Id))
                .ToDictionary(x => x.Id);
            var limits = AccountBadehkarLimits
                .AsNoTracking()
                .Include(x => x.Currency)
                .Where(x => x.IsActive && accountIds.Contains(x.AccountId))
                .ToList();

            ValidateProjectedDebts(
                changes,
                accounts,
                limits,
                (accountId, currencyId) => LedgerEntries
                    .AsNoTracking()
                    .Where(x => x.AccountId == accountId && x.CurrencyId == currencyId)
                    .Sum(x => (decimal?)(x.TalabKar - x.BadehKar)) ?? 0m);
        }

        private async Task ValidateAccountDebtLimitsAsync(CancellationToken cancellationToken)
        {
            var changes = GetPendingLedgerBalanceChanges();
            if (changes.Count == 0)
                return;

            var accountIds = changes.Select(x => x.AccountId).Distinct().ToList();
            var accounts = await Accounts
                .AsNoTracking()
                .Where(x => accountIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            var limits = await AccountBadehkarLimits
                .AsNoTracking()
                .Include(x => x.Currency)
                .Where(x => x.IsActive && accountIds.Contains(x.AccountId))
                .ToListAsync(cancellationToken);

            foreach (var changeGroup in changes.GroupBy(x => new { x.AccountId, x.CurrencyId }))
            {
                if (!accounts.TryGetValue(changeGroup.Key.AccountId, out var account) ||
                    !IsCustomerOrCorrespondentAccount(account))
                    continue;

                var limit = limits.FirstOrDefault(x =>
                    x.AccountId == changeGroup.Key.AccountId &&
                    x.CurrencyId == changeGroup.Key.CurrencyId);
                if (limit == null)
                    continue;

                var databaseBalance = await LedgerEntries
                    .AsNoTracking()
                    .Where(x =>
                        x.AccountId == changeGroup.Key.AccountId &&
                        x.CurrencyId == changeGroup.Key.CurrencyId)
                    .SumAsync(x => (decimal?)(x.TalabKar - x.BadehKar), cancellationToken) ?? 0m;
                ThrowIfLimitExceeded(
                    account,
                    limit,
                    databaseBalance,
                    databaseBalance + changeGroup.Sum(x => x.Delta));
            }
        }

        private static void ValidateProjectedDebts(
            IReadOnlyCollection<LedgerBalanceChange> changes,
            IReadOnlyDictionary<long, Account> accounts,
            IReadOnlyCollection<AccountBadehkarLimit> limits,
            Func<long, long, decimal> getDatabaseBalance)
        {
            foreach (var changeGroup in changes.GroupBy(x => new { x.AccountId, x.CurrencyId }))
            {
                if (!accounts.TryGetValue(changeGroup.Key.AccountId, out var account) ||
                    !IsCustomerOrCorrespondentAccount(account))
                    continue;

                var limit = limits.FirstOrDefault(x =>
                    x.AccountId == changeGroup.Key.AccountId &&
                    x.CurrencyId == changeGroup.Key.CurrencyId);
                if (limit == null)
                    continue;

                var databaseBalance = getDatabaseBalance(
                    changeGroup.Key.AccountId,
                    changeGroup.Key.CurrencyId);
                ThrowIfLimitExceeded(
                    account,
                    limit,
                    databaseBalance,
                    databaseBalance + changeGroup.Sum(x => x.Delta));
            }
        }

        private List<LedgerBalanceChange> GetPendingLedgerBalanceChanges()
        {
            var changes = new List<LedgerBalanceChange>();
            foreach (var entry in ChangeTracker.Entries<LedgerEntry>())
            {
                if (entry.State == EntityState.Added)
                {
                    changes.Add(new LedgerBalanceChange(
                        entry.Entity.AccountId,
                        entry.Entity.CurrencyId,
                        entry.Entity.TalabKar - entry.Entity.BadehKar));
                }
                else if (entry.State == EntityState.Deleted)
                {
                    changes.Add(new LedgerBalanceChange(
                        entry.OriginalValues.GetValue<long>(nameof(LedgerEntry.AccountId)),
                        entry.OriginalValues.GetValue<long>(nameof(LedgerEntry.CurrencyId)),
                        -(entry.OriginalValues.GetValue<decimal>(nameof(LedgerEntry.TalabKar)) -
                          entry.OriginalValues.GetValue<decimal>(nameof(LedgerEntry.BadehKar)))));
                }
                else if (entry.State == EntityState.Modified)
                {
                    changes.Add(new LedgerBalanceChange(
                        entry.OriginalValues.GetValue<long>(nameof(LedgerEntry.AccountId)),
                        entry.OriginalValues.GetValue<long>(nameof(LedgerEntry.CurrencyId)),
                        -(entry.OriginalValues.GetValue<decimal>(nameof(LedgerEntry.TalabKar)) -
                          entry.OriginalValues.GetValue<decimal>(nameof(LedgerEntry.BadehKar)))));
                    changes.Add(new LedgerBalanceChange(
                        entry.Entity.AccountId,
                        entry.Entity.CurrencyId,
                        entry.Entity.TalabKar - entry.Entity.BadehKar));
                }
            }

            return changes;
        }

        private static void ThrowIfLimitExceeded(
            Account account,
            AccountBadehkarLimit limit,
            decimal currentBalance,
            decimal projectedBalance)
        {
            var currentDebt = Math.Max(-currentBalance, 0m);
            var projectedDebt = Math.Max(-projectedBalance, 0m);
            if (projectedDebt <= limit.BadehkarLimit || projectedDebt <= currentDebt)
                return;

            var currencyCode = limit.Currency?.Code ?? limit.CurrencyId.ToString();
            throw new InvalidOperationException(
                $"سقف بدهکاری حساب «{account.AccountName}» در ارز {currencyCode} تجاوز می‌شود. " +
                $"سقف تعیین‌شده: {AmountValueHelper.Format(limit.BadehkarLimit)}، " +
                $"بدهکاری بعد از عملیات: {AmountValueHelper.Format(projectedDebt)}.");
        }

        private static bool IsCustomerOrCorrespondentAccount(Account account) =>
            account.AccountType is "Customer" or "Correspondent" or "مشتری" or "نماینده" or "نمایندگی" ||
            account.ReferenceType is "Customer" or "Correspondent";

        private sealed record LedgerBalanceChange(
            long AccountId,
            long CurrencyId,
            decimal Delta);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureIndexes(modelBuilder);
            ConfigureDefaultValues(modelBuilder);
            ConfigureDecimalPrecision(modelBuilder);
            ConfigureLedgerConstraints(modelBuilder);
            ConfigureRelationships(modelBuilder);
            ConfigureReportEntities(modelBuilder);
            ConfigureHawalaEntity(modelBuilder);
            SeedData(modelBuilder);
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.Hawala)
                .WithMany()
                .HasForeignKey(x => x.HawalaId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<CompanySetting>(entity =>
            {
                entity.Property(x => x.CompanyName)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(x => x.LogoPath)
                    .HasMaxLength(500);

                entity.Property(x => x.PhoneNumber)
                    .HasMaxLength(50);

                entity.Property(x => x.WhatsAppNumber)
                    .HasMaxLength(50);

                entity.Property(x => x.TelegramUserName)
                    .HasMaxLength(100);

                entity.Property(x => x.Address)
                    .HasMaxLength(500);

                entity.Property(x => x.FooterNote)
                    .HasMaxLength(1000);

                entity.HasOne(x => x.DefaultProfitCurrency)
                    .WithMany()
                    .HasForeignKey(x => x.DefaultProfitCurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        // ==========================================
        // تنظیمات جداول Identity (با کلید long)
        // ==========================================
        private static void ConfigureIdentityTables(ModelBuilder modelBuilder)
        {
            // ✅ IdentityUserLogin - کلید ترکیبی
            modelBuilder.Entity<IdentityUserLogin<long>>(entity =>
            {
                entity.ToTable("UserLogins");
                entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });
            });

            // ✅ IdentityUserRole - کلید ترکیبی
            modelBuilder.Entity<IdentityUserRole<long>>(entity =>
            {
                entity.ToTable("UserRoles");
                entity.HasKey(e => new { e.UserId, e.RoleId });
            });

            // ✅ IdentityUserToken - کلید ترکیبی
            modelBuilder.Entity<IdentityUserToken<long>>(entity =>
            {
                entity.ToTable("UserTokens");
                entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });
            });

            // ✅ IdentityUserClaim - کلید اصلی
            modelBuilder.Entity<IdentityUserClaim<long>>(entity =>
            {
                entity.ToTable("UserClaims");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
            });

            // ✅ IdentityRoleClaim - کلید اصلی
            modelBuilder.Entity<IdentityRoleClaim<long>>(entity =>
            {
                entity.ToTable("RoleClaims");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
            });
        }

        // ==========================================
        // تنظیمات Identity (User و Role)
        // ==========================================
        private static void ConfigureIdentity(ModelBuilder modelBuilder)
        {
            // تنظیمات ApplicationUser
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.ToTable("Users");
                entity.HasIndex(u => u.UserName).IsUnique();
                entity.Property(u => u.FullName).HasMaxLength(200);
                entity.Property(u => u.IsActive).HasDefaultValue(true);
                entity.Property(u => u.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

                // رابطه با Branch
                entity.HasOne(u => u.Branch)
                    .WithMany(b => b.Users)
                    .HasForeignKey(u => u.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // تنظیمات IdentityRole
            modelBuilder.Entity<IdentityRole<long>>(entity =>
            {
                entity.ToTable("Roles");
                entity.HasIndex(r => r.Name).IsUnique();
            });
        }

        // ==========================================
        // ایندکس‌ها
        // ==========================================
        private static void ConfigureIndexes(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Branch>().HasIndex(x => x.Code).IsUnique();
            modelBuilder.Entity<Customer>().HasIndex(x => x.CustomerCode).IsUnique();
            modelBuilder.Entity<Correspondent>().HasIndex(x => x.Code).IsUnique();
            modelBuilder.Entity<Currency>().HasIndex(x => x.Code).IsUnique();
            modelBuilder.Entity<Account>().HasIndex(x => x.AccountCode).IsUnique();
            modelBuilder.Entity<Transaction>().HasIndex(x => x.TransactionNo).IsUnique();
            modelBuilder.Entity<AccountBadehkarLimit>().HasIndex(x => new { x.AccountId, x.CurrencyId }).IsUnique();
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.AccountId, x.CurrencyId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => x.TransactionId);
            modelBuilder.Entity<ExchangeRate>().HasIndex(x => new { x.FromCurrencyId, x.ToCurrencyId, x.EffectiveDate });
            modelBuilder.Entity<Document>().HasIndex(x => new { x.EntityType, x.EntityId });
            modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.TableName, x.RecordId });
            modelBuilder.Entity<Hawala>().HasIndex(x => new { x.HawalaType, x.Status });
            modelBuilder.Entity<Hawala>()
                .HasIndex(x => new { x.CorrespondentId, x.HawalaType, x.Number })
                .IsUnique();
            modelBuilder.Entity<Hawala>()
                .HasIndex(x => x.SourceHawalaId)
                .IsUnique()
                .HasFilter("[SourceHawalaId] IS NOT NULL");
            modelBuilder.Entity<Hawala>().HasIndex(x => x.ReferenceNumber);
            modelBuilder.Entity<Hawala>().HasIndex(x => x.CreatedAt);
            modelBuilder.Entity<DailyReport>().HasIndex(r => new { r.Date, r.BranchId });
            modelBuilder.Entity<TransactionReport>().HasIndex(r => r.TransactionNo);
            modelBuilder.Entity<TransactionReport>().HasIndex(r => r.CreatedAt);
            modelBuilder.Entity<CommissionReport>().HasIndex(r => new { r.Date, r.BranchId });
            modelBuilder.Entity<TrialBalance>().HasIndex(r => new { r.AsOfDate, r.AccountId });
            modelBuilder.Entity<CashDailyBalance>()
                .HasIndex(x => new { x.JournalDate, x.AccountId, x.CurrencyId })
                .IsUnique();
        }

        // ==========================================
        // مقادیر پیش‌فرض
        // ==========================================
        private static void ConfigureDefaultValues(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Branch>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Currency>().Property(x => x.DecimalPlaces).HasDefaultValue(2);
            modelBuilder.Entity<Currency>().Property(x => x.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Currency>().Property(x => x.QuotationPriority).HasDefaultValue(1000);
            modelBuilder.Entity<Account>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Customer>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Correspondent>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<AccountBadehkarLimit>().Property(x => x.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.OperationType).HasDefaultValue("Treasury");
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.ProfitStatus).HasDefaultValue("NotCalculated");
            modelBuilder.Entity<CashDailyBalance>().Property(x => x.IsClosed).HasDefaultValue(false);
            modelBuilder.Entity<CashDailyBalance>().Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<Hawala>()
                .Property(x => x.Status)
                .HasDefaultValue("Pending");

            modelBuilder.Entity<Hawala>()
                .Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<DailyReport>()
                .Property(r => r.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<TransactionReport>()
                .Property(r => r.ReportGeneratedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<CommissionReport>()
                .Property(r => r.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<TrialBalance>()
                .Property(r => r.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
        }

        // ==========================================
        // دقت اعداد (Decimal Precision)
        // ==========================================
        private static void ConfigureDecimalPrecision(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LedgerEntry>().Property(x => x.TalabKar).HasPrecision(18, 4);
            modelBuilder.Entity<LedgerEntry>().Property(x => x.BadehKar).HasPrecision(18, 4);
            modelBuilder.Entity<TransactionDetail>().Property(x => x.FromAmount).HasPrecision(18, 4);
            modelBuilder.Entity<TransactionDetail>().Property(x => x.ToAmount).HasPrecision(18, 4);
            modelBuilder.Entity<TransactionDetail>().Property(x => x.ExchangeRate).HasPrecision(18, 8);
            modelBuilder.Entity<TransactionDetail>().Property(x => x.TransferAmount).HasPrecision(18, 4);
            modelBuilder.Entity<TransactionDetail>().Property(x => x.CommissionAmount).HasPrecision(18, 4);
            modelBuilder.Entity<TransactionDetail>().Property(x => x.AgentCommissionAmount).HasPrecision(18, 4);
            modelBuilder.Entity<Transfer>().Property(x => x.Amount).HasPrecision(18, 4);
            modelBuilder.Entity<ExchangeRate>().Property(x => x.BuyRate).HasPrecision(18, 8);
            modelBuilder.Entity<ExchangeRate>().Property(x => x.SellRate).HasPrecision(18, 8);
            modelBuilder.Entity<Expense>().Property(x => x.Amount).HasPrecision(18, 4);
            modelBuilder.Entity<AccountBadehkarLimit>().Property(x => x.BadehkarLimit).HasPrecision(18, 4);
            modelBuilder.Entity<CapitalInvestment>().Property(x => x.Amount).HasPrecision(18, 4);
            modelBuilder.Entity<CapitalInvestment>().Property(x => x.ProfitCurrencyAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.FromAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.ToAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.ExchangeRate).HasPrecision(18, 8);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.CommissionAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.ExternalFeeAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.CostAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.RealizedProfit).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.DeferredAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.ExchangeProfitAmount).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.InventoryCostIncrease).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.InventoryCostDecrease).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.ShortLiabilityIncrease).HasPrecision(18, 4);
            modelBuilder.Entity<MoneyExchangeOperation>().Property(x => x.ShortLiabilityDecrease).HasPrecision(18, 4);

            modelBuilder.Entity<Hawala>()
                .Property(x => x.FromAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<Hawala>()
                .Property(x => x.ToAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<Hawala>()
                .Property(x => x.ExchangeRate)
                .HasPrecision(18, 8);

            modelBuilder.Entity<Hawala>()
                .Property(x => x.CommissionAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<Hawala>()
                .Property(x => x.AgentCommissionAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<DailyReport>()
                .Property(r => r.TotalSendAmount).HasPrecision(18, 4);
            modelBuilder.Entity<DailyReport>()
                .Property(r => r.TotalReceiveAmount).HasPrecision(18, 4);
            modelBuilder.Entity<DailyReport>()
                .Property(r => r.TotalCommission).HasPrecision(18, 4);
            modelBuilder.Entity<DailyReport>()
                .Property(r => r.TotalExpenses).HasPrecision(18, 4);
            modelBuilder.Entity<DailyReport>()
                .Property(r => r.NetIncome).HasPrecision(18, 4);

            modelBuilder.Entity<TransactionReport>()
                .Property(r => r.FromAmount).HasPrecision(18, 4);
            modelBuilder.Entity<TransactionReport>()
                .Property(r => r.ToAmount).HasPrecision(18, 4);
            modelBuilder.Entity<TransactionReport>()
                .Property(r => r.Commission).HasPrecision(18, 4);

            modelBuilder.Entity<CommissionReport>()
                .Property(r => r.TotalCommission).HasPrecision(18, 4);
            modelBuilder.Entity<CommissionReport>()
                .Property(r => r.TotalAgentCommission).HasPrecision(18, 4);
            modelBuilder.Entity<CommissionReport>()
                .Property(r => r.NetCommission).HasPrecision(18, 4);

            modelBuilder.Entity<TrialBalance>()
                .Property(r => r.TotalDebit).HasPrecision(18, 4);
            modelBuilder.Entity<TrialBalance>()
                .Property(r => r.TotalCredit).HasPrecision(18, 4);
            modelBuilder.Entity<TrialBalance>()
                .Property(r => r.Balance).HasPrecision(18, 4);
            modelBuilder.Entity<CapitalInvestment>()
                .Property(x => x.Amount)
                .HasPrecision(18, 4);
            modelBuilder.Entity<AccountMoneyOperation>()
                .Property(x => x.Amount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .Property(x => x.FromAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .Property(x => x.ToAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .Property(x => x.ExchangeRate)
                .HasPrecision(18, 8);
            modelBuilder.Entity<CashDailyBalance>()
                .Property(x => x.OpeningBalance)
                .HasPrecision(18, 4);
            modelBuilder.Entity<CashDailyBalance>()
                .Property(x => x.ClosingBalance)
                .HasPrecision(18, 4);
        }

        // ==========================================
        // محدودیت‌های دفتر کل
        // ==========================================
        private static void ConfigureLedgerConstraints(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LedgerEntry>()
                .HasCheckConstraint("CK_LedgerEntries_TalabKar_NonNegative", "[TalabKar] >= 0");

            modelBuilder.Entity<LedgerEntry>()
                .HasCheckConstraint("CK_LedgerEntries_BadehKar_NonNegative", "[BadehKar] >= 0");

            modelBuilder.Entity<LedgerEntry>()
                .HasCheckConstraint(
                    "CK_LedgerEntries_OnlyOneSide",
                    "([TalabKar] > 0 AND [BadehKar] = 0) OR ([TalabKar] = 0 AND [BadehKar] > 0)");
        }

        // ==========================================
        // روابط (Foreign Keys)
        // ==========================================
        private static void ConfigureRelationships(ModelBuilder modelBuilder)
        {
            // Transaction <-> CreatedByUser
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.CreatedByUser)
                .WithMany(u => u.CreatedTransactions)
                .HasForeignKey(t => t.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Transaction <-> CancelledByUser
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.CancelledByUser)
                .WithMany(u => u.CancelledTransactions)
                .HasForeignKey(t => t.CancelledBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Transaction <-> Branch
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Branch)
                .WithMany(b => b.Transactions)
                .HasForeignKey(t => t.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transaction <-> Customer
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Customer)
                .WithMany(c => c.Transactions)
                .HasForeignKey(t => t.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transaction <-> ReversedTransaction
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.ReversedTransaction)
                .WithMany()
                .HasForeignKey(t => t.ReversedTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // TransactionDetail relationships...
            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.Transaction)
                .WithMany(t => t.TransactionDetails)
                .HasForeignKey(td => td.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.Correspondent)
                .WithMany(c => c.TransactionDetails)
                .HasForeignKey(td => td.CorrespondentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.FromCurrency)
                .WithMany()
                .HasForeignKey(td => td.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.ToCurrency)
                .WithMany()
                .HasForeignKey(td => td.ToCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.CommissionCurrency)
                .WithMany()
                .HasForeignKey(td => td.CommissionCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.AgentCommissionCurrency)
                .WithMany()
                .HasForeignKey(td => td.AgentCommissionCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // LedgerEntry relationships...
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Transaction)
                .WithMany(t => t.LedgerEntries)
                .HasForeignKey(le => le.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Account)
                .WithMany(a => a.LedgerEntries)
                .HasForeignKey(le => le.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Currency)
                .WithMany(c => c.LedgerEntries)
                .HasForeignKey(le => le.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transfer relationships...
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Transfer)
                .WithMany(t => t.LedgerEntries)
                .HasForeignKey(le => le.TransferId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.FromAccount)
                .WithMany(a => a.FromTransfers)
                .HasForeignKey(t => t.FromAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.ToAccount)
                .WithMany(a => a.ToTransfers)
                .HasForeignKey(t => t.ToAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.Currency)
                .WithMany()
                .HasForeignKey(t => t.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense relationships...
            modelBuilder.Entity<Expense>()
                 .Property(x => x.Amount)
                 .HasPrecision(18, 4);

            modelBuilder.Entity<Expense>()
                .HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Expense>()
                .HasOne(x => x.ExpenseAccount)
                .WithMany()
                .HasForeignKey(x => x.ExpenseAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Expense>()
                .HasOne(x => x.PaidFromAccount)
                .WithMany()
                .HasForeignKey(x => x.PaidFromAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.Expense)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);

       

            // ExchangeRate relationships...
            modelBuilder.Entity<ExchangeRate>()
                .HasOne(er => er.FromCurrency)
                .WithMany()
                .HasForeignKey(er => er.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExchangeRate>()
                .HasOne(er => er.ToCurrency)
                .WithMany()
                .HasForeignKey(er => er.ToCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExchangeRate>()
                .HasOne(er => er.CreatedByUser)
                .WithMany(u => u.ExchangeRates)
                .HasForeignKey(er => er.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // AccountBadehkarLimit relationships...
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.Account)
                .WithMany(a => a.AccountBadehkarLimits)
                .HasForeignKey(abl => abl.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.Currency)
                .WithMany(c => c.AccountBadehkarLimits)
                .HasForeignKey(abl => abl.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.CreatedByUser)
                .WithMany(u => u.AccountLimits)
                .HasForeignKey(abl => abl.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // AuditLog <-> ApplicationUser
            modelBuilder.Entity<AuditLog>()
                .HasOne(al => al.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(al => al.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Hawala relationships...
            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.Correspondent)
                .WithMany()
                .HasForeignKey(h => h.CorrespondentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.FromCurrency)
                .WithMany()
                .HasForeignKey(h => h.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.ToCurrency)
                .WithMany()
                .HasForeignKey(h => h.ToCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.CommissionCurrency)
                .WithMany()
                .HasForeignKey(h => h.CommissionCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.AgentCommissionCurrency)
                .WithMany()
                .HasForeignKey(h => h.AgentCommissionCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.CreatedByUser)
                .WithMany(u => u.CreatedHawalas)
                .HasForeignKey(h => h.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.PaidByUser)
                .WithMany(u => u.PaidHawalas)
                .HasForeignKey(h => h.PaidBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.PaidFromAccount)
                .WithMany()
                .HasForeignKey(h => h.PaidFromAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.SourceHawala)
                .WithOne()
                .HasForeignKey<Hawala>(h => h.SourceHawalaId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hawala>()
                .HasOne(h => h.CancelledByUser)
                .WithMany(u => u.CancelledHawalas)
                .HasForeignKey(h => h.CancelledBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Report relationships...
            modelBuilder.Entity<DailyReport>()
                .HasOne(r => r.Branch)
                .WithMany()
                .HasForeignKey(r => r.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionReport>()
                .HasOne(r => r.Transaction)
                .WithMany()
                .HasForeignKey(r => r.TransactionId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CommissionReport>()
                .HasOne(r => r.Branch)
                .WithMany()
                .HasForeignKey(r => r.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TrialBalance>()
                .HasOne(r => r.Account)
                .WithMany()
                .HasForeignKey(r => r.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<CapitalInvestment>()
                .HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CapitalInvestment>()
                .HasOne(x => x.ProfitCurrency)
                .WithMany()
                .HasForeignKey(x => x.ProfitCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CapitalInvestment>()
                .HasOne(x => x.ReceivingAccount)
                .WithMany()
                .HasForeignKey(x => x.ReceivingAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CapitalInvestment>()
                .HasOne(x => x.CapitalAccount)
                .WithMany()
                .HasForeignKey(x => x.CapitalAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.CapitalInvestment)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.CapitalInvestmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // AccountMoneyOperation relationships...
            modelBuilder.Entity<AccountMoneyOperation>()
                .HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountMoneyOperation>()
                .HasOne(x => x.CashOrBankAccount)
                .WithMany()
                .HasForeignKey(x => x.CashOrBankAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountMoneyOperation>()
                .HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.AccountMoneyOperation)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.AccountMoneyOperationId)
                .OnDelete(DeleteBehavior.Restrict);
            // MoneyExchangeOperation relationships...
            modelBuilder.Entity<MoneyExchangeOperation>()
    .HasOne(x => x.FromAccount)
    .WithMany()
    .HasForeignKey(x => x.FromAccountId)
    .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .HasOne(x => x.ToAccount)
                .WithMany()
                .HasForeignKey(x => x.ToAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .HasOne(x => x.FromCurrency)
                .WithMany()
                .HasForeignKey(x => x.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .HasOne(x => x.ToCurrency)
                .WithMany()
                .HasForeignKey(x => x.ToCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .HasOne(x => x.ProfitCurrency)
                .WithMany()
                .HasForeignKey(x => x.ProfitCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .HasOne(x => x.RateBaseCurrency)
                .WithMany()
                .HasForeignKey(x => x.RateBaseCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MoneyExchangeOperation>()
                .HasOne(x => x.RateQuoteCurrency)
                .WithMany()
                .HasForeignKey(x => x.RateQuoteCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.MoneyExchangeOperation)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.MoneyExchangeOperationId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CashDailyBalance>()
                .HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CashDailyBalance>()
                .HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

        }

        // ==========================================
        // تنظیمات اختصاصی حواله
        // ==========================================
        private static void ConfigureHawalaEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Hawala>()
                .HasCheckConstraint("CK_Hawala_FromAmount_Positive", "[FromAmount] > 0");

            modelBuilder.Entity<Hawala>()
                .HasCheckConstraint("CK_Hawala_ToAmount_Positive_IfNotNull", "[ToAmount] IS NULL OR [ToAmount] > 0");

            modelBuilder.Entity<Hawala>()
                .HasCheckConstraint("CK_Hawala_ExchangeRate_Positive_IfNotNull", "[ExchangeRate] IS NULL OR [ExchangeRate] > 0");

            modelBuilder.Entity<Hawala>()
                .HasCheckConstraint("CK_Hawala_CommissionAmount_NonNegative_IfNotNull", "[CommissionAmount] IS NULL OR [CommissionAmount] >= 0");

            modelBuilder.Entity<Hawala>()
                .HasCheckConstraint("CK_Hawala_HawalaType_Valid",
                    "[HawalaType] IN ('HawalaSend', 'HawalaReceive', 'HawalaOther')");

            modelBuilder.Entity<Hawala>()
                .HasCheckConstraint("CK_Hawala_Status_Valid",
                    "[Status] IN ('Pending', 'Paid', 'Cancel')");
        }

        // ==========================================
        // تنظیمات اختصاصی گزارش‌ها
        // ==========================================
        private static void ConfigureReportEntities(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DailyReport>()
                .HasCheckConstraint("CK_DailyReport_TotalSendAmount_NonNegative", "[TotalSendAmount] >= 0");

            modelBuilder.Entity<DailyReport>()
                .HasCheckConstraint("CK_DailyReport_TotalReceiveAmount_NonNegative", "[TotalReceiveAmount] >= 0");
        }

        // ==========================================
        // داده‌های اولیه (Seed Data)
        // ==========================================
        private static void SeedData(ModelBuilder modelBuilder)
        {
            var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            modelBuilder.Entity<Branch>().HasData(
                new Branch
                {
                    Id = 1,
                    Code = "MAIN",
                    Name = "شعبه اصلی",
                    PhoneNumber = null,
                    Address = "",
                    IsArchived = false,
                    CreatedAt = createdAt
                }
            );

            modelBuilder.Entity<Currency>().HasData(
                new Currency { Id = 1, Code = "AFN", Name = "افغانی", Symbol = "؋", DecimalPlaces = 2, QuotationPriority = 60, IsActive = true },
                new Currency { Id = 2, Code = "USD", Name = "دالر امریکایی", Symbol = "$", DecimalPlaces = 2, QuotationPriority = 20, IsActive = true },
                new Currency { Id = 3, Code = "EUR", Name = "یورو", Symbol = "€", DecimalPlaces = 2, QuotationPriority = 10, IsActive = true },
                new Currency { Id = 4, Code = "AED", Name = "درهم عربی", Symbol = "د.إ", DecimalPlaces = 2, QuotationPriority = 30, IsActive = true },
                new Currency { Id = 5, Code = "IRR", Name = "ریال ایرانی", Symbol = "﷼", DecimalPlaces = 2, QuotationPriority = 50, IsActive = true },
                new Currency { Id = 6, Code = "PKR", Name = "روپیه پاکستانی", Symbol = "₨", DecimalPlaces = 2, QuotationPriority = 40, IsActive = true }
            );

            modelBuilder.Entity<Account>().HasData(
                new Account { Id = 1, AccountCode = "1001", AccountName = "صندوق", AccountType = "Cash", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 2, AccountCode = "1101", AccountName = "بانک", AccountType = "Bank", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 3, AccountCode = "3001", AccountName = "درآمد کمیسیون حواله", AccountType = "Income", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 4, AccountCode = "3002", AccountName = "درآمد تبادل", AccountType = "Income", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 5, AccountCode = "4001", AccountName = "هزینه دفتر", AccountType = "Expense", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 6, AccountCode = "5001", AccountName = "سرمایه مالک", AccountType = "Equity", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt }
            );
        }
    }
}
