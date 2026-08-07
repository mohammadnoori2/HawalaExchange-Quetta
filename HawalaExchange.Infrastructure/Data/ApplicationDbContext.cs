
using HawalaExchange.Domain.Entities;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HawalaExchange.Infrastructure.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<long>, long>
    {
        private readonly ICurrentTenant _currentTenant;
        private long? tenantOverride;
        private int subscriptionEnforcementBypassDepth;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentTenant currentTenant)
            : base(options)
        {
            _currentTenant = currentTenant;
        }

        public long CurrentTenantId => tenantOverride ?? _currentTenant.TenantId;
        public long CurrentUserId => _currentTenant.UserId;

        public void PrepareTenantEntity(ITenantEntity entity)
        {
            var tenantId = CurrentTenantId;
            if (tenantId <= 0)
                throw new InvalidOperationException("صرافی جاری تشخیص داده نشد. لطفاً دوباره وارد سیستم شوید.");

            if (entity.TenantId == 0)
                entity.TenantId = tenantId;
            else if (entity.TenantId != tenantId)
                throw new InvalidOperationException("عملیات روی داده‌های صرافی دیگر مجاز نیست.");
        }

        public long RequireCurrentUserId()
        {
            var userId = CurrentUserId;
            return userId > 0
                ? userId
                : throw new InvalidOperationException("کاربر جاری تشخیص داده نشد. لطفاً دوباره وارد سیستم شوید.");
        }

        public async Task<long> GetDefaultBranchIdAsync(CancellationToken cancellationToken = default)
        {
            var branchId = await Branches
                .AsNoTracking()
                .OrderByDescending(x => x.Code == "HQ")
                .ThenBy(x => x.Id)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return branchId ?? throw new InvalidOperationException("برای صرافی جاری هیچ شعبه‌ای تعریف نشده است.");
        }

        public IDisposable UseTenantScope(long tenantId)
        {
            if (tenantId <= 0)
                throw new ArgumentOutOfRangeException(nameof(tenantId));

            var previous = tenantOverride;
            tenantOverride = tenantId;
            return new TenantScope(() => tenantOverride = previous);
        }

        public IDisposable BypassSubscriptionEnforcement()
        {
            subscriptionEnforcementBypassDepth++;
            return new TenantScope(() => subscriptionEnforcementBypassDepth--);
        }

        // ===== DbSets موجود =====
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<TenantSubscription> TenantSubscriptions { get; set; }
        public DbSet<SubscriptionPayment> SubscriptionPayments { get; set; }
        public DbSet<SubscriptionInvoice> SubscriptionInvoices { get; set; }
        public DbSet<SubscriptionInvoiceItem> SubscriptionInvoiceItems { get; set; }
        public DbSet<BillingNumberSequence> BillingNumberSequences { get; set; }
        public DbSet<TenantUsageSnapshot> TenantUsageSnapshots { get; set; }
        public DbSet<PlatformAuditLog> PlatformAuditLogs { get; set; }
        public DbSet<SaasAutomationSettings> SaasAutomationSettings { get; set; }
        public DbSet<SaasNotification> SaasNotifications { get; set; }
        public DbSet<SaasAutomationRun> SaasAutomationRuns { get; set; }
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
        public async Task EnsureSystemAccountsAsync(
            long tenantId,
            CancellationToken cancellationToken = default)
        {
            var systemCodes = new[]
            {
                CurrencyInventoryAccountCode,
                CurrencySaleLiabilityAccountCode,
                PendingHawalaAccountCode
            };
            var accounts = await Accounts
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && systemCodes.Contains(x.AccountCode))
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
                tenantId,
                CurrencyInventoryAccountCode,
                "موجودی ارز به بهای تمام‌شده",
                "Asset");
            liabilityAccount ??= AddSystemAccount(
                tenantId,
                CurrencySaleLiabilityAccountCode,
                "تعهد فروش ارز",
                "Liability");
            pendingHawalaAccount ??= AddSystemAccount(
                tenantId,
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
                .IgnoreQueryFilters()
                .Where(x =>
                    x.TenantId == tenantId &&
                    x.AccountId == liabilityAccount.Id &&
                    x.HawalaId != null &&
                    x.MoneyExchangeOperationId == null)
                .ToListAsync(cancellationToken);
            foreach (var entry in hawalaEntriesOnLiability)
                entry.AccountId = pendingHawalaAccount.Id;

            var exchangeEntriesOnPendingHawala = await LedgerEntries
                .IgnoreQueryFilters()
                .Where(x =>
                    x.TenantId == tenantId &&
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

        private Account AddSystemAccount(
            long tenantId,
            string accountCode,
            string accountName,
            string accountType)
        {
            var account = new Account
            {
                TenantId = tenantId,
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
            AssignAndValidateTenantIds();
            ValidateSubscriptionWriteAccess();
            ValidateAccountDebtLimits();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            AssignAndValidateTenantIds();
            await ValidateSubscriptionWriteAccessAsync(cancellationToken);
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
            account.CustomerId.HasValue || account.CorrespondentId.HasValue;

        private sealed record LedgerBalanceChange(
            long AccountId,
            long CurrencyId,
            decimal Delta);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureIdentityTables(modelBuilder);
            ConfigureIdentity(modelBuilder);
            ConfigureIndexes(modelBuilder);
            ConfigureDefaultValues(modelBuilder);
            ConfigureDecimalPrecision(modelBuilder);
            ConfigureLedgerConstraints(modelBuilder);
            ConfigureRelationships(modelBuilder);
            ConfigureHawalaEntity(modelBuilder);
            ConfigureSaasManagement(modelBuilder);
            SeedData(modelBuilder);
            ConfigureTenantFilters(modelBuilder);
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

            // Must run after every relationship has been configured.
            ConfigureTenantForeignKeys(modelBuilder);
        }

        private void ValidateSubscriptionWriteAccess()
        {
            if (subscriptionEnforcementBypassDepth > 0) return;
            var tenantIds = PendingTenantWriteIds();
            foreach (var tenantId in tenantIds)
            {
                var subscription = TenantSubscriptions.AsNoTracking()
                    .Where(x => x.TenantId == tenantId)
                    .OrderByDescending(x => x.StartAt).ThenByDescending(x => x.Id)
                    .Select(x => new { x.Status, x.EndAt, x.GracePeriodEndAt })
                    .FirstOrDefault();
                EnsureSubscriptionAllowsWrite(subscription?.Status, subscription?.EndAt, subscription?.GracePeriodEndAt);
            }
        }

        private async Task ValidateSubscriptionWriteAccessAsync(CancellationToken cancellationToken)
        {
            if (subscriptionEnforcementBypassDepth > 0) return;
            var tenantIds = PendingTenantWriteIds();
            foreach (var tenantId in tenantIds)
            {
                var subscription = await TenantSubscriptions.AsNoTracking()
                    .Where(x => x.TenantId == tenantId)
                    .OrderByDescending(x => x.StartAt).ThenByDescending(x => x.Id)
                    .Select(x => new { x.Status, x.EndAt, x.GracePeriodEndAt })
                    .FirstOrDefaultAsync(cancellationToken);
                EnsureSubscriptionAllowsWrite(subscription?.Status, subscription?.EndAt, subscription?.GracePeriodEndAt);
            }
        }

        private long[] PendingTenantWriteIds() => ChangeTracker.Entries()
            .Where(x => x.Entity is ITenantEntity &&
                        x.Entity is not ApplicationUser { IsPlatformUser: true } &&
                        x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => ((ITenantEntity)x.Entity).TenantId)
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        private static void EnsureSubscriptionAllowsWrite(
            SubscriptionStatus? status,
            DateTime? endAt,
            DateTime? gracePeriodEndAt)
        {
            var now = DateTime.UtcNow;
            if (status is null)
                throw new InvalidOperationException("برای این صرافی اشتراک ثبت نشده است.");
            if (status is SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled)
                throw new InvalidOperationException("اشتراک صرافی فعال نیست و عملیات تغییردهنده مجاز نمی‌باشد.");
            if (endAt >= now) return;
            if (gracePeriodEndAt >= now)
                throw new InvalidOperationException("اشتراک منقضی شده و سیستم در دوره مهلت فقط خواندنی است.");
            throw new InvalidOperationException("اشتراک و دوره مهلت پایان یافته است.");
        }

        private void AssignAndValidateTenantIds()
        {
            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    if (entry.Entity is ApplicationUser { IsPlatformUser: true })
                        continue;
                    PrepareTenantEntity(entry.Entity);
                }
            }
        }

        private sealed class TenantScope(Action onDispose) : IDisposable
        {
            private Action? dispose = onDispose;
            public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
        }

        private void ConfigureTenantFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                         .Where(x => typeof(ITenantEntity).IsAssignableFrom(x.ClrType)))
            {
                if (entityType.ClrType == typeof(ApplicationUser))
                {
                    modelBuilder.Entity<ApplicationUser>()
                        .HasQueryFilter(u => !u.IsPlatformUser && u.TenantId == CurrentTenantId);
                    continue;
                }
                var parameter = Expression.Parameter(entityType.ClrType, "entity");
                var tenantId = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
                var currentTenantId = Expression.Property(
                    Expression.Constant(this),
                    nameof(CurrentTenantId));
                var filter = Expression.Lambda(
                    Expression.Equal(tenantId, currentTenantId),
                    parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
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
        private static void ConfigureSaasManagement(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SubscriptionPlan>(entity =>
            {
                entity.HasIndex(x => x.Code).IsUnique();
                entity.HasIndex(x => new { x.IsActive, x.DisplayOrder });
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_SubscriptionPlans_Prices", "[MonthlyPrice] >= 0 AND [AnnualPrice] >= 0");
                    table.HasCheckConstraint("CK_SubscriptionPlans_Limits", "[TrialDays] >= 0 AND [MaxUsers] >= 0 AND [MaxBranches] >= 0 AND [MaxStorageBytes] >= 0 AND [MaxMonthlyTransactions] >= 0");
                });
            });

            modelBuilder.Entity<TenantSubscription>(entity =>
            {
                entity.HasOne(x => x.Tenant)
                    .WithMany(x => x.Subscriptions)
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Plan)
                    .WithMany(x => x.Subscriptions)
                    .HasForeignKey(x => x.PlanId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.TenantId, x.Status, x.EndAt });
                entity.HasIndex(x => new { x.Status, x.EndAt });
                entity.HasIndex(x => x.NextPaymentAt);
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_TenantSubscriptions_Dates", "[EndAt] > [StartAt]");
                    table.HasCheckConstraint("CK_TenantSubscriptions_Price", "[AgreedPrice] >= 0");
                });
            });

            modelBuilder.Entity<SubscriptionPayment>(entity =>
            {
                entity.HasOne(x => x.Subscription)
                    .WithMany(x => x.Payments)
                    .HasForeignKey(x => x.SubscriptionId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.SubscriptionId, x.Status, x.DueAt });
                entity.HasIndex(x => x.ReferenceNumber)
                    .IsUnique()
                    .HasFilter("[ReferenceNumber] IS NOT NULL");
                entity.ToTable(table =>
                    table.HasCheckConstraint("CK_SubscriptionPayments_Amount", "[Amount] >= 0"));
            });

            modelBuilder.Entity<SubscriptionInvoice>(entity =>
            {
                entity.HasOne(x => x.Tenant)
                    .WithMany(x => x.SubscriptionInvoices)
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Subscription)
                    .WithMany(x => x.Invoices)
                    .HasForeignKey(x => x.SubscriptionId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.InvoiceNumber).IsUnique();
                entity.HasIndex(x => new { x.TenantId, x.Status, x.DueAt });
                entity.HasIndex(x => new { x.Status, x.DueAt });
                entity.HasIndex(x => new { x.CurrencyCode, x.IssuedAt });
                entity.ToTable(table =>
                {
                    table.HasCheckConstraint("CK_SubscriptionInvoices_Dates", "[ServicePeriodEnd] > [ServicePeriodStart] AND [DueAt] >= [IssuedAt]");
                    table.HasCheckConstraint("CK_SubscriptionInvoices_Amounts", "[Subtotal] >= 0 AND [DiscountAmount] >= 0 AND [TaxAmount] >= 0 AND [TotalAmount] >= 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [TotalAmount]");
                });
            });

            modelBuilder.Entity<SubscriptionInvoiceItem>(entity =>
            {
                entity.HasOne(x => x.Invoice)
                    .WithMany(x => x.Items)
                    .HasForeignKey(x => x.InvoiceId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => new { x.InvoiceId, x.SortOrder });
                entity.ToTable(table => table.HasCheckConstraint(
                    "CK_SubscriptionInvoiceItems_Amounts",
                    "[Quantity] > 0 AND [UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] >= 0"));
            });

            modelBuilder.Entity<SubscriptionPayment>()
                .HasOne(x => x.Invoice)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SubscriptionPayment>()
                .HasIndex(x => x.ProviderTransactionId)
                .IsUnique()
                .HasFilter("[ProviderTransactionId] IS NOT NULL");

            modelBuilder.Entity<BillingNumberSequence>(entity =>
            {
                entity.HasIndex(x => new { x.Year, x.Prefix }).IsUnique();
                entity.ToTable(table => table.HasCheckConstraint("CK_BillingNumberSequences_NextValue", "[NextValue] > 0"));
            });

            modelBuilder.Entity<TenantUsageSnapshot>(entity =>
            {
                entity.HasOne(x => x.Tenant)
                    .WithMany(x => x.UsageSnapshots)
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.TenantId, x.PeriodStart }).IsUnique();
                entity.HasIndex(x => x.PeriodStart);
            });

            modelBuilder.Entity<PlatformAuditLog>(entity =>
            {
                entity.HasOne(x => x.Tenant)
                    .WithMany()
                    .HasForeignKey(x => x.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.CreatedAt, x.Action });
                entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
            });

            modelBuilder.Entity<SaasAutomationSettings>(entity =>
            {
                entity.ToTable(table => table.HasCheckConstraint("CK_SaasAutomationSettings_Values", "[Id] = 1 AND [RunIntervalMinutes] >= 5 AND [ExpiryWarningDays] > 0 AND [GracePeriodDays] >= 0 AND [QuotaWarningPercent] BETWEEN 1 AND 100"));
            });
            modelBuilder.Entity<SaasNotification>(entity =>
            {
                entity.HasIndex(x => x.DeduplicationKey).IsUnique();
                entity.HasIndex(x => new { x.ReadAt, x.Severity, x.CreatedAt });
                entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
                entity.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<SaasAutomationRun>(entity =>
            {
                entity.HasIndex(x => x.RunKey).IsUnique();
                entity.HasIndex(x => new { x.StartedAt, x.Status });
            });
        }

        private void ConfigureIdentity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            });

            // تنظیمات ApplicationUser
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.ToTable("Users");
                entity.Property(u => u.LocalUserName).HasMaxLength(256).IsRequired();
                entity.HasIndex(u => u.LocalUserName).IsUnique();
                entity.HasIndex(u => u.NormalizedEmail)
                    .IsUnique()
                    .HasFilter("[NormalizedEmail] IS NOT NULL");
                entity.Property(u => u.FullName).HasMaxLength(200);
                entity.Property(u => u.IsActive).HasDefaultValue(true);
                entity.Property(u => u.IsPlatformUser).HasDefaultValue(false);
                entity.Property(u => u.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.ToTable(table => table.HasCheckConstraint(
                    "CK_Users_Scope",
                    "([IsPlatformUser] = 1 AND [BranchId] IS NULL) OR ([IsPlatformUser] = 0 AND [BranchId] IS NOT NULL)"));

                // رابطه با Branch
                entity.HasOne(u => u.Branch)
                    .WithMany(b => b.Users)
                    .HasForeignKey(u => u.BranchId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(u => u.Tenant)
                    .WithMany(t => t.Users)
                    .HasForeignKey(u => u.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Document>()
                .Property(x => x.FileSizeBytes)
                .HasDefaultValue(0L);

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
            modelBuilder.Entity<Branch>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            modelBuilder.Entity<Customer>().HasIndex(x => new { x.TenantId, x.CustomerCode }).IsUnique();
            modelBuilder.Entity<Correspondent>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            modelBuilder.Entity<Currency>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            modelBuilder.Entity<Account>().HasIndex(x => new { x.TenantId, x.AccountCode }).IsUnique();
            modelBuilder.Entity<Account>()
                .HasIndex(x => new { x.TenantId, x.CustomerId })
                .IsUnique()
                .HasFilter("[CustomerId] IS NOT NULL");
            modelBuilder.Entity<Account>()
                .HasIndex(x => new { x.TenantId, x.CorrespondentId })
                .IsUnique()
                .HasFilter("[CorrespondentId] IS NOT NULL");
            modelBuilder.Entity<Transaction>().HasIndex(x => new { x.TenantId, x.TransactionNo }).IsUnique();
            modelBuilder.Entity<Transaction>().HasIndex(x => new { x.TenantId, x.CreatedAt, x.Status });
            modelBuilder.Entity<AccountBadehkarLimit>().HasIndex(x => new { x.TenantId, x.AccountId, x.CurrencyId }).IsUnique();
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.AccountId, x.CurrencyId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.CreatedAt });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.TransactionId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.HawalaId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.TransferId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.CapitalInvestmentId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.ExpenseId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.AccountMoneyOperationId });
            modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.TenantId, x.MoneyExchangeOperationId });
            modelBuilder.Entity<ExchangeRate>().HasIndex(x => new { x.TenantId, x.FromCurrencyId, x.ToCurrencyId, x.EffectiveDate });
            modelBuilder.Entity<Document>().HasIndex(x => new { x.TenantId, x.TransactionId });
            modelBuilder.Entity<Document>().HasIndex(x => new { x.TenantId, x.CustomerId });
            modelBuilder.Entity<Document>().HasIndex(x => new { x.TenantId, x.CorrespondentId });
            modelBuilder.Entity<Document>().HasIndex(x => new { x.TenantId, x.AccountId });
            modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.TenantId, x.TableName, x.RecordId });
            modelBuilder.Entity<AuditLog>().HasIndex(x => new { x.TenantId, x.CreatedAt });
            modelBuilder.Entity<Hawala>().HasIndex(x => new { x.TenantId, x.HawalaType, x.Status });
            modelBuilder.Entity<Hawala>()
                .HasIndex(x => new { x.TenantId, x.CorrespondentId, x.HawalaType, x.Number })
                .IsUnique();
            modelBuilder.Entity<Hawala>()
                .HasIndex(x => new { x.TenantId, x.SourceHawalaId })
                .IsUnique()
                .HasFilter("[SourceHawalaId] IS NOT NULL");
            modelBuilder.Entity<Hawala>().HasIndex(x => new { x.TenantId, x.ReferenceNumber });
            modelBuilder.Entity<Hawala>().HasIndex(x => new { x.TenantId, x.CreatedAt });
            modelBuilder.Entity<CashDailyBalance>()
                .HasIndex(x => new { x.TenantId, x.JournalDate, x.AccountId, x.CurrencyId })
                .IsUnique();

            modelBuilder.Entity<Expense>().HasIndex(x => new { x.TenantId, x.ExpenseDate });
            modelBuilder.Entity<AccountMoneyOperation>().HasIndex(x => new { x.TenantId, x.OperationDate });
            modelBuilder.Entity<MoneyExchangeOperation>().HasIndex(x => new { x.TenantId, x.CreatedAt });
            modelBuilder.Entity<CapitalInvestment>().HasIndex(x => new { x.TenantId, x.CreatedAt });

            modelBuilder.Entity<CompanySetting>().HasIndex(x => x.TenantId).IsUnique();
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

            modelBuilder.Entity<LedgerEntry>()
                .ToTable(t => t.HasCheckConstraint(
                    "CK_LedgerEntries_AtMostOneSource",
                    "(CASE WHEN [TransactionId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [HawalaId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [TransferId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [CapitalInvestmentId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [ExpenseId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [AccountMoneyOperationId] IS NULL THEN 0 ELSE 1 END + " +
                    "CASE WHEN [MoneyExchangeOperationId] IS NULL THEN 0 ELSE 1 END) <= 1"));
        }

        private static void ConfigureTenantForeignKeys(ModelBuilder modelBuilder)
        {
            var tenantEntityTypes = modelBuilder.Model.GetEntityTypes()
                .Where(x => typeof(ITenantEntity).IsAssignableFrom(x.ClrType))
                .ToList();

            foreach (var entityType in tenantEntityTypes)
            {
                var tenantProperty = entityType.FindProperty(nameof(ITenantEntity.TenantId));
                var primaryKey = entityType.FindPrimaryKey();
                if (tenantProperty is null || primaryKey is null || primaryKey.Properties.Count != 1)
                    continue;

                if (entityType.FindKey(new[] { tenantProperty, primaryKey.Properties[0] }) is null)
                    entityType.AddKey(new[] { tenantProperty, primaryKey.Properties[0] });
            }

            foreach (var dependentType in tenantEntityTypes)
            {
                var dependentTenant = dependentType.FindProperty(nameof(ITenantEntity.TenantId));
                if (dependentTenant is null)
                    continue;

                foreach (var foreignKey in dependentType.GetForeignKeys().ToList())
                {
                    var principalType = foreignKey.PrincipalEntityType;
                    if (!typeof(ITenantEntity).IsAssignableFrom(principalType.ClrType) ||
                        foreignKey.Properties.Contains(dependentTenant))
                        continue;

                    var principalTenant = principalType.FindProperty(nameof(ITenantEntity.TenantId));
                    if (principalTenant is null)
                        continue;

                    var principalProperties = new[] { principalTenant }
                        .Concat(foreignKey.PrincipalKey.Properties)
                        .ToList();
                    var principalKey = principalType.FindKey(principalProperties)
                        ?? principalType.AddKey(principalProperties);
                    var dependentProperties = new[] { dependentTenant }
                        .Concat(foreignKey.Properties)
                        .ToList();

                    foreignKey.SetProperties(dependentProperties, principalKey);
                }
            }
        }

        // ==========================================
        // روابط (Foreign Keys)
        // ==========================================
        private static void ConfigureRelationships(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Document>().HasOne(x => x.Transaction).WithMany(x => x.Documents)
                .HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Document>().HasOne(x => x.Customer).WithMany()
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Document>().HasOne(x => x.Correspondent).WithMany()
                .HasForeignKey(x => x.CorrespondentId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Document>().HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Document>().ToTable(t => t.HasCheckConstraint(
                "CK_Documents_ExactlyOneOwner",
                "(CASE WHEN [TransactionId] IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN [CustomerId] IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN [CorrespondentId] IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN [AccountId] IS NULL THEN 0 ELSE 1 END) = 1"));

            modelBuilder.Entity<Account>()
                .HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Account>()
                .HasOne(x => x.Correspondent)
                .WithMany()
                .HasForeignKey(x => x.CorrespondentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Account>()
                .ToTable(t => t.HasCheckConstraint(
                    "CK_Accounts_OneOwner",
                    "[CustomerId] IS NULL OR [CorrespondentId] IS NULL"));

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
                .WithMany(c => c.FromTransactions)
                .HasForeignKey(td => td.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.ToCurrency)
                .WithMany(c => c.ToTransactions)
                .HasForeignKey(td => td.ToCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.CommissionCurrency)
                .WithMany(c => c.CommissionTransactions)
                .HasForeignKey(td => td.CommissionCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.AgentCommissionCurrency)
                .WithMany(c => c.AgentCommissionTransactions)
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
                .WithMany(c => c.Transfers)
                .HasForeignKey(t => t.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense relationships...
            modelBuilder.Entity<Expense>()
                 .Property(x => x.Amount)
                 .HasPrecision(18, 4);

            modelBuilder.Entity<Expense>()
                .HasOne(x => x.Currency)
                .WithMany(c => c.Expenses)
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
                .WithMany(c => c.FromExchangeRates)
                .HasForeignKey(er => er.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExchangeRate>()
                .HasOne(er => er.ToCurrency)
                .WithMany(c => c.ToExchangeRates)
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
        // داده‌های اولیه (Seed Data)
        // ==========================================
        private static void SeedData(ModelBuilder modelBuilder)
        {
            var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            modelBuilder.Entity<Branch>().HasData(
                new Branch
                {
                    Id = 1,
                    TenantId = 1,
                    Code = "MAIN",
                    Name = "شعبه اصلی",
                    PhoneNumber = null,
                    Address = "",
                    IsArchived = false,
                    CreatedAt = createdAt
                }
            );

            modelBuilder.Entity<Tenant>().HasData(
                new Tenant
                {
                    Id = 1,
                    Name = "صرافی پیش‌فرض",
                    IsActive = true,
                    CreatedAt = createdAt
                }
            );

            modelBuilder.Entity<SubscriptionPlan>().HasData(
                new SubscriptionPlan
                {
                    Id = 1,
                    Code = "STANDARD",
                    Name = "پلن استاندارد",
                    Description = "پلن پایه برای صرافی پیش‌فرض",
                    MonthlyPrice = 0,
                    AnnualPrice = 0,
                    CurrencyCode = "USD",
                    TrialDays = 0,
                    MaxUsers = 25,
                    MaxBranches = 5,
                    MaxStorageBytes = 10L * 1024 * 1024 * 1024,
                    MaxMonthlyTransactions = 100000,
                    IncludesAdvancedReports = true,
                    IncludesDocumentManagement = true,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = createdAt
                });

            modelBuilder.Entity<TenantSubscription>().HasData(
                new TenantSubscription
                {
                    Id = 1,
                    TenantId = 1,
                    PlanId = 1,
                    Status = SubscriptionStatus.Active,
                    BillingCycle = BillingCycle.Annual,
                    StartAt = createdAt,
                    EndAt = new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    AutoRenew = false,
                    AgreedPrice = 0,
                    CurrencyCode = "USD",
                    CreatedAt = createdAt
                });

            modelBuilder.Entity<Currency>().HasData(
                new Currency { Id = 1, TenantId = 1, Code = "AFN", Name = "افغانی", Symbol = "؋", DecimalPlaces = 2, QuotationPriority = 60, IsActive = true },
                new Currency { Id = 2, TenantId = 1, Code = "USD", Name = "دالر امریکایی", Symbol = "$", DecimalPlaces = 2, QuotationPriority = 20, IsActive = true },
                new Currency { Id = 3, TenantId = 1, Code = "EUR", Name = "یورو", Symbol = "€", DecimalPlaces = 2, QuotationPriority = 10, IsActive = true },
                new Currency { Id = 4, TenantId = 1, Code = "AED", Name = "درهم عربی", Symbol = "د.إ", DecimalPlaces = 2, QuotationPriority = 30, IsActive = true },
                new Currency { Id = 5, TenantId = 1, Code = "IRR", Name = "ریال ایرانی", Symbol = "﷼", DecimalPlaces = 2, QuotationPriority = 50, IsActive = true },
                new Currency { Id = 6, TenantId = 1, Code = "PKR", Name = "روپیه پاکستانی", Symbol = "₨", DecimalPlaces = 2, QuotationPriority = 40, IsActive = true }
            );

            modelBuilder.Entity<Account>().HasData(
                new Account { Id = 1, TenantId = 1, AccountCode = "1001", AccountName = "صندوق", AccountType = "Cash", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 2, TenantId = 1, AccountCode = "1101", AccountName = "بانک", AccountType = "Bank", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 3, TenantId = 1, AccountCode = "3001", AccountName = "درآمد کمیسیون حواله", AccountType = "Income", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 4, TenantId = 1, AccountCode = "3002", AccountName = "درآمد تبادل", AccountType = "Income", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 5, TenantId = 1, AccountCode = "4001", AccountName = "هزینه دفتر", AccountType = "Expense", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 6, TenantId = 1, AccountCode = "5001", AccountName = "سرمایه مالک", AccountType = "Equity", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt }
            );
        }
    }
}
