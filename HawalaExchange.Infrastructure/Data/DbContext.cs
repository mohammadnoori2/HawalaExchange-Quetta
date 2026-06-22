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

            // LedgerEntry constraints
            modelBuilder.Entity<LedgerEntry>()
                .HasCheckConstraint("CK_LedgerEntries_TalabKar", "TalabKar >= 0");

            modelBuilder.Entity<LedgerEntry>()
                .HasCheckConstraint("CK_LedgerEntries_BadehKar", "BadehKar >= 0");

            modelBuilder.Entity<LedgerEntry>()
                .HasCheckConstraint("CK_LedgerEntries_OnlyOneSide",
                    "(TalabKar > 0 AND BadehKar = 0) OR (TalabKar = 0 AND BadehKar > 0)");

            // Unique constraint for AccountBadehkarLimit
            modelBuilder.Entity<AccountBadehkarLimit>()
                .HasIndex(a => new { a.AccountId, a.CurrencyId })
                .IsUnique();

            // Set default values
            modelBuilder.Entity<Branch>()
                .Property(b => b.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<User>()
                .Property(u => u.IsActive)
                .HasDefaultValue(true);

            modelBuilder.Entity<Currency>()
                .Property(c => c.DecimalPlaces)
                .HasDefaultValue(2);

            modelBuilder.Entity<Currency>()
                .Property(c => c.IsActive)
                .HasDefaultValue(true);

            modelBuilder.Entity<Account>()
                .Property(a => a.IsActive)
                .HasDefaultValue(true);

            modelBuilder.Entity<Account>()
                .Property(a => a.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<Customer>()
                .Property(c => c.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<Correspondent>()
                .Property(c => c.IsArchived)
                .HasDefaultValue(false);

            modelBuilder.Entity<AccountBadehkarLimit>()
                .Property(a => a.IsActive)
                .HasDefaultValue(true);

            // Relationship configurations
            modelBuilder.Entity<User>()
                .HasOne(u => u.Branch)
                .WithMany(b => b.Users)
                .HasForeignKey(u => u.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Branch)
                .WithMany(b => b.Transactions)
                .HasForeignKey(t => t.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Customer)
                .WithMany(c => c.Transactions)
                .HasForeignKey(t => t.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.CreatedByUser)
                .WithMany(u => u.CreatedTransactions)
                .HasForeignKey(t => t.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.CancelledByUser)
                .WithMany(u => u.CancelledTransactions)
                .HasForeignKey(t => t.CancelledBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LedgerEntry>()
                .HasOne(le => le.Transaction)
                .WithMany(t => t.LedgerEntries)
                .HasForeignKey(le => le.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Transfer>()
                .HasOne(t => t.Transaction)
                .WithMany(tr => tr.Transfers)
                .HasForeignKey(t => t.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Transaction)
                .WithMany(t => t.Expenses)
                .HasForeignKey(e => e.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}