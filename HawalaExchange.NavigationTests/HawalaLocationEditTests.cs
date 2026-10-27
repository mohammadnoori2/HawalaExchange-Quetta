using System.Reflection;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Shared.Modals;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public sealed class HawalaLocationEditTests
{
    [Fact]
    public async Task Edit_uses_server_assigned_account_even_when_names_do_not_match()
    {
        await Run(async (setup, modal) => {
            setup.Hawalas.Setup(x => x.ResolvePaymentLocationAccountAsync(11)).ReturnsAsync(12);
            await Change(modal, 11);
            Assert.Equal(12, Model(modal).FromAccountId);
            Assert.Equal(3, Model(modal).GeneratedSendHawalaNumber);
            Assert.Empty(Field<List<string>>(modal, "ValidationErrors"));
        });
    }

    [Fact]
    public async Task Missing_account_clears_old_payment_account_and_shows_validation()
    {
        await Run(async (setup, modal) => {
            setup.Hawalas.Setup(x => x.ResolvePaymentLocationAccountAsync(11)).ThrowsAsync(new InvalidOperationException("مسئول معتبر پیدا نشد"));
            Model(modal).FromAccountId = 99;
            await Change(modal, 11);
            Assert.Null(Model(modal).FromAccountId);
            Assert.Contains(Field<List<string>>(modal, "ValidationErrors"), x => x.Contains("مسئول معتبر پیدا نشد"));
        });
    }

    [Fact]
    public async Task Late_resolution_cannot_replace_the_latest_location_or_account()
    {
        await Run(async (setup, modal) => {
            var pending = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            setup.Hawalas.Setup(x => x.ResolvePaymentLocationAccountAsync(11)).Returns(pending.Task);
            setup.Hawalas.Setup(x => x.ResolvePaymentLocationAccountAsync(22)).ReturnsAsync(24);
            var first = Change(modal, 11);
            var second = Change(modal, 22);
            pending.TrySetResult(12);
            await Task.WhenAll(first, second);
            Assert.Equal(22, Model(modal).PaymentLocationId);
            Assert.Equal(24, Model(modal).FromAccountId);
        });
    }

    [Fact]
    public async Task Pending_hawala_is_not_executed_by_changing_its_location()
    {
        await Run(async (setup, modal) => {
            Model(modal).Status = "Pending";
            await Change(modal, 11);
            Assert.Equal("Pending", Model(modal).Status);
            setup.Hawalas.Verify(x => x.ResolvePaymentLocationAccountAsync(It.IsAny<long?>()), Times.Never);
        });
    }

    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static CreateHawalaDto Model(Probe modal) => Field<CreateHawalaDto>(modal, "CurrentHawala");
    private static T Field<T>(Probe modal, string name) => (T)typeof(CreateHawalaModal).GetProperty(name, Hidden)!.GetValue(modal)!;
    private static Task Change(Probe modal, long id) => (Task)typeof(CreateHawalaModal).GetMethod("OnPaymentLocationChanged", Hidden)!
        .Invoke(modal, [new ChangeEventArgs { Value = id.ToString() }])!;

    private static async Task Run(Func<Setup, Probe, Task> test)
    {
        using var setup = new Setup();
        await using var renderer = new HtmlRenderer(setup.Services, setup.Services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => {
            Probe? modal = null;
            await renderer.RenderComponentAsync<Probe>(ParameterView.FromDictionary(new Dictionary<string, object?> {
                ["Capture"] = (Action<Probe>)(x => modal = x), ["IsEditMode"] = true
            }));
            Model(modal!).HawalaType = "HawalaReceive"; Model(modal!).Status = "Paid";
            typeof(CreateHawalaModal).GetProperty("AccountsList", Hidden)!.SetValue(modal, new List<AccountDto> {
                new() { Id = 12, AccountName = "مسئول با نام متفاوت", AccountType = "Correspondent", ReferenceType = "Correspondent", ReferenceId = 2 },
                new() { Id = 24, AccountName = "مسئول دوم", AccountType = "Correspondent", ReferenceType = "Correspondent", ReferenceId = 4 }
            });
            await test(setup, modal!);
        });
    }
    public sealed class Probe : CreateHawalaModal
    {
        [Parameter] public Action<Probe> Capture { get; set; } = default!;
        protected override void OnInitialized() => Capture(this);
        protected override Task OnInitializedAsync() => Task.CompletedTask;
        protected override Task OnParametersSetAsync() => Task.CompletedTask;
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }
    private sealed class Setup : IDisposable
    {
        public Mock<IHawalaService> Hawalas { get; } = new();
        public ServiceProvider Services { get; }
        public Setup()
        {
            var collection = new ServiceCollection().AddLogging();
            for (var type = typeof(Probe); type is not null; type = type.BaseType)
                foreach (var property in type.GetProperties(Hidden | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(x => x.GetCustomAttribute<InjectAttribute>() != null && x.PropertyType.IsInterface))
                {
                    var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(property.PropertyType))!;
                    collection.AddSingleton(property.PropertyType, mock.Object);
                }
            Hawalas.Setup(x => x.GetNextNumberAsync(It.IsAny<long>(), "HawalaSend")).ReturnsAsync(3);
            Services = collection.AddSingleton(Hawalas.Object).AddSingleton<NavigationManager>(new TestNavigation())
                .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, Mock.Of<ICurrentTenant>()))
                .BuildServiceProvider();
        }
        public void Dispose() => Services.Dispose();
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/hawala-transactions");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
