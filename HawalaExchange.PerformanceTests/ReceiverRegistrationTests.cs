using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaSystem.Mappings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class ReceiverRegistrationTests(SqlServerPerformanceFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Payment_registers_a_customer_without_an_account_only_when_opted_in(bool save)
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (hawala, cash) = await Seed(context);
        var name = $"Receiver-{Guid.NewGuid():N}";
        var accounts = await context.Accounts.CountAsync();
        var payment = new PayHawalaDto { PaidFromAccountId = cash.Id, ReceiverName = name, SaveReceiver = save,
            ReceiverPhone = "0790123456", ReceiverTazkiraNumber = Guid.NewGuid().ToString("N"), ReceiverAddress = "Kabul" };
        await fixture.CreateService(context).MarkAsPaidAsync(hawala.Id, payment);
        var customers = await context.Customers.Where(x => x.FullName == name).ToListAsync();
        Assert.Equal(save ? 1 : 0, customers.Count);
        Assert.Equal(accounts, await context.Accounts.CountAsync());
        if (save) { Assert.Equal(payment.ReceiverTazkiraNumber, customers[0].TazkiraNumber); Assert.Equal("Kabul", customers[0].Address); }
        Assert.False(new PayHawalaDto().SaveReceiver);
    }

    [Fact]
    public async Task Matching_identity_is_not_duplicated_or_overwritten()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (hawala, cash) = await Seed(context);
        var key = Guid.NewGuid().ToString("N");
        context.Customers.Add(new Customer { FullName = "Existing", CustomerCode = key, TazkiraNumber = key, Address = "Preserved" });
        await context.SaveChangesAsync();
        await fixture.CreateService(context).MarkAsPaidAsync(hawala.Id, new PayHawalaDto {
            PaidFromAccountId = cash.Id, ReceiverName = "Different spelling", ReceiverTazkiraNumber = key, SaveReceiver = true });
        var matches = await context.Customers.Where(x => x.TazkiraNumber == key).ToListAsync();
        Assert.Single(matches); Assert.Equal("Preserved", matches[0].Address); Assert.Equal("Existing", matches[0].FullName);
    }

    [Fact]
    public async Task Failed_payment_does_not_register_a_receiver()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (hawala, cash) = await Seed(context);
        hawala.Status = "Cancel"; await context.SaveChangesAsync();
        var name = $"Failed-{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService(context).MarkAsPaidAsync(hawala.Id,
            new PayHawalaDto { PaidFromAccountId = cash.Id, ReceiverName = name, SaveReceiver = true }));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Customers.AnyAsync(x => x.FullName == name));
        Assert.Equal("Cancel", (await verify.Hawalas.SingleAsync(x => x.Id == hawala.Id)).Status);
    }

    [Fact]
    public async Task Failure_after_saving_rolls_back_both_payment_and_receiver()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var (hawala, cash) = await Seed(context);
        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogAsync("UPDATE", "Hawalas", hawala.Id, "Pending", "Paid", fixture.UserId))
            .ThrowsAsync(new InvalidOperationException("Simulated audit failure"));
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var service = new HawalaService(context, mapper, Mock.Of<ILedgerService>(), Mock.Of<IAccountService>(), audit.Object, Mock.Of<IFileService>());
        var name = $"Rollback-{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkAsPaidAsync(hawala.Id,
            new PayHawalaDto { PaidFromAccountId = cash.Id, ReceiverName = name, SaveReceiver = true }));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Customers.AnyAsync(x => x.FullName == name));
        Assert.Equal("Pending", (await verify.Hawalas.SingleAsync(x => x.Id == hawala.Id)).Status);
        Assert.False(await verify.LedgerEntries.AnyAsync(x => x.HawalaId == hawala.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repeated_receiver_without_identity_number_is_not_duplicated(bool hasPhone)
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var name = $"Fallback-{Guid.NewGuid():N}";
        var phone = hasPhone ? Guid.NewGuid().ToString("N") : null;
        context.Customers.Add(new Customer { FullName = name, FatherName = "Father", PhoneNumber = phone, CustomerCode = Guid.NewGuid().ToString("N") });
        await context.SaveChangesAsync();
        var (hawala, cash) = await Seed(context);
        await fixture.CreateService(context).MarkAsPaidAsync(hawala.Id, new PayHawalaDto {
            PaidFromAccountId = cash.Id, ReceiverName = name, ReceiverFatherName = "Father", ReceiverPhone = phone, SaveReceiver = true });
        Assert.Equal(1, await context.Customers.CountAsync(x => x.FullName == name));
    }

    private async Task<(Hawala, Account)> Seed(HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
    {
        var cash = new Account { AccountCode = $"RCV-{Guid.NewGuid():N}", AccountName = "Receiver test cash", AccountType = "Cash" };
        context.Accounts.Add(cash); await context.SaveChangesAsync();
        var hawala = new Hawala { Number = Random.Shared.NextInt64(180_000_000, 190_000_000), HawalaType = "HawalaReceive", Status = "Pending",
            CorrespondentId = fixture.SourceCorrespondent.Id, PaymentLocationId = fixture.OwnLocation.Id,
            SenderName = "Sender", ReceiverName = "Receiver", FromCurrencyId = 2, ToCurrencyId = 2,
            FromAmount = 100, ToAmount = 100, CreatedBy = fixture.UserId, CreatedAt = DateTime.UtcNow };
        context.Hawalas.Add(hawala); await context.SaveChangesAsync();
        return (hawala, cash);
    }
}
