using System.Data.Common;
using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class TenantServiceDatabaseConcurrencyTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Concurrent_currency_and_branch_reads_use_the_shared_context_without_overlapping_sql()
    {
        await using var seed = fixture.CreateContext();
        var pause = new PauseFirstQuery();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(seed.Database.GetConnectionString()).AddInterceptors(pause).Options;
        await using var db = new ApplicationDbContext(options, new TestCurrentTenant());
        var permission = new Mock<IUserPermissionService>();
        permission.SetupGet(x => x.CurrentPermissions).Returns(new HashSet<string> { "Currencies.View", "Branches.View" });
        var registrations = new ServiceCollection()
            .AddSingleton(permission.Object).AddSingleton(Mock.Of<IDbContextFactory<ApplicationDbContext>>())
            .AddSingleton<IMapper>(new MapperConfiguration(x => x.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper())
            .AddScoped(_ => db).AddSingleton(Mock.Of<ISubscriptionAccessService>())
            .AddScoped<ICurrencyService, CurrencyService>().AddScoped<IBranchService, BranchService>();
        registrations.ProtectTenantServices();
        await using var provider = registrations.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var currency = scope.ServiceProvider.GetRequiredService<ICurrencyService>();
        var branches = scope.ServiceProvider.GetRequiredService<IBranchService>();
        var first = currency.GetAllAsync(); await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = branches.GetAllAsync();
        try { Assert.False(second.IsCompleted); Assert.Equal(1, pause.Calls); }
        finally { pause.Release.TrySetResult(); }
        Assert.NotEmpty(await first); Assert.NotEmpty(await second);
    }

    private sealed class PauseFirstQuery : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref Calls) == 1) { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
}
