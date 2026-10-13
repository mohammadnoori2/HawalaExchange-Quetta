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

    [Theory]
    [InlineData(3)]
    [InlineData(417)]
    [InlineData(3700)]
    public async Task Preparation_saves_locations_without_financial_posting_and_survives_reload_and_repeat(int rows)
    {
        var request = await PreviewAsync(rows);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var beforeHawalas = await context.Hawalas.CountAsync();
        var beforeLedger = await context.LedgerEntries.CountAsync();
        var import = CreateImport(context);
        var queue = CreateQueue(context);
        var final = new ConfirmHawalaImportDto { BatchId = request.BatchId, UsePreparedLocations = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.EnqueueAsync(final));
        var watch = Stopwatch.StartNew();
        var prepared = await import.PrepareLocationsAsync(request);
        output.WriteLine($"Rows={rows}; preparation={watch.Elapsed.TotalSeconds:F3}s");
        Assert.True(prepared.LocationsPrepared);
        Assert.Empty(prepared.MissingLocations);
        Assert.All(prepared.PaymentLocations.Where(x => x.PaymentLocationId != prepared.OwnPaymentLocationId), x => Assert.NotNull(x.ResponsibleCorrespondentId));
        Assert.Equal(beforeHawalas, await context.Hawalas.CountAsync());
        Assert.Equal(beforeLedger, await context.LedgerEntries.CountAsync());
        var locationCount = await context.PaymentLocations.CountAsync();
        Assert.True((await import.GetPreviewAsync(request.BatchId))!.LocationsPrepared);
        Assert.True((await import.PrepareLocationsAsync(request)).LocationsPrepared);
        Assert.Equal(locationCount, await context.PaymentLocations.CountAsync());
        Assert.Equal("Preview", (await context.HawalaImportBatches.AsNoTracking().SingleAsync(x => x.Id == request.BatchId)).Status);
        await queue.EnqueueAsync(final);
        var jobId = await context.HawalaImportJobs.Where(x => x.BatchId == request.BatchId).Select(x => x.Id).SingleAsync();
        Assert.True(await queue.ProcessAsync(jobId, CancellationToken.None));
        context.ChangeTracker.Clear();
        Assert.Equal("Completed", (await context.HawalaImportJobs.SingleAsync(x => x.Id == jobId)).Status);
        Assert.Equal(beforeHawalas + 2 * rows - 1, await context.Hawalas.CountAsync());
        Assert.Equal(locationCount, await context.PaymentLocations.CountAsync());
    }

    [Fact]
    public async Task Diagnose_delete_of_3700_imported_rows_with_real_audit_service()
    {
        var request = await PreviewAsync(3700);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var queue = CreateQueue(context);
        await queue.EnqueueAsync(request);
        var jobId = await context.HawalaImportJobs.Where(x => x.BatchId == request.BatchId).Select(x => x.Id).SingleAsync();
        Assert.True(await queue.ProcessAsync(jobId, CancellationToken.None));
        context.ChangeTracker.Clear();
        Assert.Equal("Completed", await context.HawalaImportJobs.Where(x => x.Id == jobId).Select(x => x.Status).SingleAsync());
        var ids = await context.HawalaImportRows.Where(x => x.BatchId == request.BatchId)
            .Select(x => new { x.HawalaId, x.GeneratedSendHawalaId }).ToListAsync();
        var hawalaIds = ids.SelectMany(x => new[] { x.HawalaId, x.GeneratedSendHawalaId })
            .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var mapper = new MapperConfiguration(config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var import = new HawalaImportService(context, fixture.CreateService(context),
            Mock.Of<IPaymentLocationService>(), Mock.Of<ICorrespondentService>(), new AuditLogService(context, mapper));
        var locations = await context.Hawalas.Where(x => x.HawalaType == "HawalaSend").Select(x => x.PaymentLocationId!.Value).Distinct().ToListAsync();
        var day = (await context.Hawalas.Select(x => x.CreatedAt).FirstAsync()).ToLocalTime().Date;
        foreach (var scope in new[] { "Incoming", "Forwarding", "Destination" })
            await fixture.CreateCommissionService(context).PostAsync(new CorrespondentCommissionPreviewRequestDto
            {
                CommissionScope = scope, HawalaType = scope == "Incoming" ? "HawalaReceive" : "HawalaSend",
                CorrespondentId = scope == "Destination" ? fixture.DestinationCorrespondent.Id : fixture.SourceCorrespondent.Id,
                PeriodFrom = day, PeriodTo = day, CommissionPerLakhAfn = 400m,
                PaymentLocationRates = locations.Select(id => new PaymentLocationCommissionRateDto { PaymentLocationId = id, PerLakhRate = 400m }).ToList(),
                LocationCurrencyRates = locations.Select(id => new LocationCurrencyCommissionRateDto { PaymentLocationId = id, CurrencyId = 2, PerLakhRate = 2000m }).ToList()
            });
        var watch = Stopwatch.StartNew();
        await import.DeleteBatchAsync(request.BatchId);
        output.WriteLine($"Deleted rows=3700; hawalas={hawalaIds.Length}; deletion={watch.Elapsed.TotalSeconds:F3}s");
        Assert.Equal("Deleted", await context.HawalaImportBatches.Where(x => x.Id == request.BatchId).Select(x => x.Status).SingleAsync());
        Assert.DoesNotContain(await import.GetHistoryAsync(), x => x.BatchId == request.BatchId);
        Assert.Null(await import.GetDetailsAsync(request.BatchId));
        Assert.Equal(hawalaIds.Length, await context.Hawalas.CountAsync(x => hawalaIds.Contains(x.Id) && x.Status == "Cancel"));
        Assert.False(await context.CorrespondentCommissionBatchItems.AnyAsync(x => x.IsActive));
        Assert.All(await context.LedgerEntries.Where(x => x.HawalaId.HasValue && hawalaIds.Contains(x.HawalaId.Value))
            .GroupBy(x => new { x.AccountId, x.CurrencyId }).Select(x => x.Sum(e => e.TalabKar - e.BadehKar)).ToListAsync(), x => Assert.Equal(0m, x));
        var entries = await context.LedgerEntries.CountAsync();
        await import.DeleteBatchAsync(request.BatchId);
        Assert.Equal(entries, await context.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Failed_posting_preserves_prepared_locations_and_responsibilities_for_corrected_retry()
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var import = CreateImport(context);
        var prepared = await import.PrepareLocationsAsync(request);
        var beforeHawalas = await context.Hawalas.CountAsync();
        var beforeLedger = await context.LedgerEntries.CountAsync();
        var row = prepared.Rows.First(x => x.RequiresOutgoingHawala);
        var final = new ConfirmHawalaImportDto
        {
            BatchId = request.BatchId, UsePreparedLocations = true,
            Commissions = [new HawalaImportCommissionDto { RowId = row.Id, Amount = 0, CurrencyId = 2 }]
        };
        var queue = CreateQueue(context);
        await queue.EnqueueAsync(final);
        var jobId = await context.HawalaImportJobs.Where(x => x.BatchId == request.BatchId).Select(x => x.Id).SingleAsync();
        await queue.ProcessAsync(jobId, CancellationToken.None);
        context.ChangeTracker.Clear();
        Assert.Equal("Failed", (await context.HawalaImportJobs.SingleAsync(x => x.Id == jobId)).Status);
        Assert.Equal(beforeHawalas, await context.Hawalas.CountAsync());
        Assert.Equal(beforeLedger, await context.LedgerEntries.CountAsync());
        var retained = await import.GetPreviewAsync(request.BatchId);
        Assert.True(retained!.LocationsPrepared);
        Assert.Empty(retained.MissingLocations);
        Assert.All(retained.PaymentLocations.Where(x => x.PaymentLocationId != retained.OwnPaymentLocationId), x => Assert.NotNull(x.ResponsibleCorrespondentId));
        final.Commissions.Clear();
        await queue.EnqueueAsync(final);
        await queue.ProcessAsync(jobId, CancellationToken.None);
        context.ChangeTracker.Clear();
        Assert.Equal("Completed", (await context.HawalaImportJobs.SingleAsync(x => x.Id == jobId)).Status);
    }

    [Fact]
    public async Task Invalid_preparation_rolls_back_locations_and_never_marks_batch_prepared()
    {
        var request = await PreviewAsync(3);
        request.OwnPaymentLocationName = "Unknown own location";
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var locations = await context.PaymentLocations.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateImport(context).PrepareLocationsAsync(request));
        context.ChangeTracker.Clear();
        Assert.Equal(locations, await context.PaymentLocations.CountAsync());
        Assert.Null((await context.HawalaImportBatches.SingleAsync(x => x.Id == request.BatchId)).LocationsPreparedAt);
        Assert.False((await CreateImport(context).GetPreviewAsync(request.BatchId))!.LocationsPrepared);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_unposted_draft_survives_cleanup_and_new_upload_and_can_be_posted_later(bool prepared)
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var import = CreateImport(context);
        if (prepared) await import.PrepareLocationsAsync(request);
        await context.HawalaImportBatches.Where(x => x.Id == request.BatchId)
            .ExecuteUpdateAsync(x => x.SetProperty(b => b.CreatedAt, DateTime.UtcNow.AddDays(-365)));
        await context.Database.ExecuteSqlInterpolatedAsync($"EXEC dbo.usp_CleanupHawalaImportStaging_v1 @TenantId={1L}, @CreatedBefore={DateTime.UtcNow}");
        await PreviewAsync(3);
        context.ChangeTracker.Clear();
        var restored = await import.GetPreviewAsync(request.BatchId);
        Assert.NotNull(restored);
        Assert.Equal(3, restored.Rows.Count);
        Assert.Equal(prepared, restored.LocationsPrepared);
        Assert.Contains(await import.GetHistoryAsync(), x => x.BatchId == request.BatchId && x.Status == "Preview");
        Assert.False(await context.HawalaImportRows.AnyAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
        await import.PrepareLocationsAsync(request);
        await CreateQueue(context).EnqueueAsync(new ConfirmHawalaImportDto { BatchId = request.BatchId, UsePreparedLocations = true });
        var jobId = await context.HawalaImportJobs.Where(x => x.BatchId == request.BatchId).Select(x => x.Id).SingleAsync();
        Assert.True(await CreateQueue(context).ProcessAsync(jobId, CancellationToken.None));
        Assert.Equal("Completed", await context.HawalaImportJobs.Where(x => x.Id == jobId).Select(x => x.Status).SingleAsync());
        Assert.Equal(3, await context.HawalaImportRows.CountAsync(x => x.BatchId == request.BatchId && x.HawalaId != null));
        Assert.True(await context.LedgerEntries.AnyAsync(x => x.HawalaId.HasValue && context.HawalaImportRows.Any(r => r.BatchId == request.BatchId && r.HawalaId == x.HawalaId)));
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
        services.AddScoped<HawalaImportService>(provider =>
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            var mapper = new MapperConfiguration(config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
            return new HawalaImportService(context, fixture.CreateService(context),
                new PaymentLocationService(context, factory, mapper, Mock.Of<IAuditLogService>()),
                Mock.Of<ICorrespondentService>(), Mock.Of<IAuditLogService>(), factory);
        });
        services.AddScoped<IHawalaImportService>(sp=>sp.GetRequiredService<HawalaImportService>());
        services.AddScoped<IHawalaImportJobExecutor>(sp=>sp.GetRequiredService<HawalaImportService>());
        services.AddSingleton(Mock.Of<IUserPermissionService>());
        services.ProtectTenantServices();
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

    [Theory]
    [InlineData(417)]
    [InlineData(3700)]
    public async Task Prepared_import_posts_through_permission_wrapped_executor(int rows)
    {
        var request=await PreviewAsync(rows);
        await using var context=fixture.CreateContext();
        using var bypass=context.BypassSubscriptionEnforcement();
        var import=CreateImport(context);
        await import.PrepareLocationsAsync(request);
        var final=new ConfirmHawalaImportDto {BatchId=request.BatchId,UsePreparedLocations=true};
        var permissions=new Mock<IUserPermissionService>();
        permissions.Setup(x=>x.RequireAsync("BulkImports.Create",It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var provider=ExecutorServices(import,permissions.Object);
        var queue=new HawalaImportQueue(context,new Factory(fixture),provider.GetRequiredService<IHawalaImportJobExecutor>());
        await queue.EnqueueAsync(final);
        var id=await context.HawalaImportJobs.Where(x=>x.BatchId==request.BatchId).Select(x=>x.Id).SingleAsync();
        Assert.True(await queue.ProcessAsync(id,CancellationToken.None));
        context.ChangeTracker.Clear();
        Assert.Equal("Completed",await context.HawalaImportJobs.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());
        Assert.Equal(rows,await context.HawalaImportRows.CountAsync(x=>x.BatchId==request.BatchId && x.HawalaId!=null));
        permissions.Verify(x=>x.RequireAsync("BulkImports.Create",It.IsAny<CancellationToken>()),Times.Once);
        Assert.False(await queue.ProcessAsync(id,CancellationToken.None));
    }

    [Fact]
    public async Task Executor_denial_preserves_prepared_import_and_retry_posts_without_reupload()
    {
        var request=await PreviewAsync(3);
        await using var context=fixture.CreateContext();
        using var bypass=context.BypassSubscriptionEnforcement();
        var import=CreateImport(context);
        await import.PrepareLocationsAsync(request);
        var permissions=new Mock<IUserPermissionService>();
        permissions.Setup(x=>x.RequireAsync("BulkImports.Create",It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(new UnauthorizedAccessException()));
        using var provider=ExecutorServices(import,permissions.Object);
        var queue=new HawalaImportQueue(context,new Factory(fixture),provider.GetRequiredService<IHawalaImportJobExecutor>());
        await queue.EnqueueAsync(new ConfirmHawalaImportDto {BatchId=request.BatchId,UsePreparedLocations=true});
        var id=await context.HawalaImportJobs.Where(x=>x.BatchId==request.BatchId).Select(x=>x.Id).SingleAsync();
        Assert.True(await queue.ProcessAsync(id,CancellationToken.None));
        var failed=await context.HawalaImportJobs.AsNoTracking().SingleAsync(x=>x.Id==id);
        Assert.Equal("Failed",failed.Status); Assert.Contains("صلاحیت",failed.ErrorMessage);
        Assert.False(await context.HawalaImportRows.AnyAsync(x=>x.BatchId==request.BatchId && x.HawalaId!=null));
        Assert.NotNull(await context.HawalaImportBatches.Where(x=>x.Id==request.BatchId).Select(x=>x.LocationsPreparedAt).SingleAsync());
        permissions.Setup(x=>x.RequireAsync("BulkImports.Create",It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await queue.RetryAsync(request.BatchId);
        Assert.True(await queue.ProcessAsync(id,CancellationToken.None));
        Assert.Equal("Completed",await context.HawalaImportJobs.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());
        Assert.Equal(3,await context.HawalaImportRows.CountAsync(x=>x.BatchId==request.BatchId && x.HawalaId!=null));
    }

    private ServiceProvider ExecutorServices(HawalaImportService import,IUserPermissionService permissions)
    {
        var services=new ServiceCollection();
        services.AddSingleton(permissions);
        services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(new Factory(fixture));
        services.AddSingleton<IHawalaImportJobExecutor>(import);
        services.ProtectTenantServices();
        return services.BuildServiceProvider();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleting_one_file_preserves_other_files_commissions_and_daily_rounding(bool closePeriod)
    {
        var first = await PreviewAsync(3, 120m);
        var second = await PreviewAsync(3, 120m, 10000);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var import = CreateImport(context);
        await import.ConfirmAsync(first);
        await import.ConfirmAsync(second);
        context.ChangeTracker.Clear();
        var locations = await context.Hawalas.Where(x => x.HawalaType == "HawalaSend")
            .Select(x => x.PaymentLocationId!.Value).Distinct().ToListAsync();
        var day = (await context.Hawalas.Select(x => x.CreatedAt).FirstAsync()).ToLocalTime().Date;
        foreach (var scope in new[] { "Incoming", "Forwarding", "Destination" })
        {
            await fixture.CreateCommissionService(context).PostAsync(new CorrespondentCommissionPreviewRequestDto
            {
                CommissionScope = scope, HawalaType = scope == "Incoming" ? "HawalaReceive" : "HawalaSend",
                CorrespondentId = scope == "Destination" ? fixture.DestinationCorrespondent.Id : fixture.SourceCorrespondent.Id,
                PeriodFrom = day, PeriodTo = day, CommissionPerLakhAfn = 1000m,
                PaymentLocationRates = locations.Select(id => new PaymentLocationCommissionRateDto { PaymentLocationId = id, PerLakhRate = 400m }).ToList(),
                LocationCurrencyRates = locations.Select(id => new LocationCurrencyCommissionRateDto { PaymentLocationId = id, CurrencyId = 2, PerLakhRate = 2000m }).ToList()
            });
        }
        context.ChangeTracker.Clear();
        var originals = await context.CorrespondentCommissionBatches.AsNoTracking().ToListAsync();
        Assert.Equal(2m, originals.Single(x => x.CommissionScope == "Forwarding").TotalCommissionUsd);
        if (closePeriod)
        {
            await fixture.CreateCorrespondentService(context).ClosePeriodAsync(fixture.SourceCorrespondent.Id, new());
            await fixture.CreateCorrespondentService(context).ClosePeriodAsync(fixture.DestinationCorrespondent.Id, new());
        }
        var closedBalances = System.Text.Json.JsonSerializer.Serialize(await context.CorrespondentAccountPeriodBalances.AsNoTracking().OrderBy(x => x.Id).ToListAsync());
        var preview = await import.GetDeletionPreviewAsync(first.BatchId);
        Assert.Equal(1m, preview!.Commissions.Single(x => x.Scope == "Forwarding").RemainingUsd);
        await import.DeleteBatchAsync(first.BatchId);
        var remaining = await context.CorrespondentCommissionBatches.AsNoTracking().Where(x => x.Status == "Posted").ToListAsync();
        Assert.Equal(3, remaining.Count);
        Assert.Equal(4m, remaining.Single(x => x.CommissionScope == "Incoming").TotalCommissionUsd);
        Assert.Equal(1m, remaining.Single(x => x.CommissionScope == "Forwarding").TotalCommissionUsd);
        Assert.Equal(5m, remaining.Single(x => x.CommissionScope == "Destination").TotalCommissionUsd);
        Assert.Equal(3, await context.CorrespondentCommissionBatches.CountAsync(x => x.Status == "Reversed"));
        Assert.Equal(3, await context.HawalaImportRows.CountAsync(x => x.BatchId == second.BatchId && x.Hawala.Status != "Cancel"));
        Assert.All(await context.LedgerEntries.GroupBy(x => x.CurrencyId).Select(x => x.Sum(e => e.TalabKar - e.BadehKar)).ToListAsync(), x => Assert.Equal(0m, x));
        await import.DeleteBatchAsync(second.BatchId);
        Assert.Equal(closedBalances, System.Text.Json.JsonSerializer.Serialize(await context.CorrespondentAccountPeriodBalances.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
        Assert.False(await context.CorrespondentCommissionBatchItems.AnyAsync(x => x.IsActive));
        Assert.All(await context.LedgerEntries.GroupBy(x => new { x.AccountId, x.CurrencyId }).Select(x => x.Sum(e => e.TalabKar - e.BadehKar)).ToListAsync(), x => Assert.Equal(0m, x));
        if (closePeriod)
        {
            foreach (var correspondent in new[] { fixture.SourceCorrespondent, fixture.DestinationCorrespondent })
            {
                var accountId = await context.Accounts.Where(x => x.CorrespondentId == correspondent.Id && !x.IsArchived).Select(x => x.Id).FirstAsync();
                Assert.All(await fixture.CreateBalanceService(context).GetAccountBalanceAsync(accountId), x => Assert.Equal(0m, x.TotalIncludingCommission));
                await fixture.CreateCorrespondentService(context).ClosePeriodAsync(correspondent.Id, new());
                Assert.All(await fixture.CreateBalanceService(context).GetAccountBalanceAsync(accountId), x => Assert.Equal(0m, x.Balance));
            }
        }
    }

    [Fact]
    public async Task Editing_imported_amount_preserves_generated_identity_and_reposts_calculated_commission()
    {
        var request = await PreviewAsync(3, 100000m);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var import = CreateImport(context);
        await import.ConfirmAsync(request);
        context.ChangeTracker.Clear();
        var row = await context.HawalaImportRows.AsNoTracking().FirstAsync(x => x.BatchId == request.BatchId && x.GeneratedSendHawalaId != null);
        var hawala = await context.Hawalas.AsNoTracking().SingleAsync(x => x.Id == row.HawalaId);
        var day = hawala.CreatedAt.ToLocalTime().Date;
        await fixture.CreateCommissionService(context).PostAsync(new CorrespondentCommissionPreviewRequestDto
        {
            CommissionScope = "Incoming", HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id,
            PeriodFrom = day, PeriodTo = day, CommissionPerLakhAfn = 400m
        });
        context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService(context).UpdateHawalaAsync(hawala.Id, new UpdateHawalaDto
        {
            FromCurrencyId = 1, ToCurrencyId = 1, FromAmount = 200000m, ToAmount = 200000m,
            PaymentLocationId = hawala.PaymentLocationId, SenderName = hawala.SenderName,
            ReceiverName = hawala.ReceiverName, Status = hawala.Status, CorrespondentId = hawala.CorrespondentId
        }));
        context.ChangeTracker.Clear();
        Assert.Equal(1, await context.CorrespondentCommissionBatches.CountAsync(x => x.Status == "Posted"));
        Assert.Equal(100000m, await context.Hawalas.Where(x => x.Id == hawala.Id).Select(x => x.FromAmount).SingleAsync());
        await fixture.CreateService(context).UpdateHawalaAsync(hawala.Id, new UpdateHawalaDto
        {
            FromAmount = 200000m, ToAmount = 200000m, PaymentLocationId = hawala.PaymentLocationId,
            FromCurrencyId = hawala.FromCurrencyId, ToCurrencyId = hawala.ToCurrencyId,
            SenderName = hawala.SenderName, ReceiverName = hawala.ReceiverName, Status = hawala.Status,
            CorrespondentId = hawala.CorrespondentId, ReferenceNumber = hawala.ReferenceNumber
        });
        context.ChangeTracker.Clear();
        Assert.Equal(1600m, await context.CorrespondentCommissionBatches.Where(x => x.Status == "Posted").Select(x => x.TotalCommissionUsd).SingleAsync());
        Assert.Equal(row.GeneratedSendHawalaId, await context.HawalaImportRows.Where(x => x.Id == row.Id).Select(x => x.GeneratedSendHawalaId).SingleAsync());
        Assert.Equal(200000m, await context.Hawalas.Where(x => x.Id == row.GeneratedSendHawalaId).Select(x => x.ToAmount).SingleAsync());
        Assert.Equal(200000m, await context.HawalaImportRows.Where(x => x.Id == row.Id).Select(x => x.Amount).SingleAsync());
        Assert.All(await context.LedgerEntries.GroupBy(x => x.CurrencyId).Select(x => x.Sum(e => e.TalabKar - e.BadehKar)).ToListAsync(), x => Assert.Equal(0m, x));
        await import.DeleteBatchAsync(request.BatchId);
        Assert.All(await context.LedgerEntries.GroupBy(x => new { x.AccountId, x.CurrencyId }).Select(x => x.Sum(e => e.TalabKar - e.BadehKar)).ToListAsync(), x => Assert.Equal(0m, x));
    }

    [Fact]
    public async Task Saved_draft_commission_survives_reload_without_posting_financial_data()
    {
        var request = await PreviewAsync(3);
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var import = CreateImport(context);
        var prepared = await import.PrepareLocationsAsync(request);
        var row = prepared.Rows.First(x => x.RequiresOutgoingHawala);
        await import.SaveDraftAsync(new ConfirmHawalaImportDto { BatchId = request.BatchId,
            Commissions = [new HawalaImportCommissionDto { RowId = row.Id, Amount = 23m, CurrencyId = 2 }] });
        context.ChangeTracker.Clear();
        var reloaded = await import.GetPreviewAsync(request.BatchId);
        Assert.Equal(23m, reloaded!.Rows.Single(x => x.Id == row.Id).AgentCommissionAmount);
        Assert.False(await context.Hawalas.AnyAsync());
        Assert.False(await context.LedgerEntries.AnyAsync());
    }

    [Fact]
    public async Task Editing_afn_import_keeps_the_commissions_original_exchange_rate()
    {
        var request = await PreviewAsync(3, 70000m, currency: "AFN");
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var import = CreateImport(context);
        await import.ConfirmAsync(request);
        context.ChangeTracker.Clear();
        var row = await context.HawalaImportRows.AsNoTracking().FirstAsync(x => x.BatchId == request.BatchId && x.GeneratedSendHawalaId != null);
        var hawala = await context.Hawalas.AsNoTracking().SingleAsync(x => x.Id == row.HawalaId);
        var day = hawala.CreatedAt.ToLocalTime().Date;
        context.CorrespondentDailyCommissionRates.Add(new CorrespondentDailyCommissionRate
        { CorrespondentId = fixture.SourceCorrespondent.Id, RateDate = day, UsdToAfnRate = 70m, CreatedBy = fixture.UserId });
        await context.SaveChangesAsync();
        await RecordedSettlementTestData.SeedAsync(context,fixture.SourceCorrespondent.Id);
        await fixture.CreateCommissionService(context).PostAsync(new CorrespondentCommissionPreviewRequestDto
        {
            CommissionScope = "Incoming", HawalaType = "HawalaReceive", CorrespondentId = fixture.SourceCorrespondent.Id,
            PeriodFrom = day, PeriodTo = day, CommissionPerLakhAfn = 400m
        });
        context.ChangeTracker.Clear();
        await context.CorrespondentDailyCommissionRates.ExecuteUpdateAsync(x => x.SetProperty(r => r.UsdToAfnRate, 90m));
        await fixture.CreateService(context).UpdateHawalaAsync(hawala.Id, new UpdateHawalaDto
        {
            FromAmount = 140000m, ToAmount = 140000m, PaymentLocationId = hawala.PaymentLocationId,
            FromCurrencyId = 1, ToCurrencyId = 1, SenderName = hawala.SenderName,
            ReceiverName = hawala.ReceiverName, Status = hawala.Status, CorrespondentId = hawala.CorrespondentId
        });
        context.ChangeTracker.Clear();
        Assert.Equal(16m, await context.CorrespondentCommissionBatches.Where(x => x.Status == "Posted").Select(x => x.TotalCommissionUsd).SingleAsync());
        Assert.All(await context.CorrespondentCommissionBatchItems.AsNoTracking().Where(x => x.IsActive).ToListAsync(), x => Assert.Equal(70m, x.SourceToAfnRate));
        Assert.Equal(2000m, await context.CorrespondentCommissionBatchItems.Where(x => x.HawalaId == hawala.Id && x.IsActive).Select(x => x.AfnEquivalent).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.CreateService(context).DeleteHawalaAsync(row.GeneratedSendHawalaId!.Value));
        context.ChangeTracker.Clear();
        foreach(var batch in await context.CorrespondentCommissionBatches.AsNoTracking().Where(x=>x.Status=="Posted").Select(x=>x.Id).ToListAsync())
            await fixture.CreateCommissionService(context).ReverseAsync(batch,"Reverse before undoing settlement");
        foreach(var conversion in await context.CorrespondentSettlementConversions.AsNoTracking().Where(x=>x.CorrespondentId==fixture.SourceCorrespondent.Id && x.Transaction.Status!="Cancel").Select(x=>x.Id).ToListAsync())
            await fixture.CreateSettlementService(context).ReverseAsync(fixture.SourceCorrespondent.Id,conversion,"Undo settlement before deletion");
        await fixture.CreateService(context).DeleteHawalaAsync(row.GeneratedSendHawalaId!.Value);
        Assert.Equal(2, await context.Hawalas.CountAsync(x => (x.Id == hawala.Id || x.Id == row.GeneratedSendHawalaId) && x.Status == "Cancel"));
        Assert.False(await context.CorrespondentCommissionBatches.AnyAsync(x=>x.Status=="Posted"));
        await import.DeleteBatchAsync(request.BatchId);
        Assert.All(await context.LedgerEntries.GroupBy(x => new { x.AccountId, x.CurrencyId }).Select(x => x.Sum(e => e.TalabKar - e.BadehKar)).ToListAsync(), x => Assert.Equal(0m, x));
    }

    private async Task<ConfirmHawalaImportDto> PreviewAsync(int rows, decimal amount = 100m, int numberOffset = 0, string currency = "USD")
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Hawalas");
        var key = Guid.NewGuid().ToString("N")[..10];
        var newNames = new[] { $"Queue New {key} A", $"Queue New {key} B" };
        for (var index = 0; index < rows; index++)
        {
            object[] values = [98_000_000 + numberOffset + index, $"Q-{key}-{index}", "Sender", "Receiver",
                index == 0 ? fixture.OwnLocation.Name : newNames[index % 2], amount, currency];
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
