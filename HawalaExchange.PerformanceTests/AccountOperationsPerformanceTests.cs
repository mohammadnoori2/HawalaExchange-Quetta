using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class AccountOperationsPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Procedure_pages_filters_and_orders_account_operations()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var account = await SeedTransfersAsync(context, 55);
        context.ChangeTracker.Clear();
        var service = fixture.CreateJournalService(context);

        var firstPage = await service.GetAccountOperationsPageAsync(new AccountOperationsFilterDto
        {
            AccountId = account.Id,
            PageNumber = 1,
            PageSize = 20
        });
        var secondPage = await service.GetAccountOperationsPageAsync(new AccountOperationsFilterDto
        {
            AccountId = account.Id,
            PageNumber = 2,
            PageSize = 20
        });
        var lastPage = await service.GetAccountOperationsPageAsync(new AccountOperationsFilterDto
        {
            AccountId = account.Id,
            PageNumber = 3,
            PageSize = 20
        });

        Assert.Equal(55, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(20, firstPage.Items.Count);
        Assert.Equal(20, secondPage.Items.Count);
        Assert.Equal(15, lastPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(x => x.OperationKey)
            .Intersect(secondPage.Items.Select(x => x.OperationKey)));
        Assert.Equal("REF-0054", firstPage.Items[0].DocumentNumber);
        Assert.Equal("REF-0035", firstPage.Items[^1].DocumentNumber);
        Assert.Equal(["USD"], firstPage.AvailableCurrencyCodes);
        Assert.DoesNotContain("نامشخص", firstPage.Items[0].SummarySentence);

        var searchResult = await service.GetAccountOperationsPageAsync(new AccountOperationsFilterDto
        {
            AccountId = account.Id,
            SearchTerm = "REF-0017",
            PageNumber = 1,
            PageSize = 20
        });
        Assert.Equal(1, searchResult.TotalCount);
        Assert.Equal("REF-0017", Assert.Single(searchResult.Items).DocumentNumber);

        var typeResult = await service.GetAccountOperationsPageAsync(new AccountOperationsFilterDto
        {
            AccountId = account.Id,
            SourceType = "انتقال",
            CurrencyCode = "USD",
            PageNumber = 1,
            PageSize = 20
        });
        Assert.Equal(55, typeResult.TotalCount);
        Assert.All(typeResult.Items, item => Assert.Equal("انتقال", item.SourceType));
    }

    [Fact]
    public async Task Details_are_loaded_only_for_a_visible_operation_and_include_both_sides()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var account = await SeedTransfersAsync(context, 2);
        context.ChangeTracker.Clear();
        var service = fixture.CreateJournalService(context);
        var page = await service.GetAccountOperationsPageAsync(new AccountOperationsFilterDto
        {
            AccountId = account.Id,
            PageSize = 20
        });
        var summary = page.Items[0];

        Assert.Empty(summary.LedgerEntries);
        Assert.Empty(summary.SourceDetails);
        var details = await service.GetAccountOperationDetailsAsync(account.Id, summary.OperationKey);

        Assert.NotNull(details);
        Assert.Equal(2, details.LedgerEntries.Count);
        Assert.NotEmpty(details.SourceDetails);
        Assert.Contains(details.AccountNames, x => x == account.AccountName);
        Assert.Contains(details.AccountNames, x => x == fixture.DestinationAccount.AccountName);
        Assert.Null(await service.GetAccountOperationDetailsAsync(
            fixture.SourceAccount.Id,
            summary.OperationKey));
    }

    [Fact]
    public async Task Procedure_rejects_an_account_from_another_tenant()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var account = await SeedTransfersAsync(context, 1);
        var otherTenant = new Tenant
        {
            Name = $"Operations tenant {Guid.NewGuid():N}",
            IsActive = true
        };
        context.Tenants.Add(otherTenant);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        using (context.UseTenantScope(otherTenant.Id))
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                fixture.CreateJournalService(context).GetAccountOperationsPageAsync(
                    new AccountOperationsFilterDto
                    {
                        AccountId = account.Id,
                        PageSize = 20
                    }));
        }
    }

    [Fact]
    public async Task Cancellation_is_honored_before_the_database_read()
    {
        await using var context = fixture.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.CreateJournalService(context).GetAccountOperationsPageAsync(
                new AccountOperationsFilterDto
                {
                    AccountId = fixture.SourceAccount.Id,
                    PageSize = 20
                },
                cancellation.Token));
    }

    [Fact]
    public async Task Measure_bounded_page_against_loading_the_complete_account_history()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Account-operation performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var account = await SeedTransfersAsync(context, 1_000);
        context.ChangeTracker.Clear();
        var service = fixture.CreateJournalService(context);

        var legacyWatch = Stopwatch.StartNew();
        var legacy = await service.GetAccountOperationsAsync(account.Id);
        legacyWatch.Stop();

        var pageWatch = Stopwatch.StartNew();
        var page = await service.GetAccountOperationsPageAsync(new AccountOperationsFilterDto
        {
            AccountId = account.Id,
            PageNumber = 1,
            PageSize = 20
        });
        pageWatch.Stop();

        Assert.Equal(1_000, legacy.Count);
        Assert.Equal(1_000, page.TotalCount);
        Assert.Equal(20, page.Items.Count);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            operations = 1_000,
            legacyReturnedRows = legacy.Count,
            legacyMilliseconds = Math.Round(legacyWatch.Elapsed.TotalMilliseconds, 2),
            pagedReturnedRows = page.Items.Count,
            pagedMilliseconds = Math.Round(pageWatch.Elapsed.TotalMilliseconds, 2)
        }));
    }

    private async Task<Account> SeedTransfersAsync(
        ApplicationDbContext context,
        int count)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var account = new Account
        {
            AccountCode = $"OPS-{suffix}",
            AccountName = $"Operations account {suffix}",
            AccountType = "Cash"
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var createdAt = DateTime.UtcNow.AddDays(-1);
        var transfers = Enumerable.Range(0, count).Select(index => new Transfer
        {
            FromAccountId = account.Id,
            ToAccountId = fixture.DestinationAccount.Id,
            CurrencyId = 2,
            Amount = 1_000 + index,
            TransferMethod = "Hawala",
            ReferenceNumber = $"REF-{index:0000}",
            Remarks = $"Paged operation {index}"
        }).ToArray();
        context.Transfers.AddRange(transfers);
        await context.SaveChangesAsync();

        context.LedgerEntries.AddRange(transfers.SelectMany((transfer, index) => new[]
        {
            new LedgerEntry
            {
                TransferId = transfer.Id,
                AccountId = account.Id,
                CurrencyId = 2,
                BadehKar = transfer.Amount,
                Description = transfer.Remarks,
                CreatedAt = createdAt.AddMinutes(index)
            },
            new LedgerEntry
            {
                TransferId = transfer.Id,
                AccountId = fixture.DestinationAccount.Id,
                CurrencyId = 2,
                TalabKar = transfer.Amount,
                Description = transfer.Remarks,
                CreatedAt = createdAt.AddMinutes(index)
            }
        }));
        await context.SaveChangesAsync();
        return account;
    }
}
