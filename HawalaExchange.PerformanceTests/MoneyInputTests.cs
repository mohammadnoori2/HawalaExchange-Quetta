using System.Reflection;
using HawalaExchange.Web.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace HawalaExchange.PerformanceTests;

public sealed class MoneyInputTests
{
    [Fact]
    public async Task Clearing_required_money_updates_the_model_to_zero()
    {
        decimal actual = -1;
        var component = new MoneyInput { Value = 250,
            ValueChanged = EventCallback.Factory.Create<decimal>(new object(), (decimal value) => actual = value) };
        await Change(component, "");
        Assert.Equal(0, actual);
    }

    [Fact]
    public async Task Clearing_optional_money_updates_the_model_to_null()
    {
        decimal? actual = -1;
        var component = new NullableMoneyInput { Value = 250,
            ValueChanged = EventCallback.Factory.Create<decimal?>(new object(), (decimal? value) => actual = value) };
        await Change(component, "");
        Assert.Null(actual);
    }

    [Theory]
    [InlineData("۲۱٬۵۶۰٫۵", 0, 21561)]
    [InlineData("-100.50", 0, -101)]
    [InlineData("65.125", 3, 65.125)]
    public async Task Commit_preserves_existing_server_parsing_and_rounding(string text, int decimals, decimal expected)
    {
        decimal actual = 0;
        var component = new MoneyInput { DecimalPlaces = decimals,
            ValueChanged = EventCallback.Factory.Create<decimal>(new object(), (decimal value) => actual = value) };
        await Change(component, text);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Both_components_commit_on_change_not_per_keystroke()
    {
        var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        foreach (var name in new[] { "MoneyInput", "NullableMoneyInput" })
        {
            var source = File.ReadAllText(Path.Combine(root, $"HawalaExchange.Web/Components/Shared/{name}.razor"));
            Assert.DoesNotContain("@oninput", source);
            Assert.DoesNotContain("@onfocus", source);
            Assert.Contains("@onchange=\"HandleChange\"", source);
            Assert.Contains("data-money-value", source);
        }
    }

    private static Task Change(object component, string text) => (Task)component.GetType()
        .GetMethod("HandleChange", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(component, [new ChangeEventArgs { Value = text }])!;
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
