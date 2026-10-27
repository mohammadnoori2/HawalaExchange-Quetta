using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class PaymentLocationAssignmentTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Hawala_location_resolution_prefers_assigned_owner_over_same_name_correspondent()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var location = new PaymentLocation { Name = fixture.SourceCorrespondent.Name,
            NormalizedName = PaymentLocationNameNormalizer.Normalize(fixture.SourceCorrespondent.Name), CreatedBy = fixture.UserId };
        context.PaymentLocations.Add(location); await context.SaveChangesAsync();
        context.PaymentLocationCorrespondentAssignments.Add(new() { PaymentLocationId = location.Id,
            CorrespondentId = fixture.DestinationCorrespondent.Id, EffectiveFrom = DateTime.Today, CreatedBy = fixture.UserId });
        await context.SaveChangesAsync();
        Assert.Equal(fixture.DestinationAccount.Id, await Resolve(context, location.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assignment_history_without_current_owner_does_not_fall_back_to_a_matching_name(bool future)
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var name = "Unassigned " + Guid.NewGuid().ToString("N");
        var decoy = new Correspondent { Code = name, Name = name, CommissionMethod = "PerTransaction" };
        var location = new PaymentLocation { Name = name,
            NormalizedName = PaymentLocationNameNormalizer.Normalize(name), CreatedBy = fixture.UserId };
        context.AddRange(decoy, location); await context.SaveChangesAsync();
        context.Accounts.Add(new Account { AccountCode = name, AccountName = name, AccountType = "Correspondent", CorrespondentId = decoy.Id });
        context.PaymentLocationCorrespondentAssignments.Add(new() { PaymentLocationId = location.Id,
            CorrespondentId = fixture.SourceCorrespondent.Id, EffectiveFrom = DateTime.Today.AddDays(future ? 1 : -2),
            EffectiveTo = future ? null : DateTime.Today, CreatedBy = fixture.UserId });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Resolve(context, location.Id));
    }

    [Fact]
    public async Task Legacy_location_without_assignment_history_keeps_name_based_resolution()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var name = "Legacy " + Guid.NewGuid().ToString("N");
        var correspondent = new Correspondent { Code = name, Name = name, CommissionMethod = "PerTransaction" };
        var location = new PaymentLocation { Name = name, NormalizedName = PaymentLocationNameNormalizer.Normalize(name), CreatedBy = fixture.UserId };
        context.AddRange(correspondent, location); await context.SaveChangesAsync();
        var account = new Account { AccountCode = name, AccountName = name, AccountType = "Correspondent", CorrespondentId = correspondent.Id };
        context.Accounts.Add(account); await context.SaveChangesAsync();
        Assert.Equal(account.Id, await Resolve(context, location.Id));
    }

    [Fact]
    public async Task Form_lookups_use_a_separate_connection_when_scoped_context_has_an_active_reader()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var key = Guid.NewGuid().ToString("N");
        var location = new PaymentLocation { Name = "Reader " + key, NormalizedName = "reader-" + key, CreatedBy = fixture.UserId };
        context.PaymentLocations.Add(location); await context.SaveChangesAsync();
        context.PaymentLocationCorrespondentAssignments.Add(new() { PaymentLocationId = location.Id,
            CorrespondentId = fixture.DestinationCorrespondent.Id, EffectiveFrom = DateTime.Today, CreatedBy = fixture.UserId });
        await context.SaveChangesAsync();
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => fixture.CreateContext());
        var service = fixture.CreateService(context, factory.Object);
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT 1 AS ActiveReader";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.Equal(fixture.DestinationAccount.Id, await service.ResolvePaymentLocationAccountAsync(location.Id));
        Assert.True(await service.GetNextNumberAsync(fixture.DestinationCorrespondent.Id, "HawalaSend") > 0);
        factory.Verify(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private Task<long> Resolve(ApplicationDbContext context, long id) =>
        fixture.CreateService(context).ResolvePaymentLocationAccountAsync(id);

    [Fact]
    public async Task Creating_location_with_correspondent_saves_both_records()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var mapper = new MapperConfiguration(
            config => config.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();
        var service = new PaymentLocationService(
            context,
            Mock.Of<IDbContextFactory<ApplicationDbContext>>(),
            mapper,
            Mock.Of<IAuditLogService>());
        var location = await service.CreateAsync(new CreatePaymentLocationDto
        {
            Name = $"Assigned {Guid.NewGuid():N}",
            ResponsibleCorrespondentId = fixture.DestinationCorrespondent.Id
        });

        var assignment = await context.PaymentLocationCorrespondentAssignments.SingleAsync(
            x => x.PaymentLocationId == location.Id);
        Assert.Equal(fixture.DestinationCorrespondent.Id, assignment.CorrespondentId);
        Assert.Equal(DateTime.Today, assignment.EffectiveFrom);
    }

    [Fact]
    public async Task Reassignment_keeps_date_bounded_history_and_rejects_backdating()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var name = $"Assignment {Guid.NewGuid():N}";
        var location = new PaymentLocation
        {
            Name = name,
            NormalizedName = PaymentLocationNameNormalizer.Normalize(name),
            Address = "Test",
            CreatedBy = fixture.UserId
        };
        context.PaymentLocations.Add(location);
        await context.SaveChangesAsync();

        var service = new PaymentLocationAssignmentService(context);
        var today = DateTime.Today;
        await service.AssignAsync(location.Id, fixture.SourceCorrespondent.Id, today);
        await service.AssignAsync(location.Id, fixture.DestinationCorrespondent.Id, today.AddDays(1));

        var history = (await service.GetAllAsync())
            .Where(x => x.PaymentLocationId == location.Id)
            .OrderBy(x => x.EffectiveFrom).ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(fixture.SourceCorrespondent.Id, history[0].CorrespondentId);
        Assert.Equal(today.AddDays(1), history[0].EffectiveTo);
        Assert.Equal(fixture.DestinationCorrespondent.Id, history[1].CorrespondentId);
        Assert.Null(history[1].EffectiveTo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignAsync(location.Id, fixture.SourceCorrespondent.Id, today.AddDays(-1)));
        Assert.Equal(2, await context.PaymentLocationCorrespondentAssignments
            .CountAsync(x => x.PaymentLocationId == location.Id));
    }
}
