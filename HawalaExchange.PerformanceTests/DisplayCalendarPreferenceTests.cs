using System.Globalization;
using HawalaExchange.Application.Helpers;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Application.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Services;
using Xunit;

namespace HawalaExchange.PerformanceTests;

public class DisplayCalendarPreferenceTests
{
    [Fact]
    public async Task UpgradeDefaultsOldCompaniesToPersianAndSavingGregorianPersistsFalse()
    {
        var database = new SqlServerPerformanceFixture();
        try
        {
            await database.InitializeDatabaseAsync("20261002160000_DeferCorrespondentCommissionsUntilPeriodClose");
            await using var context = database.CreateContext();
            using var bypass = context.BypassSubscriptionEnforcement();
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.CompanySettings (TenantId, CompanyName, OwnPaymentLocationId, DefaultProfitCurrencyId, CreatedAt) VALUES (1, N'Calendar upgrade test', {database.OwnLocation.Id}, 2, {DateTime.UtcNow})");
            var entriesBefore = await context.LedgerEntries.CountAsync();
            await context.Database.MigrateAsync();
            var service = new CompanySettingService(context);
            var settings = await service.GetAsync();
            Assert.True(settings.UsePersianCalendar);
            settings.UsePersianCalendar = false;
            Assert.False((await service.SaveAsync(settings)).UsePersianCalendar);
            context.ChangeTracker.Clear();
            Assert.False((await service.GetAsync()).UsePersianCalendar);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(context.Database.GetConnectionString()).Options;
            var currentTenant = new TestCurrentTenant();
            var display = new AppDateDisplay(options, currentTenant);
            await display.EnsureLoadedAsync();
            Assert.False(display.UsePersianCalendar);
            var initialization = display.EnsureLoadedAsync();
            Assert.Same(initialization, display.EnsureLoadedAsync());
            var otherTenant = new AppDateDisplay(options, new TestCurrentTenant { TenantId = 2 });
            await otherTenant.EnsureLoadedAsync();
            Assert.True(otherTenant.UsePersianCalendar);
            settings.UsePersianCalendar = true;
            await service.SaveAsync(settings);
            context.ChangeTracker.Clear();
            Assert.True((await service.GetAsync()).UsePersianCalendar);
            display.SetCalendar(true);
            await display.EnsureLoadedAsync();
            Assert.True(display.UsePersianCalendar);
            var reconnected = new AppDateDisplay(options, currentTenant);
            await reconnected.EnsureLoadedAsync();
            Assert.True(reconnected.UsePersianCalendar);
            Assert.Equal(entriesBefore, await context.LedgerEntries.CountAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public void ExistingAndNewCompaniesDefaultToPersian()
    {
        Assert.True(new CompanySetting().UsePersianCalendar);
        Assert.True(new CompanySettingDto().UsePersianCalendar);
        Assert.True(new DisplayDateFormatter().UsePersianCalendar);
    }

    [Fact]
    public void PreferenceChangesOnlyDisplayNotTheUnderlyingDate()
    {
        var date = new DateTime(2026, 10, 2, 15, 45, 30);
        var formatter = new DisplayDateFormatter();
        Assert.Equal("1405/07/10", formatter.Date(date));
        Assert.Equal("1405/07/10 03:45 PM", formatter.DateTime(date));
        formatter.SetCalendar(false);
        Assert.Equal("2026/10/02", formatter.Date(date));
        Assert.Equal("2026/10/02 15:45:30", formatter.DateTime(date, true, true));
        formatter.SetCalendar(true);
        Assert.Equal("1405/07/10", formatter.Date(DateOnly.FromDateTime(date)));
        Assert.Equal(2026, date.Year);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingDatesAndCustomFallbacksArePreserved(bool persian)
    {
        var formatter = new DisplayDateFormatter();
        formatter.SetCalendar(persian);
        Assert.Equal("—", formatter.Date((DateTime?)null));
        Assert.Equal("ادامه دارد", formatter.Date(null, "ادامه دارد"));
        Assert.Equal("—", formatter.DateTime(DateTime.MinValue));
    }

    [Fact]
    public void TenantFormattersAreIndependentAndAlwaysUseEnglishDigits()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
            var persian = new DisplayDateFormatter();
            var gregorian = new DisplayDateFormatter();
            gregorian.SetCalendar(false);
            var date = new DateTime(2026, 3, 21);
            Assert.Equal("1405/01/01", persian.Date(date));
            Assert.Equal("2026/03/21", gregorian.Date(date));
            Assert.Equal("1405/01", persian.Month(date));
            Assert.Equal("2026/03", gregorian.Month(date));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
