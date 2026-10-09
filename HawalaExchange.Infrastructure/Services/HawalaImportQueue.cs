using System.Data;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HawalaExchange.Infrastructure.Services;

public sealed class HawalaImportQueue(
    ApplicationDbContext context,
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IHawalaImportService importService,
    ILogger<HawalaImportQueue>? logger = null) : IHawalaImportQueue
{
    public async Task EnqueueAsync(ConfirmHawalaImportDto request, CancellationToken cancellationToken = default)
    {
        var userId = context.RequireCurrentUserId();
        // Blazor circuit contexts live a long time. Never reuse their tracked job state
        // after a background scope has changed Queued -> Failed/Completed.
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (database.CurrentTenantId != context.CurrentTenantId || database.CurrentUserId != userId)
            throw new InvalidOperationException("هویت کاربر یا صرافی صف ثبت مطابقت ندارد.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var batch = await LockBatchAsync(database, request.BatchId, cancellationToken)
            ?? throw new InvalidOperationException("پیش‌نمایش پیدا نشد.");
        var job = await database.HawalaImportJobs.SingleOrDefaultAsync(x => x.BatchId == batch.Id, cancellationToken);
        if (batch.Status == "Posted" || job?.Status is "Queued" or "Running" or "Completed")
        {
            await transaction.CommitAsync(cancellationToken);
            return; // Double-clicks/reconnects are idempotent, not new financial postings.
        }
        if (batch.Status != "Preview") throw new InvalidOperationException("این فایل قابل ثبت نیست.");
        if (await database.HawalaImportRows.AnyAsync(x => x.BatchId == batch.Id &&
                x.ValidationErrors != null && x.ValidationErrors != "", cancellationToken))
            throw new InvalidOperationException("ابتدا خطاهای پیش‌نمایش را اصلاح کنید.");
        job ??= new HawalaImportJob { BatchId = batch.Id };
        job.RequestJson = JsonSerializer.Serialize(request);
        job.RequestedBy = userId;
        job.Status = "Queued";
        job.ProgressPercent = 0;
        job.ProgressMessage = "در صف ثبت گروهی؛ می‌توانید به کارهای دیگر برسید.";
        job.ErrorMessage = null;
        job.CreatedAt = DateTime.UtcNow;
        job.StartedAt = null;
        job.CompletedAt = null;
        if (job.Id == 0) database.HawalaImportJobs.Add(job);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RetryAsync(long batchId, CancellationToken cancellationToken = default)
    {
        var job = await context.HawalaImportJobs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BatchId == batchId && x.Status == "Failed", cancellationToken)
            ?? throw new InvalidOperationException("فایل ناموفق برای تلاش مجدد پیدا نشد.");
        var request = JsonSerializer.Deserialize<ConfirmHawalaImportDto>(job.RequestJson)
            ?? throw new InvalidOperationException("تنظیمات ثبت فایل پیدا نشد.");
        await EnqueueAsync(request, cancellationToken);
    }

    private static Task<HawalaImportBatch?> LockBatchAsync(ApplicationDbContext database, long batchId, CancellationToken cancellationToken) =>
        database.HawalaImportBatches
            .FromSqlInterpolated($"SELECT * FROM dbo.HawalaImportBatches WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {database.CurrentTenantId} AND Id = {batchId}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    /// <summary>Called only by the worker in a fresh scope with the persisted tenant/user identity.</summary>
    public async Task<bool> ProcessAsync(long jobId, CancellationToken cancellationToken)
    {
        // A database-wide session lock also serializes different IIS processes during recycle.
        // No transaction or row locks are held by this lease connection.
        await using var lease = new SqlConnection(context.Database.GetConnectionString());
        await lease.OpenAsync(cancellationToken);
        await using var acquire = lease.CreateCommand();
        acquire.CommandText = "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'HawalaImportWorker:v1', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0; SELECT @r;";
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync(cancellationToken)) < 0) return false;
        try
        {
            var job = await context.HawalaImportJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
            if (job is null || job.Status is not ("Queued" or "Running")) return false;
            var posted = await context.HawalaImportBatches.AnyAsync(x => x.Id == job.BatchId && x.Status == "Posted", cancellationToken);
            if (posted)
            {
                await context.HawalaImportJobs.Where(x => x.Id == jobId).ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.Status, "Completed").SetProperty(x => x.ProgressPercent, 100)
                    .SetProperty(x => x.ProgressMessage, "ثبت گروهی تکمیل شد.")
                    .SetProperty(x => x.CompletedAt, DateTime.UtcNow).SetProperty(x => x.ErrorMessage, (string?)null), cancellationToken);
                return true;
            }
            // Operational metadata must be writable even when a tenant is suspended:
            // otherwise its oldest request would starve every other tenant in the global queue.
            // Normal subscription enforcement still applies to all financial posting below.
            await context.HawalaImportJobs.Where(x => x.Id == jobId).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, "Running").SetProperty(x => x.Attempts, x => x.Attempts + 1)
                .SetProperty(x => x.StartedAt, DateTime.UtcNow).SetProperty(x => x.ErrorMessage, (string?)null), cancellationToken);
            try
            {
                if (job.RequestedBy != context.CurrentUserId ||
                    !await context.Users.AnyAsync(x => x.Id == job.RequestedBy && x.IsActive, cancellationToken) ||
                    !await context.Tenants.AnyAsync(x => x.Id == context.CurrentTenantId && x.IsActive && !x.IsArchived, cancellationToken))
                    throw new InvalidOperationException("کاربر یا صرافی ثبت‌کننده دیگر فعال نیست.");
                var request = JsonSerializer.Deserialize<ConfirmHawalaImportDto>(job.RequestJson)
                    ?? throw new InvalidOperationException("تنظیمات ثبت گروهی قابل خواندن نیست.");
                if (request.BatchId != job.BatchId) throw new InvalidOperationException("شناسه فایل با درخواست ثبت مطابقت ندارد.");
                if (importService is not HawalaImportService service)
                    throw new InvalidOperationException("سرویس ثبت گروهی آماده نیست.");
                await service.ConfirmQueuedAsync(request, new JobProgress(contextFactory, job.Id), cancellationToken);
            }
            catch (Exception error)
            {
                // Confirm rolls back all hawalas/ledger entries before we publish failure.
                context.ChangeTracker.Clear();
                var shuttingDown = error is OperationCanceledException && cancellationToken.IsCancellationRequested;
                if (!shuttingDown) logger?.LogError(error, "Hawala import failed. Tenant {TenantId}, batch {BatchId}, job {JobId}.", context.CurrentTenantId, job.BatchId, job.Id);
                var message = shuttingDown ? "ثبت متوقف شد؛ پس از راه‌اندازی دوباره ادامه می‌یابد."
                    : error is InvalidOperationException ? error.Message
                    : $"ثبت انجام نشد ({error.GetType().Name}{(error is SqlException sql ? $", SQL {sql.Number}" : "")}). دوباره تلاش کنید؛ در صورت تکرار با پشتیبانی تماس بگیرید.";
                message = message[..Math.Min(message.Length, 2000)];
                await context.HawalaImportJobs.Where(x => x.Id == jobId && x.Status == "Running")
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.Status, shuttingDown ? "Queued" : "Failed")
                        .SetProperty(x => x.ErrorMessage, shuttingDown ? null : message)
                        .SetProperty(x => x.ProgressMessage, shuttingDown ? message : "ثبت ناموفق؛ هیچ حواله‌ای از این تلاش ثبت نشد."), CancellationToken.None);
                if (shuttingDown) throw;
            }
            return true;
        }
        finally
        {
            // Closing pooled connections does not guarantee a session app-lock is released.
            await using var release = lease.CreateCommand();
            release.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'HawalaImportWorker:v1', @LockOwner=N'Session';";
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private sealed class JobProgress(IDbContextFactory<ApplicationDbContext> factory, long jobId) : IProgress<HawalaImportProgressDto>
    {
        public void Report(HawalaImportProgressDto value)
        {
            // Deliberately synchronous: no fire-and-forget tasks or shared DbContext use.
            // Job rows are separate from the batch row locked by the import transaction.
            if (value.Percent >= 100) return; // Completion is committed atomically with the import.
            try
            {
                using var progressContext = factory.CreateDbContext();
                progressContext.Database.SetCommandTimeout(5);
                progressContext.HawalaImportJobs.Where(x => x.Id == jobId && x.Status == "Running")
                    .ExecuteUpdate(update => update.SetProperty(x => x.ProgressPercent, value.Percent)
                        .SetProperty(x => x.ProgressMessage, value.Message));
            }
            catch (SqlException) { /* A progress refresh failure must not cancel financial posting. */ }
        }
    }
}
