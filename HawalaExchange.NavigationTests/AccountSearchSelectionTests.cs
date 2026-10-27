using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public sealed class AccountSearchSelectionTests
{
    [Fact]
    public void Clearing_hawala_account_also_clears_the_bound_id_and_generated_number()
    {
        var modal = new HawalaExchange.Web.Components.Shared.Modals.CreateHawalaModal();
        var type = modal.GetType();
        var model = (CreateHawalaDto)type.GetProperty("CurrentHawala", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(modal)!;
        model.FromAccountId = 12; model.GeneratedSendHawalaNumber = 100;
        type.GetMethod("FromAccountChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(modal, [0L]);
        Assert.Null(model.FromAccountId); Assert.Null(model.GeneratedSendHawalaNumber);
    }

    [Fact]
    public async Task Selection_uses_allowed_account_and_ignores_late_or_duplicate_messages()
    {
        await Run(async (renderer, component, changes) => {
            await Send(component, "2", 2); await Send(component, "0", 1); await Send(component, "2", 2);
            Assert.Equal(new long[] { 2 }, changes);
        });
    }

    [Theory]
    [InlineData("99")]
    [InlineData("3")]
    [InlineData("<script>")]
    public async Task Unknown_archived_or_invalid_accounts_are_not_selected(string id)
    {
        await Run(async (renderer, component, changes) => {
            await Send(component, id, 1); Assert.Empty(changes);
        });
    }

    [Fact]
    public async Task Editing_invalidates_the_previous_selection_and_next_choice_is_committed()
    {
        await Run(async (renderer, component, changes) => {
            await Send(component, "0", 1); await Send(component, "2", 2);
            Assert.Equal(new long[] { 0, 2 }, changes);
        });
    }

    [Fact]
    public async Task Disabled_select_does_not_change_the_account()
    {
        await Run(async (renderer, component, changes) => {
            component.Disabled = true;
            await Send(component, "2", 1); Assert.Empty(changes);
        });
    }

    [Fact]
    public async Task Pending_invalidation_finishes_before_the_next_selection_callback()
    {
        await Run(async (renderer, component, changes) => {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            component.ValueChanged = EventCallback.Factory.Create<long>(new object(), async (long id) => {
                if (id == 0) await release.Task;
                changes.Add(id);
            });
            var clear = Send(component, "0", 1);
            var select = Send(component, "2", 2);
            Assert.False(select.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(clear, select);
            Assert.Equal(new long[] { 0, 2 }, changes);
        });
    }

    private static Task Send(AccountSearchSelect component, string id, long revision) =>
        (Task)typeof(AccountSearchSelect).GetMethod("HandleSelectionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(component, [new ChangeEventArgs { Value = System.Text.Json.JsonSerializer.Serialize(new { id, revision }) }])!;

    private static async Task Run(Func<HtmlRenderer, Probe, List<long>, Task> test)
    {
        using var services = new ServiceCollection().AddLogging().AddSingleton(Mock.Of<IJSRuntime>())
            .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, Mock.Of<ICurrentTenant>()))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            Probe? component = null; var changes = new List<long>();
            await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?> {
                ["Capture"] = (Action<Probe>)(x => component = x), ["ShowBalanceSummary"] = false,
                ["Accounts"] = new[] { new AccountDto { Id = 2, AccountName = "کابل", AccountCode = "A2" },
                    new AccountDto { Id = 3, AccountName = "Archived", IsArchived = true } },
                ["ValueChanged"] = EventCallback.Factory.Create<long>(new object(), (long id) => changes.Add(id))
            }));
            await test(renderer, component!, changes);
        });
    }
    public sealed class Probe : AccountSearchSelect
    {
        [Parameter] public Action<Probe> Capture { get; set; } = default!;
        protected override void OnInitialized() => Capture(this);
    }
}
