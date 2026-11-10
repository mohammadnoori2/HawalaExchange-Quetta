using HawalaExchange.Application.DTOs;
using HawalaExchange.Web.Helpers;

namespace HawalaExchange.PerformanceTests;

public class HawalaPaymentAccountSelectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Creating_a_paid_receipt_selects_its_location_owner_in_either_selection_order(bool locationFirst)
    {
        var selection = new HawalaPaymentAccountSelection();
        var model = new CreateHawalaDto { HawalaType = "HawalaReceive", Status = "Pending" };
        var calls = 0;
        Task<AccountDto?> Resolve(long id) { calls++; Assert.Equal(7, id); return Task.FromResult<AccountDto?>(new() { Id = 42 }); }
        if (locationFirst) model.PaymentLocationId = 7; else model.Status = "Paid";
        Assert.Null(await selection.ResolveAsync(model, () => model, Resolve)); Assert.Equal(0, calls);
        model.PaymentLocationId = 7; model.Status = "Paid";
        Assert.Equal(42, (await selection.ResolveAsync(model, () => model, Resolve))!.Id);
        Assert.Equal(42, model.FromAccountId); Assert.False(selection.IsResolving);
    }

    [Fact]
    public async Task A_new_location_wins_over_a_slow_previous_response()
    {
        var selection = new HawalaPaymentAccountSelection();
        var model = new CreateHawalaDto { HawalaType = "HawalaReceive", Status = "Paid", PaymentLocationId = 7 };
        var slow = new TaskCompletionSource<AccountDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = selection.ResolveAsync(model, () => model, _ => slow.Task);
        Assert.True(selection.IsResolving); model.PaymentLocationId = 8;
        var latest = selection.ResolveAsync(model, () => model, _ => Task.FromResult<AccountDto?>(new() { Id = 84 }));
        slow.SetResult(new() { Id = 42 });
        Assert.Null(await first); Assert.Equal(84, (await latest)!.Id); Assert.Equal(84, model.FromAccountId);
        Assert.False(selection.IsResolving);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("send")]
    [InlineData("manual")]
    [InlineData("reopen")]
    public async Task Outdated_resolution_never_overwrites_changed_form_or_manual_account(string change)
    {
        var selection = new HawalaPaymentAccountSelection();
        var model = new CreateHawalaDto { HawalaType = "HawalaReceive", Status = "Paid", PaymentLocationId = 7 };
        var current = model;
        var slow = new TaskCompletionSource<AccountDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = selection.ResolveAsync(model, () => current, _ => slow.Task);
        if (change == "pending") model.Status = "Pending";
        if (change == "send") model.HawalaType = "HawalaSend";
        if (change == "manual") { model.FromAccountId = 99; selection.Invalidate(); }
        if (change == "reopen") current = new();
        slow.SetResult(new() { Id = 42 }); Assert.Null(await pending);
        Assert.Equal(change == "manual" ? (long?)99 : null, model.FromAccountId);
    }

    [Fact]
    public async Task Missing_or_archived_owner_does_not_leave_an_unrelated_old_account_selected()
    {
        var selection = new HawalaPaymentAccountSelection();
        var model = new CreateHawalaDto { HawalaType = "HawalaReceive", Status = "Paid", PaymentLocationId = 7, FromAccountId = 99 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => selection.ResolveAsync(model, () => model,
            _ => Task.FromResult<AccountDto?>(new() { Id = 42, IsArchived = true })));
        Assert.Null(model.FromAccountId); Assert.False(selection.IsResolving);
        model.PaymentLocationId = null;
        Assert.Null(await selection.ResolveAsync(model, () => model, _ => throw new Exception("Must not query without a location")));
    }
}
