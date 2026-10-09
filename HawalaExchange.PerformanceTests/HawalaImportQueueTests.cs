using System.Diagnostics;
using AutoMapper;
using ClosedXML.Excel;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit.Abstractions;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace HawalaExchange.PerformanceTests;

// Own disposable database: turning RCSI off must never affect the server or other tests.
public sealed class HawalaImportQueueTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly SqlServerPerformanceFixture fixture = new();
    public async Task InitializeAsync()
    {
        await fixture.InitializeAsync();
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var settings = new CompanySetting { CompanyName = "Queue Test", OwnPaymentLocationId = fixture.OwnLocation.Id };
        context.CompanySettings.Add(settings);
        await context.SaveChangesAsync();
        var name = fixture.DatabaseName;
        Assert.StartsWith("HawalaExchangeQuettaPerformanceTests_", name);
        await context.Database.ExecuteSqlRawAsync($"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE");
    }
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData(3)]
    [InlineData(417)]
    [InlineData(3700)]
    public async Task Real_new_locations_and_accounting_finish_without_self_blocking(int rows)
    {
        var request = await PreviewAsync(rows);
        await using (var context = fixture.CreateContext())
        {
            using var bypass = context.BypassSubscriptionEnforcement();
            var queue = CreateQueue(context);
            var watch = Stopwatch.StartNew();
            await queue.EnqueueAsync(request);
            await queue.EnqueueAsync(request);
            output.WriteLine($"Rows={rows}; enqueue={watch.Elapsed.TotalSeconds:F3}s");
            Assert.Equal(1, await context.HawalaImportJobs.CountAsync(x => x.BatchId == request.BatchId));
            Assert.Equal(0, await context.HawalaImportRows.CountAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateImport(context).ConfirmAsync(request));
        }
        await using var worker = fixture.CreateContext();
        using var workerBypass = worker.BypassSubscriptionEnforcement();
        var jobId = await worker.HawalaImportJobs.Where(x => x.BatchId == request.BatchId).Select(x => x.Id).SingleAsync();
        var execution = Stopwatch.StartNew();
        Assert.True(await CreateQueue(worker).ProcessAsync(jobId, CancellationToken.None));
        output.WriteLine($"Rows={rows}; execution={execution.Elapsed.TotalSeconds:F3}s");
        worker.ChangeTracker.Clear();
        var job = await worker.HawalaImportJobs.SingleAsync(x => x.Id == jobId);
        Assert.Equal("Completed", job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.Equal(1, job.Attempts);
        var imported = await worker.HawalaImportRows.AsNoTracking().Where(x => x.BatchId == request.BatchId).ToListAsync();
        Assert.Equal(rows, imported.Count(x => x.HawalaId.HasValue));
        Assert.Equal(rows - 1, imported.Count(x => x.GeneratedSendHawalaId.HasValue));
        var ids = imported.SelectMany(x => new[] { x.HawalaId, x.GeneratedSendHawalaId }).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var ledger = await worker.LedgerEntries.Where(x => x.HawalaId.HasValue && ids.Contains(x.HawalaId.Value)).ToListAsync();
        Assert.NotEmpty(ledger);
        Assert.Equal(0, ledger.Sum(x => x.TalabKar - x.BadehKar));
        Assert.False(await CreateQueue(worker).ProcessAsync(jobId, CancellationToken.None));
        Assert.Equal(rows, await worker.HawalaImportRows.CountAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
    }

    [Fact]
    public async Task Failure_rolls_back_locations_and_hawalas_and_can_be_corrected_and_retried()
    {
        var request = await PreviewAsync(3);
        var ownName = request.OwnPaymentLocationName;
        request.OwnPaymentLocationName = "Own location does not exist";
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var queue = CreateQueue(context);
        await queue.EnqueueAsync(request);
        var jobId = await context.HawalaImportJobs.Where(x => x.BatchId == request.BatchId).Select(x => x.Id).SingleAsync();
        // Simulate the stale tracked Queued state in a long-lived browser circuit.
        await context.HawalaImportJobs.SingleAsync(x => x.Id == jobId);
        Assert.True(await queue.ProcessAsync(jobId, CancellationToken.None));
        Assert.Equal("Failed", (await context.HawalaImportJobs.AsNoTracking().SingleAsync(x => x.Id == jobId)).Status);
        Assert.False(await context.HawalaImportRows.AnyAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
        Assert.False(await context.PaymentLocations.AnyAsync(x => request.LocationsToCreate.Contains(x.Name)));
        request.OwnPaymentLocationName = ownName;
        await queue.EnqueueAsync(request);
        context.ChangeTracker.Clear();
        Assert.True(await queue.ProcessAsync(jobId, CancellationToken.None));
        context.ChangeTracker.Clear();
        Assert.Equal("Completed", (await context.HawalaImportJobs.SingleAsync(x => x.Id == jobId)).Status);
        Assert.Equal(1, await context.HawalaImportJobs.CountAsync(x => x.BatchId == request.BatchId));
    }

    [Fact]
    public async Task Restart_recovers_running_job_and_completed_batch_is_never_reposted()
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await CreateQueue(context).EnqueueAsync(request);
        var jobId = await context.HawalaImportJobs.Select(x => x.Id).SingleAsync();
        await context.HawalaImportJobs.Where(x => x.Id == jobId).ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, "Running"));
        context.ChangeTracker.Clear();
        Assert.True(await CreateQueue(context).ProcessAsync(jobId, CancellationToken.None));
        await context.HawalaImportJobs.Where(x => x.Id == jobId).ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, "Running"));
        context.ChangeTracker.Clear();
        Assert.True(await CreateQueue(context).ProcessAsync(jobId, CancellationToken.None));
        Assert.Equal(3, await context.HawalaImportRows.CountAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
        Assert.Equal("Completed", await context.HawalaImportJobs.Where(x => x.Id == jobId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Database_global_lease_prevents_a_second_worker_and_is_released_after_processing()
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var queue = CreateQueue(context);
        await queue.EnqueueAsync(request);
        var jobId = await context.HawalaImportJobs.Select(x => x.Id).SingleAsync();
        await using (var lease = fixture.CreateContext())
        {
            await lease.Database.OpenConnectionAsync();
            await lease.Database.ExecuteSqlRawAsync("EXEC sys.sp_getapplock @Resource=N'HawalaImportWorker:v1', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0;");
            Assert.False(await queue.ProcessAsync(jobId, CancellationToken.None));
            Assert.Equal("Queued", await context.HawalaImportJobs.Where(x => x.Id == jobId).Select(x => x.Status).SingleAsync());
            await lease.Database.ExecuteSqlRawAsync("EXEC sys.sp_releaseapplock @Resource=N'HawalaImportWorker:v1', @LockOwner=N'Session';");
        }
        Assert.True(await queue.ProcessAsync(jobId, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_staging_cleanup_and_delete_cannot_remove_queued_requests()
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await CreateQueue(context).EnqueueAsync(request);
        await context.HawalaImportBatches.Where(x => x.Id == request.BatchId)
            .ExecuteUpdateAsync(x => x.SetProperty(b => b.CreatedAt, DateTime.UtcNow.AddDays(-10)));
        await context.Database.ExecuteSqlInterpolatedAsync($"EXEC dbo.usp_CleanupHawalaImportStaging_v1 @TenantId={1L}, @CreatedBefore={DateTime.UtcNow}");
        Assert.True(await context.HawalaImportBatches.AnyAsync(x => x.Id == request.BatchId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateImport(context).DeleteBatchAsync(request.BatchId));
        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @f nvarchar(260), @r int, @d int; EXEC dbo.usp_DeleteHawalaImportBatch_v1 @TenantId={1L}, @BatchId={request.BatchId}, @FileName=@f OUTPUT, @RowCount=@r OUTPUT, @DeletedHawalaCount=@d OUTPUT;"));
        Assert.Equal(51044, error.Number);
    }

    [Fact]
    public async Task Other_tenant_cannot_enqueue_retry_or_execute_this_batch()
    {
        var request = await PreviewAsync(3);
        await using var original = fixture.CreateContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(original.Database.GetConnectionString()).Options;
        await using var foreign = new ApplicationDbContext(options, new TestCurrentTenant { TenantId = 2, UserId = fixture.UserId });
        using var bypass = foreign.BypassSubscriptionEnforcement();
        var queue = CreateQueue(foreign);
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.EnqueueAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.RetryAsync(request.BatchId));
        Assert.False(await queue.ProcessAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Hosted_worker_uses_saved_identity_without_an_http_request_and_stops_cleanly()
    {
        var request = await PreviewAsync(3);
        await using var original = fixture.CreateContext();
        using var bypass = original.BypassSubscriptionEnforcement();
        await CreateQueue(original).EnqueueAsync(request);
        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(original.Database.GetConnectionString()).Options;
        services.AddSingleton(options);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddScoped<AuthenticationStateProvider>(_ => Mock.Of<AuthenticationStateProvider>());
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<CurrentTenant>());
        services.AddScoped<ApplicationDbContext>();
        services.AddScoped<IDbContextFactory<ApplicationDbContext>, TenantDbContextFactory>();
        services.AddScoped<IHawalaImportService>(provider =>
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            var mapper = new MapperConfiguration(config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
            return new HawalaImportService(context, fixture.CreateService(context),
                new PaymentLocationService(context, factory, mapper, Mock.Of<IAuditLogService>()),
                Mock.Of<ICorrespondentService>(), Mock.Of<IAuditLogService>(), factory);
        });
        services.AddScoped<HawalaImportQueue>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var worker = new HawalaImportWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<HawalaImportWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (await original.HawalaImportJobs.Where(x => x.BatchId == request.BatchId).Select(x => x.Status).SingleAsync(timeout.Token) != "Completed")
                await Task.Delay(100, timeout.Token);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.True(worker.ExecuteTask?.IsCompletedSuccessfully);
        Assert.Equal(fixture.UserId, await original.HawalaImportBatches.Where(x => x.Id == request.BatchId).Select(x => x.ConfirmedBy).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_confirm_clicks_create_one_durable_request()
    {
        var request = await PreviewAsync(3);
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        using var firstBypass = first.BypassSubscriptionEnforcement();
        using var secondBypass = second.BypassSubscriptionEnforcement();
        await Task.WhenAll(CreateQueue(first).EnqueueAsync(request), CreateQueue(second).EnqueueAsync(request));
        Assert.Equal(1, await first.HawalaImportJobs.CountAsync(x => x.BatchId == request.BatchId));
    }

    [Fact]
    public async Task Inactive_submitter_is_failed_instead_of_posting_financial_data()
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await CreateQueue(context).EnqueueAsync(request);
        var jobId = await context.HawalaImportJobs.Select(x => x.Id).SingleAsync();
        await context.Users.Where(x => x.Id == fixture.UserId).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        Assert.True(await CreateQueue(context).ProcessAsync(jobId, CancellationToken.None));
        Assert.Equal("Failed", await context.HawalaImportJobs.Where(x => x.Id == jobId).Select(x => x.Status).SingleAsync());
        Assert.False(await context.HawalaImportRows.AnyAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
    }

    [Fact]
    public async Task Shutdown_during_posting_rolls_back_and_preserves_request_for_restart()
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await CreateQueue(context).EnqueueAsync(request);
        var jobId = await context.HawalaImportJobs.Select(x => x.Id).SingleAsync();
        using var stop = new CancellationTokenSource();
        var realHawalas = fixture.CreateService(context);
        var cancelAfterPosting = new Mock<IHawalaService>();
        cancelAfterPosting.Setup(x => x.CreateHawalasAsync(It.IsAny<IReadOnlyCollection<CreateHawalaDto>>()))
            .Returns(async (IReadOnlyCollection<CreateHawalaDto> items) =>
            {
                var result = await realHawalas.CreateHawalasAsync(items);
                stop.Cancel();
                return result;
            });
        var queue = new HawalaImportQueue(context, new Factory(fixture), CreateImport(context, cancelAfterPosting.Object));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.ProcessAsync(jobId, stop.Token));
        context.ChangeTracker.Clear();
        Assert.Equal("Queued", await context.HawalaImportJobs.Where(x => x.Id == jobId).Select(x => x.Status).SingleAsync());
        Assert.False(await context.HawalaImportRows.AnyAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
        Assert.False(await context.PaymentLocations.AnyAsync(x => request.LocationsToCreate.Contains(x.Name)));
        Assert.False(await context.Hawalas.AnyAsync(x => x.ReferenceNumber != null && x.ReferenceNumber.StartsWith("Q-")));
        Assert.True(await CreateQueue(context).ProcessAsync(jobId, CancellationToken.None));
        Assert.Equal(3, await context.HawalaImportRows.CountAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
    }

    private HawalaImportService CreateImport(ApplicationDbContext context, IHawalaService? hawalas = null)
    {
        var mapper = new MapperConfiguration(config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var locations = new PaymentLocationService(context, new Factory(fixture), mapper, Mock.Of<IAuditLogService>());
        return new HawalaImportService(context, hawalas ?? fixture.CreateService(context), locations,
            Mock.Of<ICorrespondentService>(), Mock.Of<IAuditLogService>(), new Factory(fixture));
    }
    private HawalaImportQueue CreateQueue(ApplicationDbContext context) => new(context, new Factory(fixture), CreateImport(context));
    private sealed class Factory(SqlServerPerformanceFixture fixture) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => fixture.CreateContext();
    }

    private async Task<ConfirmHawalaImportDto> PreviewAsync(int rows)
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Hawalas");
        var key = Guid.NewGuid().ToString("N")[..10];
        var newNames = new[] { $"Queue New {key} A", $"Queue New {key} B" };
        for (var index = 0; index < rows; index++)
        {
            object[] values = [98_000_000 + index, $"Q-{key}-{index}", "Sender", "Receiver",
                index == 0 ? fixture.OwnLocation.Name : newNames[index % 2], 100, "USD"];
            for (var column = 0; column < values.Length; column++)
                sheet.Cell(index + 1, column + 1).Value = XLCellValue.FromObject(values[column]);
        }
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var preview = await CreateImport(context).PreviewAsync(stream, "queue-test.xlsx", fixture.SourceCorrespondent.Id);
        Assert.Equal(0, preview.InvalidRowCount);
        return new ConfirmHawalaImportDto
        {
            BatchId = preview.BatchId,
            OwnPaymentLocationName = fixture.OwnLocation.Name,
            LocationsToCreate = newNames.ToList(),
            LocationMappings = newNames.Select(name => new HawalaImportLocationMappingDto
            { PaymentLocationName = name, CorrespondentId = fixture.DestinationCorrespondent.Id }).ToList()
        };
    }
}
