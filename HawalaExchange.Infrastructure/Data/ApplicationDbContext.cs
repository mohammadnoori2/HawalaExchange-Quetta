using HawalaExchange.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ===== DbSets موجود =====
        public DbSet<Branch> Branches { get; set; }
        public DbSet<User> Users { get; set; }
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

        // ===== DbSets جدید برای گزارش‌ها =====
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
            SeedData(modelBuilder);
        }

        // ==========================================
        // ایندکس‌ها
        // ==========================================
        private static void ConfigureIndexes(ModelBuilder modelBuilder)
        {
            // ایندکس‌های موجود
            modelBuilder.Entity<Branch>().HasIndex(x => x.Code).IsUnique();
            modelBuilder.Entity<User>().HasIndex(x => x.UserName).IsUnique();
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

            // ایندکس‌های جدید برای گزارش‌ها
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
            // مقادیر پیش‌فرض موجود
            modelBuilder.Entity<Branch>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<User>().Property(x => x.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Currency>().Property(x => x.DecimalPlaces).HasDefaultValue(2);
            modelBuilder.Entity<Currency>().Property(x => x.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Account>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Customer>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Correspondent>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<AccountBadehkarLimit>().Property(x => x.IsActive).HasDefaultValue(true);

            // مقادیر پیش‌فرض برای گزارش‌ها
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
            // دقت اعداد موجود
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

            // دقت اعداد برای گزارش‌ها
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
            // --- روابط موجود ---
            // User <-> Branch
            modelBuilder.Entity<User>()
                .HasOne(u => u.Branch)
                .WithMany(b => b.Users)
                .HasForeignKey(u => u.BranchId)
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

            // Transaction <-> ReversedTransaction (self-ref)
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.ReversedTransaction)
                .WithMany()
                .HasForeignKey(t => t.ReversedTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // TransactionDetail <-> Transaction
            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.Transaction)
                .WithMany(t => t.TransactionDetails)
                .HasForeignKey(td => td.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // TransactionDetail <-> Correspondent
            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.Correspondent)
                .WithMany(c => c.TransactionDetails)
                .HasForeignKey(td => td.CorrespondentId)
                .OnDelete(DeleteBehavior.Restrict);

            // TransactionDetail <-> Currencies
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

            // LedgerEntry <-> Transaction
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Transaction)
                .WithMany(t => t.LedgerEntries)
                .HasForeignKey(le => le.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // LedgerEntry <-> Account
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Account)
                .WithMany(a => a.LedgerEntries)
                .HasForeignKey(le => le.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // LedgerEntry <-> Currency
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Currency)
                .WithMany(c => c.LedgerEntries)
                .HasForeignKey(le => le.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transfer <-> Transaction
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.Transaction)
                .WithMany(tr => tr.Transfers)
                .HasForeignKey(t => t.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transfer <-> FromAccount
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.FromAccount)
                .WithMany(a => a.FromTransfers)
                .HasForeignKey(t => t.FromAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transfer <-> ToAccount
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.ToAccount)
                .WithMany(a => a.ToTransfers)
                .HasForeignKey(t => t.ToAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transfer <-> Currency
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.Currency)
                .WithMany()
                .HasForeignKey(t => t.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense <-> Transaction
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Transaction)
                .WithMany(t => t.Expenses)
                .HasForeignKey(e => e.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense <-> Currency
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Currency)
                .WithMany()
                .HasForeignKey(e => e.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // ExchangeRate <-> Currencies
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

            // ExchangeRate <-> User (CreatedBy)
            modelBuilder.Entity<ExchangeRate>()
                .HasOne(er => er.CreatedByUser)
                .WithMany()
                .HasForeignKey(er => er.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // AccountBadehkarLimit <-> Account
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.Account)
                .WithMany(a => a.AccountBadehkarLimits)
                .HasForeignKey(abl => abl.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // AccountBadehkarLimit <-> Currency
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.Currency)
                .WithMany(c => c.AccountBadehkarLimits)
                .HasForeignKey(abl => abl.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // AccountBadehkarLimit <-> User (CreatedBy)
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.CreatedByUser)
                .WithMany()
                .HasForeignKey(abl => abl.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // AuditLog <-> User
            modelBuilder.Entity<AuditLog>()
                .HasOne(al => al.User)
                .WithMany()
                .HasForeignKey(al => al.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- روابط جدید برای گزارش‌ها ---
            // DailyReport <-> Branch
            modelBuilder.Entity<DailyReport>()
                .HasOne(r => r.Branch)
                .WithMany()
                .HasForeignKey(r => r.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            // TransactionReport <-> Transaction
            modelBuilder.Entity<TransactionReport>()
                .HasOne(r => r.Transaction)
                .WithMany()
                .HasForeignKey(r => r.TransactionId)
                .OnDelete(DeleteBehavior.SetNull);

            // CommissionReport <-> Branch
            modelBuilder.Entity<CommissionReport>()
                .HasOne(r => r.Branch)
                .WithMany()
                .HasForeignKey(r => r.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            // TrialBalance <-> Account
            modelBuilder.Entity<TrialBalance>()
                .HasOne(r => r.Account)
                .WithMany()
                .HasForeignKey(r => r.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        }

        // ==========================================
        // تنظیمات اختصاصی گزارش‌ها
        // ==========================================
        private static void ConfigureReportEntities(ModelBuilder modelBuilder)
        {
            // محدودیت‌های چک برای گزارش روزانه
            modelBuilder.Entity<DailyReport>()
                .HasCheckConstraint("CK_DailyReport_TotalSendAmount_NonNegative", "[TotalSendAmount] >= 0");

            modelBuilder.Entity<DailyReport>()
                .HasCheckConstraint("CK_DailyReport_TotalReceiveAmount_NonNegative", "[TotalReceiveAmount] >= 0");

            // می‌توان محدودیت‌های دیگری نیز افزود
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

            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = 1,
                    BranchId = 1,
                    FullName = "System Admin",
                    UserName = "admin",
                    PasswordHash = "CHANGE_THIS_PASSWORD_HASH",
                    Role = "Admin",
                    IsActive = true,
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