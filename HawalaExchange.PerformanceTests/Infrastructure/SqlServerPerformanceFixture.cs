using AutoMapper;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaSystem.Mappings;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests.Infrastructure;

public sealed class SqlServerPerformanceFixture : IAsyncLifetime
{
    private const string SafeDatabasePrefix = "HawalaExchangeQuettaPerformanceTests_";
    private readonly TestCurrentTenant currentTenant = new();
    private readonly string connectionString;

    public SqlServerPerformanceFixture()
    {
        var supplied = Environment.GetEnvironmentVariable("HAWALA_PERF_TEST_CONNECTION");
        var builder = new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(supplied)
            ? "Server=.;Integrated Security=True;TrustServerCertificate=True;Encrypt=False"
            : supplied)
        {
            InitialCatalog = $"{SafeDatabasePrefix}{Environment.ProcessId}_{Guid.NewGuid():N}"
        };

        connectionString = builder.ConnectionString;
        DatabaseName = builder.InitialCatalog;
    }

    public string DatabaseName { get; }
    public CommandCounterInterceptor Commands { get; } = new();
    public long UserId => currentTenant.UserId;

    public async Task InitializeAsync()
    {
        EnsureSafeDatabaseName();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        using var bypass = context.BypassSubscriptionEnforcement();
        var user = new ApplicationUser
        {
            TenantId = 1,
            UserName = $"perf-{Guid.NewGuid():N}",
            NormalizedUserName = $"PERF-{Guid.NewGuid():N}",
            LocalUserName = "performance-test",
            FullName = "Performance Test",
            BranchId = 1,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        currentTenant.UserId = user.Id;

        SourceCorrespondent = new Correspondent
        {
            Code = "PERF-SOURCE",
            Name = "Performance Source",
            CommissionMethod = "PeriodicPerLakh",
            SettlementCurrencyId = 2
        };
        DestinationCorrespondent = new Correspondent
        {
            Code = "PERF-DEST",
            Name = "Performance Destination",
            CommissionMethod = "PerTransaction",
            SettlementCurrencyId = 2
        };
        context.Correspondents.AddRange(SourceCorrespondent, DestinationCorrespondent);
        await context.SaveChangesAsync();

        SourceAccount = new Account
        {
            AccountCode = "PERF-SOURCE-ACCOUNT",
            AccountName = "Performance Source Account",
            AccountType = "Correspondent",
            CorrespondentId = SourceCorrespondent.Id
        };
        DestinationAccount = new Account
        {
            AccountCode = "PERF-DEST-ACCOUNT",
            AccountName = "Performance Destination Account",
            AccountType = "Correspondent",
            CorrespondentId = DestinationCorrespondent.Id
        };
        OwnLocation = new PaymentLocation
        {
            Name = "Kabul Performance",
            NormalizedName = PaymentLocationNameNormalizer.Normalize("Kabul Performance"),
            Address = "Kabul",
            CreatedBy = user.Id
        };
        RemoteLocation = new PaymentLocation
        {
            Name = "Remote Performance",
            NormalizedName = PaymentLocationNameNormalizer.Normalize("Remote Performance"),
            Address = "Remote",
            CreatedBy = user.Id
        };
        context.AddRange(SourceAccount, DestinationAccount, OwnLocation, RemoteLocation);
        await context.SaveChangesAsync();
        Commands.Reset();
    }

    public async Task DisposeAsync()
    {
        EnsureSafeDatabaseName();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(180))
            .AddInterceptors(Commands)
            .EnableDetailedErrors()
            .Options;
        return new ApplicationDbContext(options, currentTenant);
    }

    public HawalaService CreateService(ApplicationDbContext context)
    {
        var mapperConfig = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);
        var mapper = mapperConfig.CreateMapper();
        return new HawalaService(
            context,
            mapper,
            Mock.Of<ILedgerService>(),
            Mock.Of<IAccountService>(),
            Mock.Of<IAuditLogService>(),
            Mock.Of<IFileService>());
    }

    public CorrespondentService CreateCorrespondentService(ApplicationDbContext context)
    {
        var mapperConfig = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);
        var mapper = mapperConfig.CreateMapper();
        return new CorrespondentService(
            context,
            mapper,
            Mock.Of<IAccountService>(),
            Mock.Of<ILedgerService>(),
            Mock.Of<IAuditLogService>(),
            NullLogger<CorrespondentService>.Instance);
    }

    public ReportService CreateReportService(ApplicationDbContext context) => new(context);
    public FinancialReportService CreateFinancialReportService(ApplicationDbContext context) => new(context);
    public AccountService CreateAccountService(ApplicationDbContext context)
    {
        var mapperConfig = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);
        var mapper = mapperConfig.CreateMapper();
        return new AccountService(
            context,
            mapper,
            new LedgerService(context, mapper),
            Mock.Of<IAuditLogService>());
    }
    public JournalService CreateJournalService(ApplicationDbContext context) => new(context);
    public CorrespondentCommissionService CreateCommissionService(ApplicationDbContext context) => new(context);
    public CorrespondentSettlementService CreateSettlementService(ApplicationDbContext context) => new(context);
    public AedDealService CreateAedDealService(ApplicationDbContext context) => new(context);
    public BalanceService CreateBalanceService(ApplicationDbContext context)
    {
        var mapperConfig = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);
        var mapper = mapperConfig.CreateMapper();
        return new BalanceService(context, new LedgerService(context, mapper), mapper);
    }

    public HawalaImportService CreateImportService(ApplicationDbContext context) => new(
        context,
        CreateService(context),
        Mock.Of<IPaymentLocationService>(),
        Mock.Of<ICorrespondentService>(),
        Mock.Of<IAuditLogService>());

    public Correspondent SourceCorrespondent { get; private set; } = null!;
    public Correspondent DestinationCorrespondent { get; private set; } = null!;
    public Account SourceAccount { get; private set; } = null!;
    public Account DestinationAccount { get; private set; } = null!;
    public PaymentLocation OwnLocation { get; private set; } = null!;
    public PaymentLocation RemoteLocation { get; private set; } = null!;

    private void EnsureSafeDatabaseName()
    {
        if (!DatabaseName.StartsWith(SafeDatabasePrefix, StringComparison.Ordinal) ||
            DatabaseName.Length <= SafeDatabasePrefix.Length)
        {
            throw new InvalidOperationException(
                $"Unsafe performance-test database name: {DatabaseName}");
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerPerformanceCollection : ICollectionFixture<SqlServerPerformanceFixture>
{
    public const string Name = "SQL Server performance";
}
