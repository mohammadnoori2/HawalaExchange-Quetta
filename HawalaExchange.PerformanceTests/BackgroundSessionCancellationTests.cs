using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Components.Account;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HawalaExchange.PerformanceTests;

public class BackgroundSessionCancellationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelled_revalidation_does_not_fail_or_force_an_existing_session_to_sign_out(bool beforeQuery)
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelConnection(() => { cancellation.Cancel(); throw new TaskCanceledException("Controlled shutdown cancellation"); });
        using var services = Services(interceptor);
        if (beforeQuery) cancellation.Cancel();
        Assert.True(await Validate(services, cancellation.Token));
        Assert.Equal(beforeQuery ? 0 : 1, interceptor.Calls);
    }

    [Fact]
    public async Task Cancellation_does_not_authenticate_an_anonymous_session()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var interceptor = new CancelConnection(() => throw new InvalidOperationException("SQL must not be reached"));
        using var services = Services(interceptor);
        Assert.False(await Validate(services, cancellation.Token, authenticated: false));
        Assert.Equal(0, interceptor.Calls);
    }

    [Fact]
    public async Task Timeout_without_loop_cancellation_is_not_accepted_as_a_valid_session()
    {
        using var services = Services(new CancelConnection(() => throw new TaskCanceledException("Independent timeout")));
        await Assert.ThrowsAsync<TaskCanceledException>(() => Validate(services, CancellationToken.None));
    }

    [Fact]
    public async Task Actual_database_failure_is_not_hidden_even_when_the_loop_is_also_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        using var services = Services(new CancelConnection(() => { cancellation.Cancel(); throw new InvalidOperationException("Controlled database failure"); }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Validate(services, cancellation.Token));
    }

    private static async Task<bool> Validate(ServiceProvider services, CancellationToken token, bool authenticated = true)
    {
        using var auth = services.GetRequiredService<IdentityRevalidatingAuthenticationStateProvider>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], authenticated ? "TestCookie" : null));
        var method = typeof(IdentityRevalidatingAuthenticationStateProvider).GetMethod("ValidateAuthenticationStateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return await (Task<bool>)method.Invoke(auth, [new AuthenticationState(principal), token])!;
    }

    private static ServiceProvider Services(CancelConnection interceptor)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ICurrentTenant>(new TestCurrentTenant());
        // Interceptor always throws before opening SQL; no real database is accessed.
        services.AddDbContext<ApplicationDbContext>(x => x.UseSqlServer("Server=localhost;Database=UnusedCancellationTest;Integrated Security=true;TrustServerCertificate=true").AddInterceptors(interceptor));
        services.AddOptions<Microsoft.AspNetCore.Identity.IdentityOptions>();
        services.AddScoped<TenantSessionValidator>();
        services.AddTransient<IdentityRevalidatingAuthenticationStateProvider>();
        return services.BuildServiceProvider();
    }

    private sealed class CancelConnection(Action fail) : DbConnectionInterceptor
    {
        public int Calls { get; private set; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Calls++; fail(); return ValueTask.FromResult(result);
        }
    }
}
