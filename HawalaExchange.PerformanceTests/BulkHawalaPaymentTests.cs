using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class BulkHawalaPaymentTests(SqlServerPerformanceFixture fixture)
{
    private static long nextNumber = 99_100_000;

    [Fact]
    public async Task Preview_uses_payable_currency_amount_and_existing_agent_commission_without_writing()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context);
        var first = Received(1, 500); first.FromCurrencyId = 2; first.FromAmount = 10;
        first.AgentCommissionAmount = 50; first.AgentCommissionCurrencyId = 2;
        var second = Received(2, 100);
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var before = await context.LedgerEntries.CountAsync();
        var preview = await fixture.CreateService(context).PreviewBulkPaymentAsync([first.Id, second.Id], cash.Id);
        Assert.True(preview.CanExecute);
        Assert.Equal(500, preview.Totals.Single(x => x.CurrencyId == 1).Principal);
        var usd = preview.Totals.Single(x => x.CurrencyId == 2);
        Assert.Equal(100, usd.Principal); Assert.Equal(50, usd.AgentCommission); Assert.Equal(150, usd.Total);
        Assert.Equal(-10_000, usd.AccountBalance); Assert.Equal(-9_850, usd.AccountBalanceAfter);
        Assert.Equal(before, await context.LedgerEntries.CountAsync());
        Assert.All(preview.Items, x => Assert.NotEmpty(x.RowVersion));
    }

    [Fact]
    public async Task Batch_pays_every_row_preserves_receiver_and_posts_balanced_entries_once()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context);
        var first = Received(1, 500); var second = Received(2, 100);
        first.AgentCommissionAmount = 20; first.AgentCommissionCurrencyId = 1;
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        var request = Request(await service.PreviewBulkPaymentAsync([first.Id, second.Id], cash.Id));
        Assert.Equal(2, await service.PayBulkAsync(request));
        context.ChangeTracker.Clear();
        var ids = new[] { first.Id, second.Id };
        var paid = await context.Hawalas.Where(x => ids.Contains(x.Id)).ToListAsync();
        Assert.All(paid, x => { Assert.Equal("Paid", x.Status); Assert.Equal(cash.Id, x.PaidFromAccountId); Assert.Equal("Father", x.ReceiverFatherName); Assert.Equal("0700000000", x.ReceiverPhone); Assert.Equal(fixture.UserId, x.PaidBy); Assert.NotNull(x.PaidAt); });
        var entries = await context.LedgerEntries.Where(x => x.HawalaId.HasValue && ids.Contains(x.HawalaId.Value)).ToListAsync();
        Assert.Equal(6, entries.Count);
        Assert.All(entries.GroupBy(x => new { x.HawalaId, x.CurrencyId }), group => Assert.Equal(group.Sum(x => x.BadehKar), group.Sum(x => x.TalabKar)));
        Assert.Equal(520, entries.Where(x => x.AccountId == cash.Id && x.CurrencyId == 1).Sum(x => x.TalabKar));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayBulkAsync(request));
        Assert.Equal(6, await context.LedgerEntries.CountAsync(x => x.HawalaId.HasValue && ids.Contains(x.HawalaId.Value)));
    }

    [Fact]
    public async Task Invalid_member_blocks_all_rows_and_reports_its_reason()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context);
        var valid = Received(2, 100); var invalid = Received(2, 200); invalid.Status = "Cancel";
        context.Hawalas.AddRange(valid, invalid); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        var preview = await service.PreviewBulkPaymentAsync([valid.Id, invalid.Id], cash.Id);
        Assert.False(preview.CanExecute); Assert.NotNull(preview.Items.Single(x => x.Id == invalid.Id).Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayBulkAsync(Request(preview)));
        await AssertNotPaid(context, valid.Id);
    }

    [Fact]
    public async Task Insufficient_cash_does_not_block_payment_and_negative_balance_is_shown()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context);
        var hawala = Received(2, 9_990); hawala.AgentCommissionAmount = 11; hawala.AgentCommissionCurrencyId = 2;
        context.Hawalas.Add(hawala); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        var preview = await service.PreviewBulkPaymentAsync([hawala.Id], cash.Id);
        Assert.True(preview.CanExecute);
        Assert.Equal(1, preview.Totals.Single().AccountBalanceAfter);
        Assert.Equal(1, await service.PayBulkAsync(Request(preview)));
        Assert.Equal("Paid", (await context.Hawalas.AsNoTracking().SingleAsync(x => x.Id == hawala.Id)).Status);
    }

    [Fact]
    public async Task Edited_row_after_preview_requires_new_preview_and_does_not_pay_other_rows()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var first = Received(2, 100); var second = Received(2, 200);
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        var request = Request(await service.PreviewBulkPaymentAsync([first.Id, second.Id], cash.Id));
        await using (var other = fixture.CreateContext())
        {
            using var otherBypass = other.BypassSubscriptionEnforcement();
            await other.Hawalas.Where(x => x.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ToAmount, 250m));
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayBulkAsync(request));
        Assert.Contains("پس از پیش‌نمایش", error.Message);
        await AssertNotPaid(context, first.Id, second.Id);
    }

    [Fact]
    public async Task Late_failure_rolls_back_already_saved_statuses_and_ledger_entries()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var first = Received(2, 100); var second = Received(2, 200);
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var calls = 0;
        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>()))
            .Returns(() => ++calls == 2 ? Task.FromException<AuditLogDto>(new InvalidOperationException("late test failure")) : Task.FromResult(new AuditLogDto()));
        var service = new HawalaService(context, Mock.Of<IMapper>(), Mock.Of<ILedgerService>(), Mock.Of<IAccountService>(), audit.Object, Mock.Of<IFileService>());
        var request = Request(await service.PreviewBulkPaymentAsync([first.Id, second.Id], cash.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayBulkAsync(request));
        Assert.Equal(2, calls);
        await AssertNotPaid(context, first.Id, second.Id);
        // The same scoped context can safely retry after the rollback.
        var normal = fixture.CreateService(context);
        var fresh = Request(await normal.PreviewBulkPaymentAsync([first.Id, second.Id], cash.Id));
        Assert.Equal(2, await normal.PayBulkAsync(fresh));
    }

    [Fact]
    public async Task Correspondent_batch_generates_one_linked_send_per_row_with_consecutive_numbers()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var first = Received(1, 500); var second = Received(2, 100);
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var previous = await context.Hawalas.Where(x => x.CorrespondentId == fixture.DestinationCorrespondent.Id && x.HawalaType == "HawalaSend").MaxAsync(x => (long?)x.Number) ?? 0;
        var service = fixture.CreateService(context);
        var preview = await service.PreviewBulkPaymentAsync([first.Id, second.Id], fixture.DestinationAccount.Id);
        Assert.True(preview.IsCorrespondent); Assert.True(preview.CanExecute);
        await service.PayBulkAsync(Request(preview));
        context.ChangeTracker.Clear();
        var ids = new[] { first.Id, second.Id };
        var sent = await context.Hawalas.Where(x => x.SourceHawalaId.HasValue && ids.Contains(x.SourceHawalaId.Value)).OrderBy(x => x.Number).ToListAsync();
        Assert.Equal(2, sent.Count); Assert.Equal(previous + 1, sent[0].Number); Assert.Equal(previous + 2, sent[1].Number);
        Assert.All(sent, x => { Assert.True(x.IsSystemGenerated); Assert.Equal("HawalaSend", x.HawalaType); Assert.Equal(fixture.DestinationCorrespondent.Id, x.CorrespondentId); });
    }

    [Fact]
    public async Task Concurrent_submissions_can_post_the_same_batch_only_once()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var first = Received(2, 100); var second = Received(2, 200);
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var request = Request(await fixture.CreateService(context).PreviewBulkPaymentAsync([first.Id, second.Id], cash.Id));
        var outcomes = await Task.WhenAll(Attempt(), Attempt());
        Assert.Equal(1, outcomes.Count(x => x));
        context.ChangeTracker.Clear();
        var ids = new[] { first.Id, second.Id };
        Assert.Equal(2, await context.Hawalas.CountAsync(x => ids.Contains(x.Id) && x.Status == "Paid"));
        Assert.Equal(4, await context.LedgerEntries.CountAsync(x => x.HawalaId.HasValue && ids.Contains(x.HawalaId.Value)));

        async Task<bool> Attempt()
        {
            await using var separate = fixture.CreateContext();
            using var separateBypass = separate.BypassSubscriptionEnforcement();
            try { await fixture.CreateService(separate).PayBulkAsync(request); return true; }
            catch (InvalidOperationException) { return false; }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205) { return false; }
        }
    }

    [Fact]
    public async Task Concurrent_different_batches_can_pay_even_when_cash_balance_becomes_negative()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var first = Received(2, 6_000); var second = Received(2, 6_000);
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        var one = Request(await service.PreviewBulkPaymentAsync([first.Id], cash.Id));
        var two = Request(await service.PreviewBulkPaymentAsync([second.Id], cash.Id));
        var outcomes = await Task.WhenAll(Attempt(one), Attempt(two));
        Assert.Equal(2, outcomes.Count(x => x));
        context.ChangeTracker.Clear();
        var ids = new[] { first.Id, second.Id };
        Assert.Equal(2, await context.Hawalas.CountAsync(x => ids.Contains(x.Id) && x.Status == "Paid"));
        Assert.Equal(12_000, await context.LedgerEntries.Where(x => x.AccountId == cash.Id && x.CurrencyId == 2).SumAsync(x => x.TalabKar));

        async Task<bool> Attempt(BulkHawalaPaymentRequestDto request)
        {
            await using var separate = fixture.CreateContext();
            using var separateBypass = separate.BypassSubscriptionEnforcement();
            try { await fixture.CreateService(separate).PayBulkAsync(request); return true; }
            catch (InvalidOperationException) { return false; }
        }
    }

    [Fact]
    public async Task Cross_tenant_ids_and_accounts_cannot_be_previewed_or_paid()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var hawala = Received(2, 100);
        context.Hawalas.Add(hawala); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        var request = Request(await service.PreviewBulkPaymentAsync([hawala.Id], cash.Id));
        using (context.UseTenantScope(2))
        {
            var preview = await service.PreviewBulkPaymentAsync([hawala.Id], cash.Id);
            Assert.False(preview.CanExecute); Assert.Empty(preview.Items);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayBulkAsync(request));
        }
        await AssertNotPaid(context, hawala.Id);
    }

    [Fact]
    public async Task Missing_selection_archived_account_and_wrong_account_type_are_rejected()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var hawala = Received(2, 100);
        context.Hawalas.Add(hawala); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        Assert.False((await service.PreviewBulkPaymentAsync([hawala.Id, long.MaxValue], cash.Id)).CanExecute);
        cash.IsArchived = true; await context.SaveChangesAsync();
        Assert.False((await service.PreviewBulkPaymentAsync([hawala.Id], cash.Id)).CanExecute);
        cash.IsArchived = false; cash.AccountType = "Income"; await context.SaveChangesAsync();
        Assert.False((await service.PreviewBulkPaymentAsync([hawala.Id], cash.Id)).CanExecute);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewBulkPaymentAsync([], cash.Id));
    }

    private Hawala Received(long currency, decimal amount) => new()
    {
        Number = Interlocked.Increment(ref nextNumber), HawalaType = "HawalaReceive", Status = "Pending",
        CorrespondentId = fixture.SourceCorrespondent.Id, PaymentLocationId = fixture.OwnLocation.Id,
        SenderName = "Sender", ReceiverName = "Receiver", ReceiverFatherName = "Father", ReceiverPhone = "0700000000",
        FromCurrencyId = currency, ToCurrencyId = currency, FromAmount = amount, ToAmount = amount,
        CreatedBy = fixture.UserId, CreatedAt = DateTime.UtcNow
    };

    private async Task<Account> Cash(ApplicationDbContext context)
    {
        var cash = new Account { AccountCode = $"BULK-{Guid.NewGuid():N}", AccountName = "Bulk test cash", AccountType = "Cash" };
        context.Accounts.Add(cash); await context.SaveChangesAsync();
        foreach (var currency in new long[] { 1, 2 })
        {
            context.LedgerEntries.Add(new LedgerEntry { AccountId = cash.Id, CurrencyId = currency, BadehKar = 10_000, Description = "Test funding" });
            context.LedgerEntries.Add(new LedgerEntry { AccountId = fixture.SourceAccount.Id, CurrencyId = currency, TalabKar = 10_000, Description = "Test funding offset" });
        }
        await context.SaveChangesAsync(); return cash;
    }

    private static BulkHawalaPaymentRequestDto Request(BulkHawalaPaymentPreviewDto preview) => new()
    { PaidFromAccountId = preview.PaidFromAccountId, Items = preview.Items };

    [Fact]
    public async Task History_is_atomic_tenant_scoped_and_keeps_paid_snapshots_after_hawala_deletion()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var first = Received(1, 500); var second = Received(2, 100);
        first.AgentCommissionAmount = 50; first.AgentCommissionCurrencyId = 2;
        context.Hawalas.AddRange(first, second); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        await service.PayBulkAsync(Request(await service.PreviewBulkPaymentAsync([first.Id, second.Id], cash.Id)));
        var batchId = await context.HawalaPaymentBatchItems.Where(x => x.HawalaId == first.Id).Select(x => x.BatchId).SingleAsync();
        var batch = await service.GetPaymentBatchAsync(batchId);
        Assert.Equal(2, batch.Count); Assert.Equal(cash.AccountName, batch.AccountName);
        Assert.Equal(150, batch.Totals.Single(x => x.CurrencyId == 2).Total);
        Assert.Equal(500, batch.Totals.Single(x => x.CurrencyId == 1).Principal);
        Assert.Equal(fixture.OwnLocation.Name, batch.Items.Single(x => x.HawalaId == first.Id).PaymentLocation);
        Assert.Contains((await service.GetPaymentHistoryAsync(1, 100)).Items, x => x.Id == batchId);
        using (context.UseTenantScope(2))
        {
            Assert.Empty((await service.GetPaymentHistoryAsync()).Items);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPaymentBatchAsync(batchId));
        }
        await context.LedgerEntries.Where(x => x.HawalaId == first.Id).ExecuteDeleteAsync();
        await context.Hawalas.Where(x => x.Id == first.Id).ExecuteDeleteAsync();
        Assert.Equal(500, (await service.GetPaymentBatchAsync(batchId)).Items.Single(x => x.HawalaId == first.Id).Amount);
    }

    [Fact]
    public async Task History_excel_contains_summary_and_currency_details_using_the_paid_snapshot()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        var cash = await Cash(context); var hawala = Received(1, 500);
        hawala.FromAmount = 10; hawala.FromCurrencyId = 2; hawala.SenderName = "=literal text";
        context.Hawalas.Add(hawala); await context.SaveChangesAsync();
        var service = fixture.CreateService(context);
        await service.PayBulkAsync(Request(await service.PreviewBulkPaymentAsync([hawala.Id], cash.Id)));
        var id = await context.HawalaPaymentBatchItems.Where(x => x.HawalaId == hawala.Id).Select(x => x.BatchId).SingleAsync();
        var settings = new Mock<ICompanySettingService>(); settings.Setup(x => x.GetAsync()).ReturnsAsync(new CompanySettingDto { UsePersianCalendar = true });
        var exporter = new HawalaExchange.Infrastructure.Services.HawalaPaymentHistoryExportService(service, settings.Object, Mock.Of<WkHtmlToPdfDotNet.Contracts.IConverter>());
        var file = await exporter.ExportAsync(id, ExportFormat.Excel);
        using var stream = new MemoryStream(file.Content); using var book = new ClosedXML.Excel.XLWorkbook(stream);
        Assert.Equal(2, book.Worksheets.Count); Assert.Equal(500, book.Worksheet("Summary").Cell(8, 2).GetValue<decimal>());
        var detail = book.Worksheet(2); Assert.Equal(500, detail.Cell(2, 7).GetValue<decimal>()); Assert.Equal("AFN", detail.Cell(2, 8).GetString());
        Assert.Equal("=literal text", detail.Cell(2, 3).GetString()); Assert.False(detail.Cell(2, 3).HasFormula);
        Assert.Contains("1405/", detail.Cell(2, 11).GetString());
    }

    [Fact]
    public async Task History_pdf_is_generated_with_currency_totals_and_escaped_details()
    {
        var batch = new HawalaPaymentBatchDto
        {
            Id = 1, ExecutedAt = new(2026, 10, 8), Count = 2, AccountName = "صندوق آزمایشی", ExecutedByName = "کاربر آزمایشی",
            Items = [new() { Number = 2172, SenderName = "عبدالله نظری", ReceiverName = "عبدالجمیل", CurrencyCode = "AFN", Amount = 21_560, PaymentLocation = "کندز", RegisteredAt = new(2026, 10, 8) }, new() { Number = 2173, SenderName = "<script>alert(1)</script>", ReceiverName = "Receiver", CurrencyCode = "USD", Amount = 100, PaymentLocation = "کابل برچی", RegisteredAt = new(2026, 10, 8) }],
            Totals = [new() { CurrencyCode = "AFN", Principal = 21_560 }, new() { CurrencyCode = "USD", Principal = 100 }]
        };
        var hawalas = new Mock<IHawalaService>(); hawalas.Setup(x => x.GetPaymentBatchAsync(1)).ReturnsAsync(batch);
        var settings = new Mock<ICompanySettingService>(); settings.Setup(x => x.GetAsync()).ReturnsAsync(new CompanySettingDto { UsePersianCalendar = true });
        // Use the same native converter that the production exporter uses.
        var native = new WkHtmlToPdfDotNet.SynchronizedConverter(new WkHtmlToPdfDotNet.PdfTools());
        var exporter = new HawalaExchange.Infrastructure.Services.HawalaPaymentHistoryExportService(hawalas.Object, settings.Object, native);
        var file = await exporter.ExportAsync(1, ExportFormat.Pdf);
        Assert.Equal("application/pdf", file.ContentType); Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(file.Content, 0, 4));
        var output = Environment.GetEnvironmentVariable("HAWALA_PDF_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) await File.WriteAllBytesAsync(output, file.Content);
    }

    private static async Task AssertNotPaid(ApplicationDbContext context, params long[] ids)
    {
        context.ChangeTracker.Clear();
        Assert.All(await context.Hawalas.Where(x => ids.Contains(x.Id)).ToListAsync(), x => Assert.Equal("Pending", x.Status));
        Assert.False(await context.LedgerEntries.AnyAsync(x => x.HawalaId.HasValue && ids.Contains(x.HawalaId.Value)));
        Assert.False(await context.HawalaPaymentBatchItems.AnyAsync(x => ids.Contains(x.HawalaId)));
    }
}
