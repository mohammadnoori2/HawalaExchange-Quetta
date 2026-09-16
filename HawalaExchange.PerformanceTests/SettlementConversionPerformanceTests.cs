using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class SettlementConversionPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Selected_hawalas_are_converted_atomically_and_cannot_be_converted_twice()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (correspondent, account) = await CreateCorrespondentAsync(context, "SET-H");
        var hawala = NewHawala(97_000_001, correspondent.Id, 3, 100m);
        context.Hawalas.Add(hawala);
        await context.SaveChangesAsync();
        context.LedgerEntries.Add(NewLedger(account.Id, 3, 100m, 0, hawala.Id));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateSettlementService(context);
        var request = new ConvertHawalasToSettlementDto
        {
            CorrespondentId = correspondent.Id,
            HawalaIds = [hawala.Id],
            HawalaRates = [new HawalaSettlementRateDto
                { HawalaId = hawala.Id, SourceCurrencyId = 3, Rate = 1.10m }]
        };

        var result = await service.ConvertHawalasAsync(request);
        context.ChangeTracker.Clear();
        var conversion = await context.CorrespondentSettlementConversions.AsNoTracking()
            .Include(x => x.Hawalas).Include(x => x.HawalaItems)
            .SingleAsync(x => x.Id == result.ConversionId);
        var ledger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == result.TransactionId).ToListAsync();

        Assert.Equal(1, result.HawalaCount);
        Assert.Equal(1, result.CurrencyCount);
        Assert.Equal(110m, Assert.Single(result.Items).TargetAmount);
        Assert.Single(conversion.Hawalas);
        Assert.Single(conversion.HawalaItems);
        Assert.Equal(4, ledger.Count);
        AssertCurrencyBalanced(ledger, 3);
        AssertCurrencyBalanced(ledger, 2);

        var updated = await service.UpdateHawalaRateAsync(new UpdateHawalaSettlementRateDto
        {
            HawalaId = hawala.Id,
            SourceCurrencyId = 3,
            Rate = 1.20m
        });
        context.ChangeTracker.Clear();
        var updatedItem = await context.CorrespondentSettlementConversionHawalaItems.AsNoTracking()
            .SingleAsync(x => x.ConversionId == result.ConversionId);
        var updatedLedger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == result.TransactionId).ToListAsync();
        Assert.Equal(1.10m, updated.PreviousRate);
        Assert.Equal(120m, updated.TargetAmount);
        Assert.Equal(1.20m, updatedItem.ExchangeRate);
        Assert.Equal(120m, updatedItem.TargetTalabKar);
        Assert.Equal(4, updatedLedger.Count);
        AssertCurrencyBalanced(updatedLedger, 3);
        AssertCurrencyBalanced(updatedLedger, 2);
        await Assert.ThrowsAsync<SqlException>(() => service.ConvertHawalasAsync(request));
    }

    [Fact]
    public async Task Account_conversion_handles_both_quotation_directions_and_closes_source_balances()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (correspondent, account) = await CreateCorrespondentAsync(context, "SET-A");
        var afnHawala = NewHawala(97_000_002, correspondent.Id, 1, 7_000m);
        var eurHawala = NewHawala(97_000_003, correspondent.Id, 3, 100m);
        context.Hawalas.AddRange(afnHawala, eurHawala);
        await context.SaveChangesAsync();
        context.LedgerEntries.AddRange(
            NewLedger(account.Id, 1, 0, 7_000m, afnHawala.Id),
            NewLedger(account.Id, 3, 100m, 0, eurHawala.Id));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateSettlementService(context);

        var result = await service.ConvertBalanceAsync(new ConvertCorrespondentBalanceDto
        {
            CorrespondentId = correspondent.Id,
            Rates =
            [
                new SettlementRateDto { SourceCurrencyId = 1, Rate = 70m },
                new SettlementRateDto { SourceCurrencyId = 3, Rate = 1.20m }
            ]
        });
        context.ChangeTracker.Clear();
        var ledger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == result.TransactionId).ToListAsync();
        var preview = await service.GetPreviewAsync(correspondent.Id);

        Assert.Equal(2, result.HawalaCount);
        Assert.Equal(2, result.CurrencyCount);
        Assert.Equal(100m, result.Items.Single(x => x.SourceCurrencyCode == "AFN").TargetAmount);
        Assert.Equal("بدهکار", result.Items.Single(x => x.SourceCurrencyCode == "AFN").BalanceDirection);
        Assert.Equal(120m, result.Items.Single(x => x.SourceCurrencyCode == "EUR").TargetAmount);
        Assert.Equal("طلبکار", result.Items.Single(x => x.SourceCurrencyCode == "EUR").BalanceDirection);
        Assert.Equal(8, ledger.Count);
        AssertCurrencyBalanced(ledger, 1);
        AssertCurrencyBalanced(ledger, 2);
        AssertCurrencyBalanced(ledger, 3);
        Assert.Empty(preview.Balances);
        Assert.Equal(0, preview.PendingHawalaCount);
    }

    [Fact]
    public async Task Missing_hawala_rate_rolls_back_every_created_record()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (correspondent, account) = await CreateCorrespondentAsync(context, "SET-R");
        var hawala = NewHawala(97_000_004, correspondent.Id, 1, 7_000m);
        context.Hawalas.Add(hawala);
        await context.SaveChangesAsync();
        context.LedgerEntries.Add(NewLedger(account.Id, 1, 7_000m, 0, hawala.Id));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var beforeTransactions = await context.Transactions.CountAsync();
        var beforeConversions = await context.CorrespondentSettlementConversions.CountAsync();
        var beforeLedger = await context.LedgerEntries.CountAsync();
        var service = fixture.CreateSettlementService(context);

        await Assert.ThrowsAsync<SqlException>(() => service.ConvertHawalasAsync(new ConvertHawalasToSettlementDto
        {
            CorrespondentId = correspondent.Id,
            HawalaIds = [hawala.Id]
        }));

        Assert.Equal(beforeTransactions, await context.Transactions.CountAsync());
        Assert.Equal(beforeConversions, await context.CorrespondentSettlementConversions.CountAsync());
        Assert.Equal(beforeLedger, await context.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Concurrent_settlement_requests_create_only_one_conversion()
    {
        long correspondentId;
        long hawalaId;
        await using (var seedContext = fixture.CreateContext())
        {
            using var bypass = seedContext.BypassSubscriptionEnforcement();
            var (correspondent, account) = await CreateCorrespondentAsync(seedContext, "SET-C");
            var hawala = NewHawala(97_000_005, correspondent.Id, 1, 7_000m);
            seedContext.Hawalas.Add(hawala);
            await seedContext.SaveChangesAsync();
            seedContext.LedgerEntries.Add(NewLedger(account.Id, 1, 7_000m, 0, hawala.Id));
            await seedContext.SaveChangesAsync();
            correspondentId = correspondent.Id;
            hawalaId = hawala.Id;
        }

        var request = new ConvertHawalasToSettlementDto
        {
            CorrespondentId = correspondentId,
            HawalaIds = [hawalaId],
            HawalaRates = [new HawalaSettlementRateDto
                { HawalaId = hawalaId, SourceCurrencyId = 1, Rate = 70m }]
        };
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        using var firstBypass = firstContext.BypassSubscriptionEnforcement();
        using var secondBypass = secondContext.BypassSubscriptionEnforcement();

        async Task<object> TryConvertAsync(HawalaExchange.Infrastructure.Services.CorrespondentSettlementService service)
        {
            try { return await service.ConvertHawalasAsync(request); }
            catch (Exception exception) { return exception; }
        }

        var results = await Task.WhenAll(
            TryConvertAsync(fixture.CreateSettlementService(firstContext)),
            TryConvertAsync(fixture.CreateSettlementService(secondContext)));

        Assert.Single(results.OfType<CorrespondentSettlementResultDto>());
        Assert.Single(results.OfType<SqlException>());
        await using var verifyContext = fixture.CreateContext();
        Assert.Equal(1, await verifyContext.CorrespondentSettlementConversionHawalas
            .CountAsync(x => x.HawalaId == hawalaId));
    }

    [Fact]
    public async Task Measure_selected_hawala_conversion_for_10000_rows()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Settlement performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int rowCount = 10_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (correspondent, account) = await CreateCorrespondentAsync(context, "SET-P");
        var hawalas = new List<Hawala>(rowCount);
        for (var index = 0; index < rowCount; index++)
            hawalas.Add(NewHawala(98_000_000 + index, correspondent.Id, 1, 7_000m + index));
        context.Hawalas.AddRange(hawalas);
        await context.SaveChangesAsync();
        var ledger = hawalas.Select(x => NewLedger(account.Id, 1, x.FromAmount, 0, x.Id)).ToList();
        context.LedgerEntries.AddRange(ledger);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = fixture.CreateSettlementService(context);
        var request = new ConvertHawalasToSettlementDto
        {
            CorrespondentId = correspondent.Id,
            HawalaIds = hawalas.Select(x => x.Id).ToList(),
            HawalaRates = hawalas.Select(x => new HawalaSettlementRateDto
                { HawalaId = x.Id, SourceCurrencyId = 1, Rate = 70m }).ToList()
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await service.ConvertHawalasAsync(request);
        stopwatch.Stop();

        Assert.Equal(rowCount, result.HawalaCount);
        Assert.Equal(rowCount, result.Items.Count);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            inputRows = rowCount,
            elapsedMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            rowsPerSecond = Math.Round(rowCount / stopwatch.Elapsed.TotalSeconds, 2)
        }));
    }

    private async Task<(Correspondent Correspondent, Account Account)> CreateCorrespondentAsync(
        HawalaExchange.Infrastructure.Data.ApplicationDbContext context,
        string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var correspondent = new Correspondent
        {
            Code = $"{prefix}-{suffix}",
            Name = $"Settlement {suffix}",
            CommissionMethod = "PerTransaction",
            SettlementCurrencyId = 2
        };
        context.Correspondents.Add(correspondent);
        await context.SaveChangesAsync();
        var account = new Account
        {
            AccountCode = $"{prefix}-ACC-{suffix}",
            AccountName = $"Settlement Account {suffix}",
            AccountType = "Correspondent",
            CorrespondentId = correspondent.Id
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return (correspondent, account);
    }

    private Hawala NewHawala(long number, long correspondentId, long currencyId, decimal amount) => new()
    {
        Number = number,
        HawalaType = "HawalaReceive",
        CorrespondentId = correspondentId,
        PaymentLocationId = fixture.OwnLocation.Id,
        SenderName = "Settlement sender",
        ReceiverName = "Settlement receiver",
        FromCurrencyId = currencyId,
        FromAmount = amount,
        ToCurrencyId = currencyId,
        ToAmount = amount,
        ExchangeRate = 1,
        Status = "Paid",
        CreatedBy = fixture.UserId,
        CreatedAt = DateTime.UtcNow
    };

    private static LedgerEntry NewLedger(
        long accountId,
        long currencyId,
        decimal talabKar,
        decimal badehKar,
        long hawalaId) => new()
    {
        AccountId = accountId,
        CurrencyId = currencyId,
        TalabKar = talabKar,
        BadehKar = badehKar,
        HawalaId = hawalaId,
        Description = "Settlement performance seed",
        CreatedAt = DateTime.UtcNow
    };

    private static void AssertCurrencyBalanced(IEnumerable<LedgerEntry> entries, long currencyId)
    {
        var currencyEntries = entries.Where(x => x.CurrencyId == currencyId).ToList();
        Assert.NotEmpty(currencyEntries);
        Assert.Equal(currencyEntries.Sum(x => x.TalabKar), currencyEntries.Sum(x => x.BadehKar));
    }
}
