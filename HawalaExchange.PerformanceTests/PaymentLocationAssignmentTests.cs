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
