
using HawalaExchange.Domain.Entities;
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
            modelBuilder.Entity<Hawala>().HasIndex(x => x.ReferenceNumber);
            modelBuilder.Entity<Hawala>().HasIndex(x => x.CreatedAt);
            modelBuilder.Entity<DailyReport>().HasIndex(r => new { r.Date, r.BranchId });
            modelBuilder.Entity<TransactionReport>().HasIndex(r => r.TransactionNo);
            modelBuilder.Entity<TransactionReport>().HasIndex(r => r.CreatedAt);
            modelBuilder.Entity<CommissionReport>().HasIndex(r => new { r.Date, r.BranchId });
            modelBuilder.Entity<TrialBalance>().HasIndex(r => new { r.AsOfDate, r.AccountId });
        }

        // ==========================================
        // مقادیر پیش‌فرض
        // ==========================================
        private static void ConfigureDefaultValues(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Branch>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Currency>().Property(x => x.DecimalPlaces).HasDefaultValue(2);
            modelBuilder.Entity<Currency>().Property(x => x.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Account>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Customer>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Correspondent>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<AccountBadehkarLimit>().Property(x => x.IsActive).HasDefaultValue(true);

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
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.Transaction)
                .WithMany(tr => tr.Transfers)
                .HasForeignKey(t => t.TransactionId)
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
                .HasOne(e => e.Transaction)
                .WithMany(t => t.Expenses)
                .HasForeignKey(e => e.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Currency)
                .WithMany()
                .HasForeignKey(e => e.CurrencyId)
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
                    Name = "Main Branch",
                    PhoneNumber = null,
                    Address = "",
                    IsArchived = false,
                    CreatedAt = createdAt
                }
            );

            modelBuilder.Entity<Currency>().HasData(
                new Currency { Id = 1, Code = "AFN", Name = "Afghani", Symbol = "؋", DecimalPlaces = 2, IsActive = true },
                new Currency { Id = 2, Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsActive = true },
                new Currency { Id = 3, Code = "EUR", Name = "Euro", Symbol = "€", DecimalPlaces = 2, IsActive = true },
                new Currency { Id = 4, Code = "AED", Name = "UAE Dirham", Symbol = "د.إ", DecimalPlaces = 2, IsActive = true },
                new Currency { Id = 5, Code = "IRR", Name = "Iranian Rial", Symbol = "﷼", DecimalPlaces = 2, IsActive = true },
                new Currency { Id = 6, Code = "PKR", Name = "Pakistani Rupee", Symbol = "₨", DecimalPlaces = 2, IsActive = true }
            );

            modelBuilder.Entity<Account>().HasData(
                new Account { Id = 1, AccountCode = "1001", AccountName = "Cash", AccountType = "Cash", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 2, AccountCode = "1101", AccountName = "Bank", AccountType = "Bank", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 3, AccountCode = "3001", AccountName = "Hawala Commission Income", AccountType = "Income", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 4, AccountCode = "3002", AccountName = "Exchange Income", AccountType = "Income", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 5, AccountCode = "4001", AccountName = "Office Expense", AccountType = "Expense", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt },
                new Account { Id = 6, AccountCode = "5001", AccountName = "Owner Capital", AccountType = "Equity", ReferenceType = null, ReferenceId = null, IsArchived = false, CreatedAt = createdAt }
            );
        }
    }
}