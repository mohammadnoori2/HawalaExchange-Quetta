using System.Security.Claims;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace HawalaExchange.PerformanceTests;

public sealed class LoginDestinationTests
{
    private const string Origin = "https://localhost:7128/";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/?from=login")]
    [InlineData("https://localhost:7128/")]
    [InlineData("/accounts")]
    [InlineData("/hawala-transactions")]
    [InlineData("/Account/AccessDenied?ReturnUrl=%2F")]
    [InlineData("/Account/Login?ReturnUrl=%2F")]
    [InlineData("/logout")]
    public void Platform_login_goes_to_platform_not_the_tenant_dashboard(string? returnUrl) =>
        Assert.Equal("/platform", LoginDestination.Resolve(true, returnUrl, Origin));

    [Theory]
    [InlineData("/platform/audit?search=hello#top", "/platform/audit?search=hello#top")]
    [InlineData("https://localhost:7128/tenants/12", "/tenants/12")]
    [InlineData("platform/reports", "/platform/reports")]
    [InlineData("/Account/Manage/ChangePassword", "/Account/Manage/ChangePassword")]
    [InlineData("/profile", "/profile")]
    public void Platform_safe_deep_links_are_kept(string input, string expected) =>
        Assert.Equal(expected, LoginDestination.Resolve(true, input, Origin));

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/accounts?Search=abc", "/accounts?Search=abc")]
    [InlineData("https://localhost:7128/customers", "/customers")]
    [InlineData("/platform", "/")]
    [InlineData("/tenants/42", "/")]
    [InlineData("/Account/AccessDenied", "/")]
    [InlineData("/login", "/")]
    public void Tenant_login_keeps_tenant_destinations_only(string input, string expected) =>
        Assert.Equal(expected, LoginDestination.Resolve(false, input, Origin));

    [Theory]
    [InlineData("https://evil.example/platform")]
    [InlineData("https://localhost:9999/platform")]
    [InlineData("http://localhost:7128/platform")]
    [InlineData("//evil.example/platform")]
    [InlineData("/\\evil.example/platform")]
    [InlineData("/%2f%2fevil.example")]
    [InlineData("/%252f%252fevil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/platform\r\nLocation:https://evil.example")]
    [InlineData("https://user@localhost:7128/platform")]
    public void Unsafe_redirects_fall_back_for_both_account_types(string input)
    {
        Assert.Equal("/platform", LoginDestination.Resolve(true, input, Origin));
        Assert.Equal("/", LoginDestination.Resolve(false, input, Origin));
    }

    [Fact]
    public void Hosted_subpath_is_preserved_and_cannot_be_escaped()
    {
        const string origin = "https://host.example/exchange/";
        Assert.Equal("/exchange/platform", LoginDestination.Resolve(true, "/exchange/", origin));
        Assert.Equal("/exchange/platform/reports", LoginDestination.Resolve(true, "platform/reports", origin));
        Assert.Equal("/exchange/platform", LoginDestination.Resolve(true, "/platform/audit", origin));
        Assert.Equal("/exchange/", LoginDestination.Home(false, origin));
    }

    [Theory]
    [InlineData(true, true, false, "GET", "/", "/platform")]
    [InlineData(true, true, false, "HEAD", "/", "/platform")]
    [InlineData(true, false, false, "GET", "/", null)]
    [InlineData(false, true, false, "GET", "/", null)]
    [InlineData(true, true, true, "GET", "/", null)]
    [InlineData(true, true, false, "POST", "/", null)]
    [InlineData(true, true, false, "GET", "/accounts", null)]
    [InlineData(true, true, false, "GET", "/platform", null)]
    public async Task Existing_platform_session_at_root_is_redirected_without_relaxing_other_route_policies(
        bool platform, bool role, bool impersonated, string method, string path, string? expected)
    {
        var claims = new List<Claim> { new("platform_user", platform ? "true" : "false") };
        if (role) claims.Add(new Claim(ClaimTypes.Role, PlatformRoles.Owner));
        if (impersonated) claims.Add(new Claim(ImpersonationIdentity.SessionClaim, "fake"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        context.Request.Method = method; context.Request.Path = path;
        var called = false;
        await new PlatformHomeRedirectMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.Equal(expected is null, called);
        Assert.Equal(expected ?? "", context.Response.Headers.Location.ToString());
        if (expected is not null) Assert.Equal(302, context.Response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_root_is_not_redirected_and_pathbase_is_preserved_for_platform()
    {
        var called = false;
        var middleware = new PlatformHomeRedirectMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext(); context.Request.Path = "/"; context.Request.Method = "GET";
        await middleware.InvokeAsync(context); Assert.True(called);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("platform_user", "true"), new Claim(ClaimTypes.Role, PlatformRoles.Viewer)], "Test"));
        context.Request.PathBase = "/exchange"; called = false;
        await middleware.InvokeAsync(context);
        Assert.False(called); Assert.Equal("/exchange/platform", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Cookie_login_and_reopening_root_reach_platform_while_business_routes_remain_forbidden()
    {
        // An isolated HTTP host, synthetic identities only; never starts the real app or touches its database.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("TestCookie").AddCookie("TestCookie", options =>
            options.AccessDeniedPath = "/Account/AccessDenied");
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Platform", policy => policy.RequireClaim("platform_user", "true").RequireRole(PlatformRoles.Owner));
            options.AddPolicy("Tenant", policy => policy.RequireClaim("tenant_id"));
        });
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseMiddleware<PlatformHomeRedirectMiddleware>(); app.UseAuthorization();
        app.MapGet("/", () => "tenant dashboard").RequireAuthorization("Tenant");
        app.MapGet("/accounts", () => "tenant accounts").RequireAuthorization("Tenant");
        app.MapGet("/platform", () => "platform dashboard").RequireAuthorization("Platform");
        app.MapPost("/test-sign-in", async (HttpContext context, bool platform) =>
        {
            var claims = new List<Claim> { new("platform_user", platform ? "true" : "false") };
            if (platform) claims.Add(new Claim(ClaimTypes.Role, PlatformRoles.Owner));
            else claims.Add(new Claim("tenant_id", "123"));
            await context.SignInAsync("TestCookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "TestCookie")));
            context.Response.Redirect(LoginDestination.Resolve(platform, "/", $"{context.Request.Scheme}://{context.Request.Host}/"));
        });
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var platformHandler = new HttpClientHandler { AllowAutoRedirect = false };
            using var platformClient = new HttpClient(platformHandler) { BaseAddress = new Uri(address) };
            using var signedIn = await platformClient.PostAsync("/test-sign-in?platform=true", null);
            Assert.Equal(System.Net.HttpStatusCode.Redirect, signedIn.StatusCode);
            Assert.Equal("/platform", signedIn.Headers.Location?.OriginalString);
            using var dashboard = await platformClient.GetAsync("/platform");
            Assert.Equal(System.Net.HttpStatusCode.OK, dashboard.StatusCode);
            Assert.Equal("platform dashboard", await dashboard.Content.ReadAsStringAsync());
            using var root = await platformClient.GetAsync("/");
            Assert.Equal(System.Net.HttpStatusCode.Redirect, root.StatusCode);
            Assert.Equal("/platform", root.Headers.Location?.OriginalString);
            using var business = await platformClient.GetAsync("/accounts");
            Assert.Equal(System.Net.HttpStatusCode.Redirect, business.StatusCode);
            Assert.Contains("/Account/AccessDenied", business.Headers.Location!.OriginalString);

            using var tenantHandler = new HttpClientHandler { AllowAutoRedirect = false };
            using var tenantClient = new HttpClient(tenantHandler) { BaseAddress = new Uri(address) };
            using var tenantSignIn = await tenantClient.PostAsync("/test-sign-in?platform=false", null);
            Assert.Equal("/", tenantSignIn.Headers.Location?.OriginalString);
            using var tenantRoot = await tenantClient.GetAsync("/");
            Assert.Equal("tenant dashboard", await tenantRoot.Content.ReadAsStringAsync());
            using var tenantDenied = await tenantClient.GetAsync("/platform");
            Assert.Equal(System.Net.HttpStatusCode.Redirect, tenantDenied.StatusCode);
            Assert.Contains("/Account/AccessDenied", tenantDenied.Headers.Location!.OriginalString);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public void All_login_flows_and_access_denied_use_account_aware_destinations()
    {
        var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
        foreach (var file in new[] { "Login", "LoginWith2fa", "LoginWithRecoveryCode", "ExternalLogin" })
        {
            var source = File.ReadAllText(Path.Combine(root, "HawalaExchange.Web/Components/Account/Pages", file + ".razor"));
            Assert.Contains("LoginDestination.Resolve(", source);
            Assert.DoesNotContain("RedirectManager.RedirectTo(ReturnUrl)", source);
        }
        var denied = File.ReadAllText(Path.Combine(root, "HawalaExchange.Web/Components/Account/Pages/AccessDenied.razor"));
        Assert.Contains("LoginDestination.Home(", denied);
        Assert.DoesNotContain("href=\"/\"", denied);
        var program = File.ReadAllText(Path.Combine(root, "HawalaExchange.Web/Program.cs"));
        Assert.True(program.IndexOf("UseAuthentication()", StringComparison.Ordinal) < program.IndexOf("UseMiddleware<PlatformHomeRedirectMiddleware>()", StringComparison.Ordinal));
        Assert.True(program.IndexOf("UseMiddleware<PlatformHomeRedirectMiddleware>()", StringComparison.Ordinal) < program.IndexOf("UseAuthorization()", StringComparison.Ordinal));
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
