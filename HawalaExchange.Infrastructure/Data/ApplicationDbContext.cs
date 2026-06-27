using HawalaExchange.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Data   // ✅ Corrected namespace
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureIndexes(modelBuilder);
            ConfigureDefaultValues(modelBuilder);
            ConfigureDecimalPrecision(modelBuilder);
            ConfigureLedgerConstraints(modelBuilder);
            ConfigureRelationships(modelBuilder);
            SeedData(modelBuilder);
        }

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

        private static void ConfigureIndexes(ModelBuilder modelBuilder)
        {
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
        }

        private static void ConfigureDefaultValues(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Branch>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<User>().Property(x => x.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Currency>().Property(x => x.DecimalPlaces).HasDefaultValue(2);
            modelBuilder.Entity<Currency>().Property(x => x.IsActive).HasDefaultValue(true);
            modelBuilder.Entity<Account>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Customer>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<Correspondent>().Property(x => x.IsArchived).HasDefaultValue(false);
            modelBuilder.Entity<AccountBadehkarLimit>().Property(x => x.IsActive).HasDefaultValue(true);
        }

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
        }

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

        private static void ConfigureRelationships(ModelBuilder modelBuilder)
        {
            // --- User <-> Branch ---
            modelBuilder.Entity<User>()
                .HasOne(u => u.Branch)
                .WithMany(b => b.Users)
                .HasForeignKey(u => u.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transaction <-> Branch ---
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Branch)
                .WithMany(b => b.Transactions)
                .HasForeignKey(t => t.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transaction <-> Customer ---
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Customer)
                .WithMany(c => c.Transactions)
                .HasForeignKey(t => t.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transaction <-> CreatedByUser ---
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.CreatedByUser)
                .WithMany(u => u.CreatedTransactions)
                .HasForeignKey(t => t.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transaction <-> CancelledByUser ---
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.CancelledByUser)
                .WithMany(u => u.CancelledTransactions)   // User must have ICollection<Transaction> CancelledTransactions
                .HasForeignKey(t => t.CancelledBy)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transaction <-> ReversedTransaction (self-ref) ---
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.ReversedTransaction)
                .WithMany()
                .HasForeignKey(t => t.ReversedTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- TransactionDetail <-> Transaction ---
            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.Transaction)
                .WithMany(t => t.TransactionDetails)
                .HasForeignKey(td => td.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- TransactionDetail <-> Correspondent ---
            modelBuilder.Entity<TransactionDetail>()
                .HasOne(td => td.Correspondent)
                .WithMany(c => c.TransactionDetails)
                .HasForeignKey(td => td.CorrespondentId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- TransactionDetail <-> Currencies ---
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

            // --- LedgerEntry <-> Transaction ---
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Transaction)
                .WithMany(t => t.LedgerEntries)
                .HasForeignKey(le => le.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- LedgerEntry <-> Account ---
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Account)
                .WithMany(a => a.LedgerEntries)
                .HasForeignKey(le => le.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- LedgerEntry <-> Currency ---
            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Currency)
                .WithMany(c => c.LedgerEntries)
                .HasForeignKey(le => le.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transfer <-> Transaction ---
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.Transaction)
                .WithMany(tr => tr.Transfers)
                .HasForeignKey(t => t.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transfer <-> FromAccount ---
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.FromAccount)
                .WithMany(a => a.FromTransfers)    // Account must have ICollection<Transfer> FromTransfers
                .HasForeignKey(t => t.FromAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transfer <-> ToAccount ---
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.ToAccount)
                .WithMany(a => a.ToTransfers)      // Account must have ICollection<Transfer> ToTransfers
                .HasForeignKey(t => t.ToAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Transfer <-> Currency ---
            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.Currency)
                .WithMany()
                .HasForeignKey(t => t.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Expense <-> Transaction ---
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Transaction)
                .WithMany(t => t.Expenses)
                .HasForeignKey(e => e.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Expense <-> Currency ---
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Currency)
                .WithMany()
                .HasForeignKey(e => e.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- ExchangeRate <-> Currencies ---
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

            // --- ExchangeRate <-> User (CreatedBy) ---
            modelBuilder.Entity<ExchangeRate>()
                .HasOne(er => er.CreatedByUser)
                .WithMany()
                .HasForeignKey(er => er.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // --- AccountBadehkarLimit <-> Account ---
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.Account)
                .WithMany(a => a.AccountBadehkarLimits)   // Account must have ICollection<AccountBadehkarLimit>
                .HasForeignKey(abl => abl.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- AccountBadehkarLimit <-> Currency ---
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.Currency)
                .WithMany(c => c.AccountBadehkarLimits)   // Currency must have ICollection<AccountBadehkarLimit>
                .HasForeignKey(abl => abl.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- AccountBadehkarLimit <-> User (CreatedBy) ---
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(abl => abl.CreatedByUser)
                .WithMany()
                .HasForeignKey(abl => abl.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // --- AuditLog <-> User ---
            modelBuilder.Entity<AuditLog>()
                .HasOne(al => al.User)
                .WithMany()
                .HasForeignKey(al => al.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}