using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class NavigationReadPerformanceTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Optimized_account_list_does_not_include_another_tenants_records()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var tenant = new Tenant { Name = "Navigation isolation test" };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        var foreign = new Account { AccountCode = "NAV-PRIVATE", AccountName = "Other tenant", AccountType = "Cash" };
        using (context.UseTenantScope(tenant.Id))
        {
            context.Accounts.Add(foreign);
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();

        var rows = await fixture.CreateAccountService(context).GetAllAsync();

        Assert.DoesNotContain(rows, x => x.Id == foreign.Id);
    }

    [Fact]
    public async Task Account_list_returns_owner_names_in_one_untracked_query()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        fixture.Commands.Reset();

        var rows = (await fixture.CreateAccountService(context).GetAllAsync()).ToList();

        var account = Assert.Single(rows, x => x.Id == fixture.SourceAccount.Id);
        Assert.Equal(fixture.SourceCorrespondent.Name, account.ReferenceName);
        Assert.Equal("Correspondent", account.ReferenceType);
        Assert.Equal(fixture.SourceCorrespondent.Id, account.ReferenceId);
        Assert.Equal(1, fixture.Commands.Count);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Reopening_account_list_reads_database_without_overwriting_unsaved_edits()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var account = await context.Accounts.SingleAsync(x => x.Id == fixture.SourceAccount.Id);
        var originalName = account.AccountName;
        account.AccountName = "Unsaved form value";

        var rows = (await fixture.CreateAccountService(context).GetAllAsync()).ToList();

        Assert.Equal(originalName, rows.Single(x => x.Id == account.Id).AccountName);
        Assert.Equal("Unsaved form value", account.AccountName);
        Assert.Equal(EntityState.Modified, context.Entry(account).State);
    }
}
