using System.Reflection;
using System.Security.Claims;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Account;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class AuthenticationSessionTests(SqlServerPerformanceFixture fixture)
{
    [Theory]
    [InlineData(false, 31)]
    [InlineData(true, 31)]
    [InlineData(false, 1440)]
    public async Task Valid_cookie_is_renewed_before_http_tenant_is_initialized(bool platform, int ageMinutes)
    {
        var user = await CreateUser(platform);
        using var provider = Services();
        var result = await Authenticate(provider, user, ageMinutes);
        Assert.True(result.Principal.Identity!.IsAuthenticated);
        Assert.Equal(user.Id.ToString(), result.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Contains(".HawalaExchange.Quetta.Auth=", result.SetCookie);
        Assert.Equal(0, result.TenantBeforeAuthentication);
        if (!platform) Assert.Equal(user.TenantId.ToString(), result.Principal.FindFirstValue(CurrentTenant.TenantIdClaim));
    }

    [Theory]
    [InlineData("changed-stamp")]
    [InlineData("missing-stamp")]
    [InlineData("wrong-tenant")]
    [InlineData("missing-tenant")]
    [InlineData("wrong-platform")]
    [InlineData("inactive-user")]
    [InlineData("inactive-tenant")]
    [InlineData("missing-user")]
    public async Task Invalid_cookie_is_rejected_and_background_validation_agrees(string reason)
    {
        var user = await CreateUser();
        using var provider = Services();
        using var setup = provider.CreateScope();
        var principal = await setup.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;
        var claimType = reason switch
        {
            "changed-stamp" or "missing-stamp" => new IdentityOptions().ClaimsIdentity.SecurityStampClaimType,
            "wrong-tenant" or "missing-tenant" => CurrentTenant.TenantIdClaim,
            "wrong-platform" => "platform_user",
            "missing-user" => ClaimTypes.NameIdentifier,
            _ => ""
        };
        if (claimType.Length > 0)
        {
            foreach (var claim in identity.FindAll(claimType).ToList()) identity.RemoveClaim(claim);
            if (!reason.StartsWith("missing-")) identity.AddClaim(new Claim(claimType,
                reason == "wrong-tenant" ? "99999999" : reason == "wrong-platform" ? "true" : "changed"));
        }
        if (reason is "inactive-user" or "inactive-tenant")
        {
            await using var db = fixture.CreateContext();
            if (reason == "inactive-user")
                await db.Users.IgnoreQueryFilters().Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            else
            {
                // Use a separate tenant so other fixture tests remain unaffected.
                var tenant = new Tenant { Name = "Inactive session test", IsActive = false };
                db.Tenants.Add(tenant); await db.SaveChangesAsync();
                long branchId;
                using (db.UseTenantScope(tenant.Id))
                using (db.BypassSubscriptionEnforcement())
                {
                    var branch = new Branch { Name = "Session test", Code = "AUTH" };
                    db.Branches.Add(branch); await db.SaveChangesAsync(); branchId = branch.Id;
                }
                await db.Users.IgnoreQueryFilters().Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, tenant.Id).SetProperty(x => x.BranchId, branchId));
                foreach (var claim in identity.FindAll(CurrentTenant.TenantIdClaim).ToList()) identity.RemoveClaim(claim);
                identity.AddClaim(new Claim(CurrentTenant.TenantIdClaim, tenant.Id.ToString()));
            }
        }
        var result = await Authenticate(provider, user, 31, principal);
        Assert.False(result.Principal.Identity?.IsAuthenticated ?? false);
        Assert.Contains("expires=", result.SetCookie.ToLowerInvariant());
        await using var scope = provider.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var method = typeof(IdentityRevalidatingAuthenticationStateProvider).GetMethod("ValidateAuthenticationStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False(await (Task<bool>)method.Invoke(auth, [new AuthenticationState(principal), CancellationToken.None])!);
    }

    [Fact]
    public async Task Expired_cookie_stays_expired_and_recent_cookie_is_not_renewed_early()
    {
        var user = await CreateUser();
        using var provider = Services();
        var recent = await Authenticate(provider, user, 1);
        Assert.True(recent.Principal.Identity!.IsAuthenticated); Assert.Empty(recent.SetCookie);
        var expired = await Authenticate(provider, user, 31 * 24 * 60);
        Assert.False(expired.Principal.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task Background_session_remains_valid_without_tenant_context()
    {
        var user = await CreateUser(); using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var principal = await sp.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
        var auth = sp.GetRequiredService<AuthenticationStateProvider>();
        var method = typeof(IdentityRevalidatingAuthenticationStateProvider).GetMethod("ValidateAuthenticationStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(await (Task<bool>)method.Invoke(auth, [new AuthenticationState(principal), CancellationToken.None])!);
        Assert.Equal(0, sp.GetRequiredService<ICurrentTenant>().TenantId);
    }

    private async Task<ApplicationUser> CreateUser(bool platform = false)
    {
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var name = $"auth-{Guid.NewGuid():N}";
        var user = new ApplicationUser { TenantId = 1, UserName = name, NormalizedUserName = name.ToUpperInvariant(), LocalUserName = name,
            FullName = "Authentication test", BranchId = platform ? null : 1, SecurityStamp = Guid.NewGuid().ToString(), IsActive = true, IsPlatformUser = platform };
        db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }

    private ServiceProvider Services()
    {
        using var db = fixture.CreateContext();
        var connectionString = db.Database.GetConnectionString();
        var services = new ServiceCollection(); services.AddLogging(); services.AddHttpContextAccessor(); services.AddDataProtection();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));
        services.AddIdentity<ApplicationUser, IdentityRole<long>>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, TenantUserClaimsPrincipalFactory>();
        services.AddScoped<TenantSessionValidator>(); services.AddScoped<ISecurityStampValidator, TenantCookieSecurityStampValidator>();
        services.ConfigureApplicationCookie(o => { o.Cookie.Name = ".HawalaExchange.Quetta.Auth"; o.ExpireTimeSpan = TimeSpan.FromDays(30); o.SlidingExpiration = true; });
        return services.BuildServiceProvider();
    }

    private async Task<(ClaimsPrincipal Principal, string SetCookie, long TenantBeforeAuthentication)> Authenticate(
        ServiceProvider provider, ApplicationUser user, int ageMinutes, ClaimsPrincipal? principal = null)
    {
        await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var http = new DefaultHttpContext { RequestServices = sp }; http.Request.Scheme = "https";
        var response = new TestResponseFeature();
        http.Features.Set<IHttpResponseFeature>(response);
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        var before = sp.GetRequiredService<ICurrentTenant>().TenantId;
        principal ??= await sp.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
        var options = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var issued = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = issued, ExpiresUtc = issued.AddDays(30), IsPersistent = true }, IdentityConstants.ApplicationScheme);
        http.Request.Headers.Cookie = options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket);
        var middleware = new AuthenticationMiddleware(_ => Task.CompletedTask, sp.GetRequiredService<IAuthenticationSchemeProvider>());
        await middleware.Invoke(http);
        await response.FireStartingAsync();
        return (http.User, http.Response.Headers.SetCookie.ToString(), before);
    }

    private sealed class TestResponseFeature : HttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> starting = new();
        public override void OnStarting(Func<object, Task> callback, object state) => starting.Push((callback, state));
        public async Task FireStartingAsync()
        {
            while (starting.TryPop(out var entry)) await entry.Callback(entry.State);
        }
    }
}
