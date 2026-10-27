using System.Net;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace HawalaExchange.NavigationTests;

public sealed class HawalaExecutionReceiptTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Receipt_contains_receiver_identity_from_selected_customer_or_saved_payment(bool draft)
    {
        var payment = new PayHawalaDto { ReceiverName = "گیرنده", ReceiverFatherName = "پدر", ReceiverPhone = "0700123456",
            ReceiverTazkiraNumber = "ID-123<test>", ReceiverAddress = "کابل <نشانی>" };
        var hawala = Paid(1);
        if (!draft)
        {
            hawala.ReceiverName = payment.ReceiverName; hawala.ReceiverFatherName = payment.ReceiverFatherName;
            hawala.ReceiverPhone = payment.ReceiverPhone; hawala.ReceiverTazkiraNumber = payment.ReceiverTazkiraNumber;
            hawala.ReceiverAddress = payment.ReceiverAddress;
        }
        using var setup = new Setup();
        await using var renderer = new HtmlRenderer(setup.Services, setup.Services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<HawalaExecutionReceiptPrint>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Hawala"] = hawala, ["Payment"] = draft ? payment : new PayHawalaDto() }))).ToHtmlString());
        Assert.Contains("ID-123&lt;test&gt;", html);
        Assert.Contains("0700123456", html);
        Assert.Contains("کابل <نشانی>", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("ID-123<test>", html);
    }

    [Fact]
    public async Task Successful_execution_prints_once_after_render_and_next_execution_prints_again()
    {
        using var setup = new Setup();
        await using var renderer = setup.Renderer();
        var hawala = Paid(1);
        await renderer.Show(hawala, true);
        await renderer.Show(hawala, true);
        setup.JS.Verify(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("hawalaTools.printElement",
            It.Is<object?[]?>(a => a != null && a.Length == 3 && Equals(a[1], 80) && Equals(a[2], true))), Times.Once);
        await renderer.Show(Paid(2), true);
        Assert.Equal(2, setup.JS.Invocations.Count(x => x.Method.Name == "InvokeAsync"));
    }

    [Theory]
    [InlineData(false, "Paid")]
    [InlineData(true, "Pending")]
    public async Task Drafts_and_manual_receipts_never_print_automatically(bool auto, string status)
    {
        using var setup = new Setup();
        await using var renderer = setup.Renderer();
        var hawala = Paid(1); hawala.Status = status;
        await renderer.Show(hawala, auto);
        Assert.Empty(setup.JS.Invocations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Print_failures_do_not_turn_a_successful_payment_into_an_exception(bool settingsFailure)
    {
        using var setup = new Setup();
        if (settingsFailure) setup.Company.Setup(x => x.GetAsync()).ThrowsAsync(new InvalidOperationException("Unavailable"));
        else setup.JS.Setup(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(It.IsAny<string>(), It.IsAny<object?[]?>()))
            .ThrowsAsync(new JSException("Print unavailable"));
        await using var renderer = setup.Renderer();
        await renderer.Show(Paid(1), true);
        await renderer.Show(Paid(1), true);
        Assert.True(setup.Company.Invocations.Count <= 1);
    }

    private static HawalaDto Paid(long id) => new() { Id = id, Number = id, Status = "Paid", PaidAt = DateTime.UtcNow,
        ReceiverName = "گیرنده", FromAmount = 100, FromCurrencyCode = "AFN" };

    private sealed class Setup : IDisposable
    {
        public Mock<IJSRuntime> JS { get; } = new();
        public Mock<ICompanySettingService> Company { get; } = new();
        public ServiceProvider Services { get; }
        public Setup()
        {
            Company.Setup(x => x.GetAsync()).ReturnsAsync(new CompanySettingDto { CompanyName = "صرافی آزمایشی" });
            Services = new ServiceCollection().AddLogging().AddSingleton(JS.Object).AddSingleton(Company.Object)
                .AddSingleton(new AppDateDisplay(new DbContextOptionsBuilder<ApplicationDbContext>().Options, Mock.Of<ICurrentTenant>()))
                .BuildServiceProvider();
        }
        public ReceiptRenderer Renderer() => new(Services, Services.GetRequiredService<ILoggerFactory>());
        public void Dispose() => Services.Dispose();
    }

    private sealed class ReceiptRenderer(IServiceProvider services, ILoggerFactory logging) : Renderer(services, logging)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        private int? root;
        public Task Show(HawalaDto hawala, bool auto) => Dispatcher.InvokeAsync(async () =>
        {
            root ??= AssignRootComponentId(InstantiateComponent(typeof(HawalaExecutionReceiptPrint)));
            await RenderRootComponentAsync(root.Value, ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["Hawala"] = hawala, ["AutoPrint"] = auto }));
        });
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
}
