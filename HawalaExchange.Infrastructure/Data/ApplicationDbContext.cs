using Microsoft.EntityFrameworkCore;
using YourNamespace.Entities;

namespace YourNamespace.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

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
                    PhoneNumber = "null",
                    Address = " ",
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
                new Currency
                {
                    Id = 1,
                    Code = "AFN",
                    Name = "Afghani",
                    Symbol = "؋",
                    DecimalPlaces = 2,
                    IsActive = true
                },
                new Currency
                {
                    Id = 2,
                    Code = "USD",
                    Name = "US Dollar",
                    Symbol = "$",
                    DecimalPlaces = 2,
                    IsActive = true
                },
                new Currency
                {
                    Id = 3,
                    Code = "EUR",
                    Name = "Euro",
                    Symbol = "€",
                    DecimalPlaces = 2,
                    IsActive = true
                },
                new Currency
                {
                    Id = 4,
                    Code = "AED",
                    Name = "UAE Dirham",
                    Symbol = "د.إ",
                    DecimalPlaces = 2,
                    IsActive = true
                },
                new Currency
                {
                    Id = 5,
                    Code = "IRR",
                    Name = "Iranian Rial",
                    Symbol = "﷼",
                    DecimalPlaces = 2,
                    IsActive = true
                },
                new Currency
                {
                    Id = 6,
                    Code = "PKR",
                    Name = "Pakistani Rupee",
                    Symbol = "₨",
                    DecimalPlaces = 2,
                    IsActive = true
                }
            );

            modelBuilder.Entity<Account>().HasData(
                new Account
                {
                    Id = 1,
                    AccountCode = "1001",
                    AccountName = "Cash",
                    AccountType = "Cash",
                    ReferenceType = null,
                    ReferenceId = null,
                    IsActive = true,
                    IsArchived = false,
                    CreatedAt = createdAt
                },
                new Account
                {
                    Id = 2,
                    AccountCode = "1101",
                    AccountName = "Bank",
                    AccountType = "Bank",
                    ReferenceType = null,
                    ReferenceId = null,
                    IsActive = true,
                    IsArchived = false,
                    CreatedAt = createdAt
                },
                new Account
                {
                    Id = 3,
                    AccountCode = "3001",
                    AccountName = "Hawala Commission Income",
                    AccountType = "Income",
                    ReferenceType = null,
                    ReferenceId = null,
                    IsActive = true,
                    IsArchived = false,
                    CreatedAt = createdAt
                },
                new Account
                {
                    Id = 4,
                    AccountCode = "3002",
                    AccountName = "Exchange Income",
                    AccountType = "Income",
                    ReferenceType = null,
                    ReferenceId = null,
                    IsActive = true,
                    IsArchived = false,
                    CreatedAt = createdAt
                },
                new Account
                {
                    Id = 5,
                    AccountCode = "4001",
                    AccountName = "Office Expense",
                    AccountType = "Expense",
                    ReferenceType = null,
                    ReferenceId = null,
                    IsActive = true,
                    IsArchived = false,
                    CreatedAt = createdAt
                },
                new Account
                {
                    Id = 6,
                    AccountCode = "5001",
                    AccountName = "Owner Capital",
                    AccountType = "Equity",
                    ReferenceType = null,
                    ReferenceId = null,
                    IsActive = true,
                    IsArchived = false,
                    CreatedAt = createdAt
                }
            );
        }
        private static void ConfigureIndexes(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Branch>()
                .HasIndex(x => x.Code)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(x => x.UserName)
                .IsUnique();

            modelBuilder.Entity<Customer>()
                .HasIndex(x => x.CustomerCode)
                .IsUnique();

            modelBuilder.Entity<Correspondent>()
                .HasIndex(x => x.Code)
                .IsUnique();

            modelBuilder.Entity<Currency>()
                .HasIndex(x => x.Code)
                .IsUnique();

            modelBuilder.Entity<Account>()
                .HasIndex(x => x.AccountCode)
                .IsUnique();

            modelBuilder.Entity<Transaction>()
                .HasIndex(x => x.TransactionNo)
                .IsUnique();

            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasIndex(x => new { x.AccountId, x.CurrencyId })
                .IsUnique();

            modelBuilder.Entity<LedgerEntry>()
                .HasIndex(x => new { x.AccountId, x.CurrencyId });

            modelBuilder.Entity<LedgerEntry>()
                .HasIndex(x => x.TransactionId);

            modelBuilder.Entity<ExchangeRate>()
                .HasIndex(x => new { x.FromCurrencyId, x.ToCurrencyId, x.EffectiveDate });

            modelBuilder.Entity<Document>()
                .HasIndex(x => new { x.EntityType, x.EntityId });

            modelBuilder.Entity<AuditLog>()
                .HasIndex(x => new { x.TableName, x.RecordId });
        }

        private static void ConfigureDefaultValues(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Branch>()
                .Property(x => x.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<User>()
                .Property(x => x.IsActive)
                .HasDefaultValue(true);

            modelBuilder.Entity<Currency>()
                .Property(x => x.DecimalPlaces)
                .HasDefaultValue(2);

            modelBuilder.Entity<Currency>()
                .Property(x => x.IsActive)
                .HasDefaultValue(true);

            modelBuilder.Entity<Account>()
                .Property(x => x.IsActive)
                .HasDefaultValue(true);

            modelBuilder.Entity<Account>()
                .Property(x => x.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<Customer>()
                .Property(x => x.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<Correspondent>()
                .Property(x => x.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .Property(x => x.IsActive)
                .HasDefaultValue(true);
        }

        private static void ConfigureDecimalPrecision(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LedgerEntry>()
                .Property(x => x.TalabKar)
                .HasPrecision(18, 4);

            modelBuilder.Entity<LedgerEntry>()
                .Property(x => x.BadehKar)
                .HasPrecision(18, 4);

            modelBuilder.Entity<TransactionDetail>()
                .Property(x => x.FromAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<TransactionDetail>()
                .Property(x => x.ToAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<TransactionDetail>()
                .Property(x => x.ExchangeRate)
                .HasPrecision(18, 8);

            modelBuilder.Entity<TransactionDetail>()
                .Property(x => x.TransferAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<TransactionDetail>()
                .Property(x => x.CommissionAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<TransactionDetail>()
                .Property(x => x.AgentCommissionAmount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<Transfer>()
                .Property(x => x.Amount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<ExchangeRate>()
                .Property(x => x.BuyRate)
                .HasPrecision(18, 8);

            modelBuilder.Entity<ExchangeRate>()
                .Property(x => x.SellRate)
                .HasPrecision(18, 8);

            modelBuilder.Entity<Expense>()
                .Property(x => x.Amount)
                .HasPrecision(18, 4);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .Property(x => x.BadehkarLimit)
                .HasPrecision(18, 4);
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
            modelBuilder.Entity<User>()
                .HasOne(x => x.Branch)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(x => x.Branch)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(x => x.Customer)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(x => x.CreatedByUser)
                .WithMany(x => x.CreatedTransactions)
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(x => x.CancelledByUser)
                .WithMany(x => x.CancelledTransactions)
                .HasForeignKey(x => x.CancelledBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne<Transaction>()
                .WithMany()
                .HasForeignKey(x => x.ReversedTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(x => x.Transaction)
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(x => x.Correspondent)
                .WithMany(x => x.TransactionDetails)
                .HasForeignKey(x => x.CorrespondentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(x => x.FromCurrency)
                .WithMany()
                .HasForeignKey(x => x.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(x => x.ToCurrency)
                .WithMany()
                .HasForeignKey(x => x.ToCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(x => x.CommissionCurrency)
                .WithMany()
                .HasForeignKey(x => x.CommissionCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionDetail>()
                .HasOne(x => x.AgentCommissionCurrency)
                .WithMany()
                .HasForeignKey(x => x.AgentCommissionCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.Transaction)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.Account)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(x => x.Currency)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transfer>()
                .HasOne(x => x.Transaction)
                .WithMany(x => x.Transfers)
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transfer>()
                .HasOne(x => x.FromAccount)
                .WithMany(x => x.FromTransfers)
                .HasForeignKey(x => x.FromAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transfer>()
                .HasOne(x => x.ToAccount)
                .WithMany(x => x.ToTransfers)
                .HasForeignKey(x => x.ToAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transfer>()
                .HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Expense>()
                .HasOne(x => x.Transaction)
                .WithMany(x => x.Expenses)
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Expense>()
                .HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExchangeRate>()
                .HasOne(x => x.FromCurrency)
                .WithMany()
                .HasForeignKey(x => x.FromCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExchangeRate>()
                .HasOne(x => x.ToCurrency)
                .WithMany()
                .HasForeignKey(x => x.ToCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ExchangeRate>()
                .HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(x => x.Account)
                .WithMany(x => x.AccountBadehkarLimits)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(x => x.Currency)
                .WithMany(x => x.AccountBadehkarLimits)
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AuditLog>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}