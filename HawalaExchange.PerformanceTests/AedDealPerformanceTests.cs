using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class AedDealPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Aed_deal_calculation_and_ledger_match_business_rules()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (source, dubai) = await CreateCorrespondentsAsync(context, "AED-C");
        var service = fixture.CreateAedDealService(context);

        fixture.Commands.Reset();
        var createWatch = Stopwatch.StartNew();
        var deal = await service.CreateAsync(new CreateAedDealDto
        {
            DealNumber = $"AED-{Guid.NewGuid():N}",
            SourceCorrespondentId = source.Id,
            DubaiCorrespondentId = dubai.Id,
            SourceCurrencyId = 4,
            Amount = 450_000m,
            RoundingDecimalPlaces = 0
        });
        createWatch.Stop();
        var createCommands = fixture.Commands.Count;
        var holdingLedger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == context.AedDeals
                .Where(d => d.Id == deal.Id).Select(d => d.HoldingTransactionId).Single())
            .ToListAsync();

        fixture.Commands.Reset();
        var conversionWatch = Stopwatch.StartNew();
        var converted = await service.ConvertAsync(new PreviewAedConversionDto
        {
            DealId = deal.Id,
            SourceAmount = 450_000m,
            ActualMarker = 200m,
            DeclaredMarker = 150m
        });
        conversionWatch.Stop();
        var conversionCommands = fixture.Commands.Count;
        var conversion = Assert.Single(converted.Conversions);
        var transactionId = await context.AedDealConversions
            .Where(x => x.Id == conversion.Id).Select(x => x.PostingTransactionId).SingleAsync();
        var conversionLedger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == transactionId).ToListAsync();

        Assert.Equal(3.67m, converted.AedPerUsdRate);
        Assert.Equal(122_861m, conversion.FinalUsdAmount);
        Assert.Equal(122_800m, conversion.DeclaredUsdAmount);
        Assert.Equal(61m, conversion.ProfitUsd);
        Assert.Equal("Converted", converted.Status);
        AssertBalanced(holdingLedger, 4);
        AssertBalanced(conversionLedger, 4);
        AssertBalanced(conversionLedger, 2);
        Assert.InRange(createCommands, 1, 3);
        Assert.InRange(conversionCommands, 1, 3);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            createMilliseconds = Math.Round(createWatch.Elapsed.TotalMilliseconds, 2),
            createCommands,
            conversionMilliseconds = Math.Round(conversionWatch.Elapsed.TotalMilliseconds, 2),
            conversionCommands
        }));
    }

    [Fact]
    public async Task Usd_deal_with_negative_marker_posts_balanced_loss()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (source, dubai) = await CreateCorrespondentsAsync(context, "AED-L");
        var service = fixture.CreateAedDealService(context);
        var deal = await CreateDealAsync(service, source.Id, dubai.Id, 2, 100_000m, 0);

        var converted = await service.ConvertAsync(new PreviewAedConversionDto
        {
            DealId = deal.Id,
            SourceAmount = 100_000m,
            ActualMarker = -200m,
            DeclaredMarker = -150m
        });

        var conversion = Assert.Single(converted.Conversions);
        Assert.Equal(99_800m, conversion.FinalUsdAmount);
        Assert.Equal(99_850m, conversion.DeclaredUsdAmount);
        Assert.Equal(-50m, conversion.ProfitUsd);
        var lossAccountId = await context.Accounts.AsNoTracking()
            .Where(x => x.AccountCode == "4003" && x.AccountType == "Expense")
            .Select(x => x.Id).SingleAsync();
        var transactionId = await context.AedDealConversions.AsNoTracking()
            .Where(x => x.Id == conversion.Id).Select(x => x.PostingTransactionId).SingleAsync();
        var entries = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == transactionId).ToListAsync();
        Assert.Contains(entries, x => x.AccountId == lossAccountId && x.BadehKar == 50m);
        AssertBalanced(entries, 2);
    }

    [Fact]
    public async Task Rounding_uses_half_away_from_zero_with_configured_decimal_places()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (source, dubai) = await CreateCorrespondentsAsync(context, "AED-G");
        var service = fixture.CreateAedDealService(context);
        var deal = await CreateDealAsync(service, source.Id, dubai.Id, 2, 100_000m, 0);

        var preview = await service.PreviewConversionAsync(new PreviewAedConversionDto
        {
            DealId = deal.Id,
            SourceAmount = 100_000m,
            ActualMarker = 0.50m,
            DeclaredMarker = 0.49m
        });
        var posted = await service.ConvertAsync(new PreviewAedConversionDto
        {
            DealId = deal.Id,
            SourceAmount = 100_000m,
            ActualMarker = 0.50m,
            DeclaredMarker = 0.49m
        });

        Assert.Equal(100_001m, preview.FinalUsdAmount);
        Assert.Equal(100_000m, preview.DeclaredUsdAmount);
        var conversion = Assert.Single(posted.Conversions);
        Assert.Equal(preview.FinalUsdAmount, conversion.FinalUsdAmount);
        Assert.Equal(preview.DeclaredUsdAmount, conversion.DeclaredUsdAmount);
        Assert.Equal(1m, conversion.ProfitUsd);
    }

    [Fact]
    public async Task Partial_conversion_can_be_reversed_and_then_deal_cancelled()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (source, dubai) = await CreateCorrespondentsAsync(context, "AED-R");
        var service = fixture.CreateAedDealService(context);
        var deal = await CreateDealAsync(service, source.Id, dubai.Id, 4, 450_000m, 0);
        var converted = await service.ConvertAsync(new PreviewAedConversionDto
        {
            DealId = deal.Id,
            SourceAmount = 100_000m,
            ActualMarker = 200m,
            DeclaredMarker = 150m
        });
        Assert.Equal("PartiallyConverted", converted.Status);
        Assert.Equal(350_000m, converted.RemainingAmount);
        var conversion = Assert.Single(converted.Conversions);
        var postingTransactionId = await context.AedDealConversions.AsNoTracking()
            .Where(x => x.Id == conversion.Id).Select(x => x.PostingTransactionId).SingleAsync();
        var originalEntries = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == postingTransactionId).ToListAsync();

        await service.ReverseConversionAsync(conversion.Id, "تست برگشت تبدیل");
        var reversedDeal = await context.AedDeals.AsNoTracking().SingleAsync(x => x.Id == deal.Id);
        var reversedConversion = await context.AedDealConversions.AsNoTracking().SingleAsync(x => x.Id == conversion.Id);
        Assert.Equal("Held", reversedDeal.Status);
        Assert.Equal(0m, reversedDeal.ConvertedAmount);
        Assert.Equal(0m, reversedDeal.TotalFinalUsd);
        Assert.Equal(0m, reversedDeal.TotalProfitUsd);
        Assert.Equal("Reversed", reversedConversion.Status);
        Assert.NotNull(reversedConversion.ReversalTransactionId);
        var reversalEntries = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.TransactionId == reversedConversion.ReversalTransactionId).ToListAsync();
        Assert.Equal(originalEntries.Count, reversalEntries.Count);
        foreach (var original in originalEntries)
            Assert.Contains(reversalEntries, reverse => reverse.AccountId == original.AccountId &&
                reverse.CurrencyId == original.CurrencyId && reverse.TalabKar == original.BadehKar &&
                reverse.BadehKar == original.TalabKar);

        var holdingTransactionId = reversedDeal.HoldingTransactionId;
        await service.CancelAsync(deal.Id, "تست لغو معامله");
        var cancelled = await context.AedDeals.AsNoTracking().SingleAsync(x => x.Id == deal.Id);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.ReversalTransactionId);
        Assert.Equal("Cancel", await context.Transactions.AsNoTracking()
            .Where(x => x.Id == holdingTransactionId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Failed_cancel_and_over_conversion_leave_accounting_unchanged()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (source, dubai) = await CreateCorrespondentsAsync(context, "AED-X");
        var service = fixture.CreateAedDealService(context);
        var deal = await CreateDealAsync(service, source.Id, dubai.Id, 4, 100_000m, 0);
        var converted = await service.ConvertAsync(new PreviewAedConversionDto
        {
            DealId = deal.Id, SourceAmount = 50_000m, ActualMarker = 200m, DeclaredMarker = 150m
        });
        var transactionCount = await context.Transactions.CountAsync();
        var ledgerCount = await context.LedgerEntries.CountAsync();

        var cancelError = await Assert.ThrowsAnyAsync<Exception>(() =>
            service.CancelAsync(deal.Id, "نباید لغو شود"));
        Assert.Contains("ابتدا تمام تبدیل", cancelError.Message);
        var conversionError = await Assert.ThrowsAnyAsync<Exception>(() =>
            service.ConvertAsync(new PreviewAedConversionDto
            {
                DealId = deal.Id, SourceAmount = 50_001m, ActualMarker = 200m, DeclaredMarker = 150m
            }));
        Assert.Contains("بیشتر از مانده", conversionError.Message);

        Assert.Equal(transactionCount, await context.Transactions.CountAsync());
        Assert.Equal(ledgerCount, await context.LedgerEntries.CountAsync());
        var unchanged = await context.AedDeals.AsNoTracking().SingleAsync(x => x.Id == deal.Id);
        Assert.Equal("PartiallyConverted", unchanged.Status);
        Assert.Equal(50_000m, unchanged.ConvertedAmount);
        Assert.Equal(converted.TotalFinalUsd, unchanged.TotalFinalUsd);
    }

    [Fact]
    public async Task Concurrent_full_conversions_allow_only_one_posting()
    {
        long dealId;
        await using (var seed = fixture.CreateContext())
        {
            using var bypass = seed.BypassSubscriptionEnforcement();
            var (source, dubai) = await CreateCorrespondentsAsync(seed, "AED-Q");
            var deal = await CreateDealAsync(fixture.CreateAedDealService(seed), source.Id, dubai.Id, 4, 100_000m, 0);
            dealId = deal.Id;
        }

        static async Task<Exception?> TryConvertAsync(SqlServerPerformanceFixture testFixture, long id)
        {
            try
            {
                await using var context = testFixture.CreateContext();
                using var bypass = context.BypassSubscriptionEnforcement();
                await testFixture.CreateAedDealService(context).ConvertAsync(new PreviewAedConversionDto
                {
                    DealId = id, SourceAmount = 100_000m, ActualMarker = 200m, DeclaredMarker = 150m
                });
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(TryConvertAsync(fixture, dealId), TryConvertAsync(fixture, dealId));
        Assert.Single(results, x => x == null);
        Assert.Single(results, x => x != null);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.AedDealConversions.CountAsync(x => x.AedDealId == dealId && x.Status == "Posted"));
        var stored = await verify.AedDeals.AsNoTracking().SingleAsync(x => x.Id == dealId);
        Assert.Equal("Converted", stored.Status);
        Assert.Equal(100_000m, stored.ConvertedAmount);
    }

    [Fact]
    public async Task Tenant_scope_cannot_use_a_user_from_another_tenant()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (source, dubai) = await CreateCorrespondentsAsync(context, "AED-T");
        using var otherTenant = context.UseTenantScope(2);
        var service = fixture.CreateAedDealService(context);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => CreateDealAsync(
            service, source.Id, dubai.Id, 4, 100_000m, 0));

        Assert.Contains("کاربر جاری معتبر نیست", error.Message);
        using var tenantOne = context.UseTenantScope(1);
        Assert.False(await context.AedDeals.AnyAsync(x => x.SourceCorrespondentId == source.Id));
    }

    private static Task<AedDealDto> CreateDealAsync(
        HawalaExchange.Infrastructure.Services.AedDealService service,
        long sourceId,
        long dubaiId,
        long currencyId,
        decimal amount,
        int roundingPlaces) => service.CreateAsync(new CreateAedDealDto
        {
            DealNumber = $"AED-{Guid.NewGuid():N}",
            SourceCorrespondentId = sourceId,
            DubaiCorrespondentId = dubaiId,
            SourceCurrencyId = currencyId,
            Amount = amount,
            RoundingDecimalPlaces = roundingPlaces
        });

    private async Task<(Correspondent Source, Correspondent Dubai)> CreateCorrespondentsAsync(
        HawalaExchange.Infrastructure.Data.ApplicationDbContext context,
        string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var source = new Correspondent
        {
            Code = $"{prefix}-S-{suffix}", Name = $"Quetta {suffix}", CommissionMethod = "PerTransaction"
        };
        var dubai = new Correspondent
        {
            Code = $"{prefix}-D-{suffix}", Name = $"Dubai {suffix}", CommissionMethod = "PerTransaction"
        };
        context.Correspondents.AddRange(source, dubai);
        await context.SaveChangesAsync();
        context.Accounts.AddRange(
            NewAccount(source, $"{prefix}-SA-{suffix}"),
            NewAccount(dubai, $"{prefix}-DA-{suffix}"));
        await context.SaveChangesAsync();
        return (source, dubai);
    }

    private static Account NewAccount(Correspondent correspondent, string code) => new()
    {
        AccountCode = code,
        AccountName = $"Account {code}",
        AccountType = "Correspondent",
        CorrespondentId = correspondent.Id
    };

    private static void AssertBalanced(IEnumerable<LedgerEntry> entries, long currencyId)
    {
        var rows = entries.Where(x => x.CurrencyId == currencyId).ToList();
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Sum(x => x.TalabKar), rows.Sum(x => x.BadehKar));
    }
}
