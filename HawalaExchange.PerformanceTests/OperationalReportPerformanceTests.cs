using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class OperationalReportPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Reports_match_hawala_commission_expense_and_currency_filters()
    {
        var reportDate = new DateTime(2031, 2, 10);
        var createdAt = reportDate.AddHours(10).ToUniversalTime();
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();

        context.Hawalas.AddRange(
            NewHawala(91_000_001, "HawalaSend", 100m, 10m, 3m, "Paid", createdAt),
            NewHawala(91_000_002, "HawalaReceive", 200m, 2m, null, "Pending", createdAt),
            NewHawala(91_000_003, "HawalaSend", 999m, 99m, 50m, "Cancel", createdAt));
        context.Expenses.Add(new Expense
        {
            ExpenseDate = createdAt,
            Title = "Report test expense",
            CurrencyId = 2,
            Amount = 20m,
            ExpenseAccountId = 5,
            PaidFromAccountId = 1,
            CreatedBy = fixture.UserId,
            CreatedAt = createdAt
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var service = fixture.CreateReportService(context);
        var daily = await service.GetDailyReportAsync(reportDate, 1, 2);
        var commissions = (await service.GetCommissionReportAsync(reportDate, reportDate, 1, 2)).Single();
        var paidTransactions = (await service.GetTransactionReportAsync(
            reportDate, reportDate, 1, "Paid", 2)).ToList();

        Assert.Equal("USD", daily.CurrencyCode);
        Assert.Equal(2, daily.TotalTransactions);
        Assert.Equal(100m, daily.TotalSendAmount);
        Assert.Equal(200m, daily.TotalReceiveAmount);
        Assert.Equal(12m, daily.TotalCommission);
        Assert.Equal(3m, daily.TotalAgentCommission);
        Assert.Equal(20m, daily.TotalExpenses);
        Assert.Equal(-11m, daily.NetIncome);
        Assert.Equal(12m, commissions.TotalCommission);
        Assert.Equal(3m, commissions.TotalAgentCommission);
        Assert.Equal(9m, commissions.NetCommission);
        Assert.Single(paidTransactions);
        Assert.Equal("91000001", paidTransactions[0].TransactionNo);
        Assert.Equal("USD", paidTransactions[0].CommissionCurrency);
    }

    [Fact]
    public async Task Report_procedures_are_tenant_scoped()
    {
        var reportDate = new DateTime(2031, 2, 10);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var suffix = Guid.NewGuid().ToString("N");
        var foreignTenant = new Tenant { Name = $"Report tenant {suffix}", IsActive = true };
        context.Tenants.Add(foreignTenant);
        await context.SaveChangesAsync();

        using (context.UseTenantScope(foreignTenant.Id))
        {
            var branch = new Branch { Code = $"R{suffix[..8]}", Name = "Foreign report branch" };
            var currency = new Currency { Code = "USD", Name = "US Dollar", IsActive = true };
            context.AddRange(branch, currency);
            await context.SaveChangesAsync();

            var user = new ApplicationUser
            {
                UserName = $"report-{suffix}",
                NormalizedUserName = $"REPORT-{suffix.ToUpperInvariant()}",
                LocalUserName = $"report-{suffix}",
                FullName = "Foreign Report User",
                BranchId = branch.Id,
                IsActive = true
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var correspondent = new Correspondent
            {
                Code = $"C{suffix[..8]}",
                Name = "Foreign report correspondent",
                CommissionMethod = "PerTransaction"
            };
            var location = new PaymentLocation
            {
                Name = $"Foreign {suffix[..8]}",
                NormalizedName = $"FOREIGN {suffix[..8]}",
                CreatedBy = user.Id
            };
            context.AddRange(correspondent, location);
            await context.SaveChangesAsync();

            context.Hawalas.Add(new Hawala
            {
                Number = 99_000_001,
                HawalaType = "HawalaSend",
                CorrespondentId = correspondent.Id,
                PaymentLocationId = location.Id,
                SenderName = "Foreign Sender",
                ReceiverName = "Foreign Receiver",
                FromCurrencyId = currency.Id,
                FromAmount = 999m,
                ToCurrencyId = currency.Id,
                ToAmount = 999m,
                Status = "Paid",
                CreatedBy = user.Id,
                CreatedAt = reportDate.AddHours(10).ToUniversalTime()
            });
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();
        var service = fixture.CreateReportService(context);

        var rows = (await service.GetTransactionReportAsync(reportDate, reportDate)).ToList();

        Assert.All(rows, row => Assert.StartsWith("9100000", row.TransactionNo));
        Assert.DoesNotContain(rows, row => row.TransactionNo == "99000001");
    }

    [Fact]
    public async Task Measure_daily_report_stored_procedure()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Report performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int rowCount = 10_000;
        const long numberBase = 92_000_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var hawalaService = fixture.CreateService(context);
        var createdAt = DateTime.Now;
        await hawalaService.CreateHawalasAsync(BuildItems(rowCount, numberBase));
        context.ChangeTracker.Clear();
        var service = fixture.CreateReportService(context);

        var oldDurations = new List<double>();
        var procedureDurations = new List<double>();
        for (var run = 0; run < 3; run++)
        {
            context.ChangeTracker.Clear();
            var oldStopwatch = Stopwatch.StartNew();
            var materialized = await context.Hawalas.AsNoTracking()
                .Where(x => x.CreatedAt >= createdAt.Date.ToUniversalTime() &&
                            x.CreatedAt < createdAt.Date.AddDays(1).ToUniversalTime())
                .ToListAsync();
            _ = materialized.Count(x => x.Status != "Cancel");
            _ = materialized.Where(x => x.HawalaType == "HawalaSend").Sum(x => x.FromAmount);
            _ = materialized.Where(x => x.HawalaType == "HawalaReceive").Sum(x => x.ToAmount ?? x.FromAmount);
            oldStopwatch.Stop();
            oldDurations.Add(oldStopwatch.Elapsed.TotalMilliseconds);

            var procedureStopwatch = Stopwatch.StartNew();
            var report = await service.GetDailyReportAsync(createdAt, 1, 2);
            procedureStopwatch.Stop();
            procedureDurations.Add(procedureStopwatch.Elapsed.TotalMilliseconds);
            Assert.True(report.TotalTransactions >= rowCount);
        }

        oldDurations.Sort();
        procedureDurations.Sort();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            inputRows = rowCount,
            previousMedianMilliseconds = Math.Round(oldDurations[1], 2),
            storedProcedureMedianMilliseconds = Math.Round(procedureDurations[1], 2),
            improvementPercent = Math.Round((1 - procedureDurations[1] / oldDurations[1]) * 100, 2)
        }));
    }

    private Hawala NewHawala(
        long number,
        string type,
        decimal amount,
        decimal? commission,
        decimal? agentCommission,
        string status,
        DateTime createdAt) => new()
    {
        Number = number,
        HawalaType = type,
        CorrespondentId = fixture.SourceCorrespondent.Id,
        PaymentLocationId = fixture.OwnLocation.Id,
        SenderName = "Report Sender",
        ReceiverName = "Report Receiver",
        FromCurrencyId = 2,
        FromAmount = amount,
        ToCurrencyId = 2,
        ToAmount = amount,
        ExchangeRate = 1,
        CommissionAmount = commission,
        CommissionCurrencyId = commission.HasValue ? 2 : null,
        AgentCommissionAmount = agentCommission,
        AgentCommissionCurrencyId = agentCommission.HasValue ? 2 : null,
        Status = status,
        CreatedBy = fixture.UserId,
        CreatedAt = createdAt
    };

    private IReadOnlyList<CreateHawalaDto> BuildItems(int rowCount, long numberBase)
    {
        var rows = new List<CreateHawalaDto>(rowCount);
        for (var index = 0; index < rowCount; index++)
        {
            var remote = index % 5 != 0;
            rows.Add(new CreateHawalaDto
            {
                Number = numberBase + index,
                HawalaType = "HawalaReceive",
                CorrespondentId = fixture.SourceCorrespondent.Id,
                PaymentLocationId = remote ? fixture.RemoteLocation.Id : fixture.OwnLocation.Id,
                FromAccountId = remote ? fixture.DestinationAccount.Id : null,
                SenderName = $"Report Sender {index}",
                ReceiverName = $"Report Receiver {index}",
                FromCurrencyId = 2,
                FromAmount = 1_000m + index,
                ToCurrencyId = 2,
                ToAmount = 1_000m + index,
                ExchangeRate = 1,
                Status = remote ? "Paid" : "Pending",
                GeneratedSendHawalaNumber = numberBase + rowCount + index,
                GeneratedSendAgentCommissionAmount = remote && index % 4 == 1 ? 25m : null,
                GeneratedSendAgentCommissionCurrencyId = remote && index % 4 == 1 ? 2 : null
            });
        }

        return rows;
    }
}
