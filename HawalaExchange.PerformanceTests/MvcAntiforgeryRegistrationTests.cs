using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;

namespace HawalaExchange.PerformanceTests;

public sealed class MvcAntiforgeryRegistrationTests
{
    [Fact]
    public void Controller_only_registration_reproduces_the_reported_missing_filter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(() => new ValidateAntiForgeryTokenAttribute().CreateInstance(provider));
        Assert.Contains("ValidateAntiforgeryTokenAuthorizationFilter", error.Message);
    }

    [Fact]
    public void Application_registers_Mvc_ViewFeatures_required_by_the_antiforgery_attribute()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "HawalaExchange.Web", "Program.cs"));
        Assert.Contains("builder.Services.AddControllersWithViews();", source);
        Assert.DoesNotContain("builder.Services.AddControllers();", source);
        Assert.Contains("app.UseAntiforgery();", source);
        var controller = File.ReadAllText(Path.Combine(SourceRoot(), "HawalaExchange.Web", "Controllers", "ImpersonationController.cs"));
        Assert.Contains("ValidateAntiForgeryToken", controller);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.BadRequest)]
    [InlineData("invalid", HttpStatusCode.BadRequest)]
    [InlineData("valid", HttpStatusCode.OK)]
    public async Task Mvc_attribute_rejects_bad_tokens_and_accepts_a_valid_cookie_token_pair(string tokenKind, HttpStatusCode expected)
    {
        // A real MVC pipeline on loopback, with no application startup, SQL,
        // migrations, authentication sessions, SMTP or background workers.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AntiforgeryProbeController).Assembly);
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAntiforgery();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { UseCookies = true })
            { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            if (tokenKind != "missing")
            {
                var tokens = await client.GetFromJsonAsync<ProbeToken>("/__security/antiforgery/token");
                Assert.False(string.IsNullOrEmpty(tokens?.RequestToken));
                client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", tokenKind == "valid" ? tokens!.RequestToken : "invalid-canary-token");
            }
            using var response = await client.PostAsync("/__security/antiforgery/protected", new StringContent(""));
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.OK) Assert.Equal("accepted", await response.Content.ReadAsStringAsync());
        }
        finally { await app.StopAsync(); }
    }

    public sealed record ProbeToken(string? RequestToken);
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
}

// Only compiled into the test assembly; never exposed by the production app.
[Route("__security/antiforgery")]
public sealed class AntiforgeryProbeController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("token")]
    public IActionResult Token() => new JsonResult(new MvcAntiforgeryRegistrationTests.ProbeToken(
        antiforgery.GetAndStoreTokens(HttpContext).RequestToken));

    [HttpPost("protected"), ValidateAntiForgeryToken]
    public IActionResult Protected() => Content("accepted");
}
