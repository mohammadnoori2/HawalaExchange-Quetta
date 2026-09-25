using ClosedXML.Excel;
using AutoMapper;
using System.Diagnostics;
using System.Text.Json;
using HawalaExchange.Application.DTOs;
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

namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class HawalaImportStagingPerformanceTests(
    SqlServerPerformanceFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Preview_bulk_stages_7_8_and_9_column_rows_and_validates_duplicates()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        await using var workbook = CreateWorkbook([
            [99_100_001, "STG-1", "Sender 1", "Receiver 1", fixture.OwnLocation.Name, 1_000, "USD"],
            [99_100_002, "STG-2", "Sender 2", "Receiver 2", fixture.RemoteLocation.Name, 2_000, "USD", 25],
            [99_100_003, "STG-3", "Sender 3", "Receiver 3", fixture.RemoteLocation.Name, 3_000, "USD", 30, "USD"],
            [99_100_003, "STG-4", "Sender 4", "Receiver 4", fixture.RemoteLocation.Name, 4_000, "USD"]
        ]);

        var preview = await fixture.CreateImportService(context)
            .PreviewAsync(workbook, "staging-columns.xlsx", fixture.SourceCorrespondent.Id);

        Assert.Equal(4, preview.RowCount);
        Assert.Equal(2, preview.InvalidRowCount);
        Assert.Contains(preview.Rows, x => x.ExcelRowNumber == 3 &&
            x.ValidationErrors!.Contains("شماره حواله در همین فایل تکراری است"));
        var defaultedCommission = Assert.Single(preview.Rows, x => x.ExcelRowNumber == 2);
        Assert.Equal(25, defaultedCommission.AgentCommissionAmount);
        Assert.Equal(2, defaultedCommission.AgentCommissionCurrencyId);
        Assert.Equal("USD", defaultedCommission.AgentCommissionCurrencyCode);
        Assert.Empty(context.ChangeTracker.Entries<HawalaImportRow>());
    }

    [Fact]
    public async Task Preview_and_confirm_preserve_paired_hawala_and_ledger_accounting()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var destinationLocation = await EnsureDestinationLocationAsync(context);
        await using var workbook = CreateWorkbook([
            [92_000_001, "STG-C-1", "Sender 1", "Receiver 1", fixture.OwnLocation.Name, 1_000, "USD"],
            [92_000_002, "STG-C-2", "Sender 2", "Receiver 2", destinationLocation.Name, 2_000, "USD", 25]
        ]);
        var service = fixture.CreateImportService(context);

        var preview = await service.PreviewAsync(
            workbook, "staging-confirm.xlsx", fixture.SourceCorrespondent.Id);
        Assert.Equal(0, preview.InvalidRowCount);
        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto
        {
            BatchId = preview.BatchId,
            Commissions = preview.Rows.Where(x => x.RequiresOutgoingHawala).Select(x =>
                new HawalaImportCommissionDto
                {
                    RowId = x.Id, Amount = x.AgentCommissionAmount, CurrencyId = x.AgentCommissionCurrencyId
                }).ToList()
        });

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.GeneratedSendCount);
        context.ChangeTracker.Clear();
        var rows = await context.HawalaImportRows.AsNoTracking()
            .Where(x => x.BatchId == preview.BatchId).ToListAsync();
        Assert.All(rows, x => Assert.NotNull(x.HawalaId));
        var generatedId = Assert.Single(rows, x => x.GeneratedSendHawalaId.HasValue).GeneratedSendHawalaId!.Value;
        var generated = await context.Hawalas.AsNoTracking().SingleAsync(x => x.Id == generatedId);
        Assert.Equal(25, generated.AgentCommissionAmount);
        Assert.Equal(2, generated.AgentCommissionCurrencyId);
        var relatedIds = rows.SelectMany(x => new long?[] { x.HawalaId, x.GeneratedSendHawalaId })
            .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var ledger = await context.LedgerEntries.AsNoTracking()
            .Where(x => x.HawalaId.HasValue && relatedIds.Contains(x.HawalaId.Value)).ToListAsync();
        Assert.Equal(0, ledger.Sum(x => x.TalabKar - x.BadehKar));

        workbook.Position = 0;
        var duplicateFile = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(
            workbook, "staging-confirm-copy.xlsx", fixture.SourceCorrespondent.Id));
        Assert.Contains("قبلاً", duplicateFile.Message);
    }

    [Fact]
    public async Task Imported_receive_and_paired_send_commissions_reconcile_with_each_other_and_the_ledger()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var locationName = $"Commission place {Guid.NewGuid():N}";
        var destinationLocation = new PaymentLocation
        {
            Name = locationName,
            NormalizedName = PaymentLocationNameNormalizer.Normalize(locationName),
            Address = "Test", CreatedBy = fixture.UserId
        };
        context.PaymentLocations.Add(destinationLocation);
        var testId = Guid.NewGuid().ToString("N")[..12];
        var source = new Correspondent
        {
            Code = $"FLOW-S-{testId}", Name = $"Flow source {testId}",
            CommissionMethod = "PeriodicPerLakh", SettlementCurrencyId = 2
        };
        var destination = new Correspondent
        {
            Code = $"FLOW-D-{testId}", Name = $"Flow destination {testId}",
            CommissionMethod = "PeriodicPerLakh", SettlementCurrencyId = 2
        };
        context.Correspondents.AddRange(source, destination);
        await context.SaveChangesAsync();
        context.Accounts.AddRange(
            new Account
            {
                AccountCode = $"FLOW-S-ACC-{testId}", AccountName = "Flow source account",
                AccountType = "Correspondent", CorrespondentId = source.Id
            },
            new Account
            {
                AccountCode = $"FLOW-D-ACC-{testId}", AccountName = "Flow destination account",
                AccountType = "Correspondent", CorrespondentId = destination.Id
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        {
            var number = Random.Shared.NextInt64(300_000_000, 800_000_000);
            await using var workbook = CreateWorkbook([
                [number, $"FLOW-USD-{Guid.NewGuid():N}", "Sender USD", "Receiver USD",
                    fixture.OwnLocation.Name, 100_000, "USD"],
                [number + 1, $"FLOW-AFN-{Guid.NewGuid():N}", "Sender AFN", "Receiver AFN",
                    destinationLocation.Name, 300_000, "AFN"]
            ]);
            var importService = fixture.CreateImportService(context);
            var importPreview = await importService.PreviewAsync(
                workbook, "commission-flow.xlsx", source.Id);
            Assert.Equal(0, importPreview.InvalidRowCount);
            var imported = await importService.ConfirmAsync(new ConfirmHawalaImportDto
            {
                BatchId = importPreview.BatchId,
                OwnPaymentLocationName = fixture.OwnLocation.Name,
                LocationMappings = [new HawalaImportLocationMappingDto
                {
                    PaymentLocationName = destinationLocation.Name,
                    CorrespondentId = destination.Id
                }]
            });
            Assert.Equal(2, imported.ImportedCount);
            Assert.Equal(1, imported.GeneratedSendCount);
            context.ChangeTracker.Clear();

            var importedHawalas = await context.Hawalas.AsNoTracking()
                .Where(x => x.Number == number || x.Number == number + 1)
                .ToListAsync();
            var received = importedHawalas.Where(x => x.HawalaType == "HawalaReceive").ToList();
            var outgoing = Assert.Single(importedHawalas, x => x.HawalaType == "HawalaSend");
            Assert.Equal(2, received.Count);
            Assert.Equal(destination.Id, outgoing.CorrespondentId);
            Assert.Equal(destinationLocation.Id, outgoing.PaymentLocationId);
            Assert.Contains(received, x => x.Id == outgoing.SourceHawalaId);
            Assert.Null(outgoing.AgentCommissionAmount);

            var day = outgoing.CreatedAt.ToLocalTime().Date;
            context.DailyCommissionRates.Add(new DailyCommissionRate
            {
                RateDate = day, UsdToAfnRate = 70m, CreatedBy = fixture.UserId
            });
            context.CorrespondentDailyCommissionRates.Add(new CorrespondentDailyCommissionRate
            {
                CorrespondentId = source.Id,
                RateDate = day, UsdToAfnRate = 66m, CreatedBy = fixture.UserId
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var incomingRequest = new CorrespondentCommissionPreviewRequestDto
            {
                CorrespondentId = source.Id,
                HawalaType = "HawalaReceive", PeriodFrom = day, PeriodTo = day,
                CommissionPerLakhAfn = 200m
            };
            var outgoingRequest = new CorrespondentCommissionPreviewRequestDto
            {
                CorrespondentId = destination.Id,
                HawalaType = "HawalaSend", PeriodFrom = day, PeriodTo = day,
                CommissionPerLakhAfn = 200m,
                PaymentLocationRates = [new PaymentLocationCommissionRateDto
                {
                    PaymentLocationId = destinationLocation.Id, PerLakhRate = 300m
                }]
            };
            var commissionService = fixture.CreateCommissionService(context);
            var incomingPreview = await commissionService.PreviewAsync(incomingRequest);
            var outgoingPreview = await commissionService.PreviewAsync(outgoingRequest);
            Assert.Equal(2, incomingPreview.HawalaCount);
            Assert.Equal(104_285.7143m, incomingPreview.TotalBaseAfn);
            Assert.Equal(209m, incomingPreview.TotalCommissionUsd);
            Assert.Equal(1, outgoingPreview.HawalaCount);
            Assert.Equal(900m, outgoingPreview.TotalCommissionAfn);
            Assert.Equal(13.64m, outgoingPreview.TotalBaseAfn);
            Assert.Equal(66m, outgoingPreview.Items[0].SourceToAfnRate);
            Assert.Equal(300m, outgoingPreview.Items[0].PerLakhRate);

            var incomingBatch = await commissionService.PostAsync(incomingRequest);
            var outgoingBatch = await commissionService.PostAsync(outgoingRequest);
            context.ChangeTracker.Clear();
            foreach (var batchId in new[] { incomingBatch.Id, outgoingBatch.Id })
            {
                var transactionId = await context.CorrespondentCommissionBatches.AsNoTracking()
                    .Where(x => x.Id == batchId).Select(x => x.PostingTransactionId)
                    .SingleAsync();
                var ledger = await context.LedgerEntries.AsNoTracking()
                    .Where(x => x.TransactionId == transactionId).ToListAsync();
                Assert.NotEmpty(ledger);
                Assert.All(ledger.GroupBy(x => x.CurrencyId), group =>
                    Assert.Equal(group.Sum(x => x.TalabKar), group.Sum(x => x.BadehKar)));
            }
            Assert.Equal(0, (await commissionService.PreviewAsync(incomingRequest)).HawalaCount);
            Assert.Equal(0, (await commissionService.PreviewAsync(outgoingRequest)).HawalaCount);

            await new PaymentLocationAssignmentService(context).AssignAsync(
                destinationLocation.Id, source.Id, day.AddDays(1));
            context.ChangeTracker.Clear();
            var originalOutgoing = await context.Hawalas.AsNoTracking()
                .SingleAsync(x => x.Id == outgoing.Id);
            Assert.Equal(destination.Id, originalOutgoing.CorrespondentId);
        }
    }

    [Fact]
    public async Task First_import_can_choose_own_location_and_map_different_payment_place_to_existing_correspondent()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var setting = await context.CompanySettings.SingleAsync();
        var previousOwnId = setting.OwnPaymentLocationId;
        setting.OwnPaymentLocationId = null;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        try
        {
            var locationName = $"Mapped Place {Guid.NewGuid():N}";
            var location = new PaymentLocation
            {
                Name = locationName,
                NormalizedName = HawalaExchange.Application.Services.PaymentLocationNameNormalizer.Normalize(locationName),
                Address = "Test",
                CreatedBy = fixture.UserId
            };
            var secondName = $"Second Place {Guid.NewGuid():N}";
            var secondLocation = new PaymentLocation
            {
                Name = secondName,
                NormalizedName = HawalaExchange.Application.Services.PaymentLocationNameNormalizer.Normalize(secondName),
                Address = "Test",
                CreatedBy = fixture.UserId
            };
            context.PaymentLocations.AddRange(location, secondLocation);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var number = Random.Shared.NextInt64(200_000_000, 900_000_000);
            await using var workbook = CreateWorkbook([
                [number, $"MAP-OWN-{Guid.NewGuid():N}", "Sender 1", "Receiver 1", fixture.OwnLocation.Name, 1_000, "USD"],
                [number + 1, $"MAP-REMOTE-{Guid.NewGuid():N}", "Sender 2", "Receiver 2", locationName, 2_000, "USD"],
                [number + 2, $"MAP-SECOND-{Guid.NewGuid():N}", "Sender 3", "Receiver 3", secondName, 3_000, "USD"]
            ]);
            var service = fixture.CreateImportService(context);
            var preview = await service.PreviewAsync(workbook, "mapping.xlsx", fixture.SourceCorrespondent.Id);
            Assert.Null(preview.OwnPaymentLocationId);
            Assert.Contains(preview.PaymentLocations, x => x.Name == locationName &&
                !x.ResponsibleCorrespondentId.HasValue);

            var result = await service.ConfirmAsync(new ConfirmHawalaImportDto
            {
                BatchId = preview.BatchId,
                OwnPaymentLocationName = fixture.OwnLocation.Name,
                LocationMappings =
                [
                    new HawalaImportLocationMappingDto
                    {
                        PaymentLocationName = locationName,
                        CorrespondentId = fixture.DestinationCorrespondent.Id
                    },
                    new HawalaImportLocationMappingDto
                    {
                        PaymentLocationName = secondName,
                        CorrespondentId = fixture.DestinationCorrespondent.Id
                    }
                ]
            });
            Assert.Equal(3, result.ImportedCount);
            Assert.Equal(2, result.GeneratedSendCount);

            var assignments = await context.PaymentLocationCorrespondentAssignments.AsNoTracking()
                .Where(x => x.PaymentLocationId == location.Id || x.PaymentLocationId == secondLocation.Id)
                .ToListAsync();
            Assert.Equal(2, assignments.Count);
            Assert.All(assignments, x => Assert.Equal(fixture.DestinationCorrespondent.Id, x.CorrespondentId));
            var outgoing = await context.Hawalas.AsNoTracking()
                .Where(x => x.HawalaType == "HawalaSend" && (x.Number == number + 1 || x.Number == number + 2))
                .ToListAsync();
            Assert.Equal(2, outgoing.Count);
            Assert.All(outgoing, x => Assert.Equal(fixture.DestinationCorrespondent.Id, x.CorrespondentId));
            var batch = await context.HawalaImportBatches.AsNoTracking().SingleAsync(x => x.Id == preview.BatchId);
            Assert.Equal(fixture.OwnLocation.Id, batch.OwnPaymentLocationId);
        }
        finally
        {
            await context.CompanySettings.ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.OwnPaymentLocationId, previousOwnId));
        }
    }

    [Fact]
    public async Task Confirm_creates_new_payment_location_with_selected_existing_correspondent()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var name = $"New Payment Place {Guid.NewGuid():N}";
        var number = Random.Shared.NextInt64(200_000_000, 900_000_000);
        await using var workbook = CreateWorkbook([
            [number, $"NEW-LOC-{Guid.NewGuid():N}", "Sender", "Receiver", name, 1_000, "USD"]
        ]);
        var mapper = new MapperConfiguration(
            config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => fixture.CreateContext());
        var paymentLocations = new PaymentLocationService(
            context, factory.Object, mapper, Mock.Of<IAuditLogService>());
        var service = new HawalaImportService(
            context, fixture.CreateService(context), paymentLocations,
            Mock.Of<ICorrespondentService>(), Mock.Of<IAuditLogService>());

        var preview = await service.PreviewAsync(workbook, "new-location.xlsx", fixture.SourceCorrespondent.Id);
        Assert.Contains(name, preview.MissingLocations);
        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto
        {
            BatchId = preview.BatchId,
            LocationsToCreate = [name],
            OwnPaymentLocationName = fixture.OwnLocation.Name,
            LocationMappings = [new HawalaImportLocationMappingDto
            {
                PaymentLocationName = name,
                CorrespondentId = fixture.DestinationCorrespondent.Id
            }]
        });

        Assert.Equal(1, result.GeneratedSendCount);
        var createdLocation = await context.PaymentLocations.AsNoTracking().SingleAsync(x => x.Name == name);
        var assignment = await context.PaymentLocationCorrespondentAssignments.AsNoTracking()
            .SingleAsync(x => x.PaymentLocationId == createdLocation.Id);
        Assert.Equal(fixture.DestinationCorrespondent.Id, assignment.CorrespondentId);
    }

    [Fact]
    public async Task Confirm_creates_explicitly_approved_correspondent_for_payment_place()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var name = $"New Agent Place {Guid.NewGuid():N}";
        var location = new PaymentLocation
        {
            Name = name,
            NormalizedName = PaymentLocationNameNormalizer.Normalize(name),
            Address = "Test",
            CreatedBy = fixture.UserId
        };
        context.PaymentLocations.Add(location);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var mapper = new MapperConfiguration(
            config => config.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var correspondents = new CorrespondentService(
            context, mapper, fixture.CreateAccountService(context),
            Mock.Of<ILedgerService>(), Mock.Of<IAuditLogService>(),
            NullLogger<CorrespondentService>.Instance);
        var service = new HawalaImportService(
            context, fixture.CreateService(context), Mock.Of<IPaymentLocationService>(),
            correspondents, Mock.Of<IAuditLogService>());
        var number = Random.Shared.NextInt64(200_000_000, 900_000_000);
        await using var workbook = CreateWorkbook([
            [number, $"NEW-AGENT-{Guid.NewGuid():N}", "Sender", "Receiver", name, 1_000, "USD"]
        ]);
        var preview = await service.PreviewAsync(workbook, "new-agent.xlsx", fixture.SourceCorrespondent.Id);
        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto
        {
            BatchId = preview.BatchId,
            OwnPaymentLocationName = fixture.OwnLocation.Name,
            LocationMappings = [new HawalaImportLocationMappingDto
            {
                PaymentLocationName = name,
                CreateCorrespondent = true
            }]
        });

        Assert.Equal(1, result.GeneratedSendCount);
        var agent = await context.Correspondents.AsNoTracking().SingleAsync(x => x.Name == name);
        var assignment = await context.PaymentLocationCorrespondentAssignments.AsNoTracking()
            .SingleAsync(x => x.PaymentLocationId == location.Id);
        Assert.Equal(agent.Id, assignment.CorrespondentId);
        Assert.True(await context.Accounts.AnyAsync(x => x.CorrespondentId == agent.Id));
    }

    [Fact]
    public async Task Staging_procedure_detects_existing_number_and_reference()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        context.Hawalas.Add(new Hawala
        {
            Number = 92_500_001, HawalaType = "HawalaReceive",
            CorrespondentId = fixture.SourceCorrespondent.Id,
            PaymentLocationId = fixture.OwnLocation.Id,
            SenderName = "Existing", ReceiverName = "Existing",
            FromCurrencyId = 2, ToCurrencyId = 2, FromAmount = 100, ToAmount = 100,
            ReferenceNumber = "STG-EXISTING", Status = "Pending", CreatedBy = fixture.UserId
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await using var workbook = CreateWorkbook([
            [92_500_001, "STG-NEW", "Sender 1", "Receiver 1", fixture.OwnLocation.Name, 1_000, "USD"],
            [92_500_002, "STG-EXISTING", "Sender 2", "Receiver 2", fixture.OwnLocation.Name, 2_000, "USD"]
        ]);

        var preview = await fixture.CreateImportService(context).PreviewAsync(
            workbook, "staging-existing.xlsx", fixture.SourceCorrespondent.Id);

        Assert.Equal(2, preview.InvalidRowCount);
        Assert.Contains(preview.Rows, x => x.ValidationErrors!.Contains("شماره حواله قبلاً"));
        Assert.Contains(preview.Rows, x => x.ValidationErrors!.Contains("رفرنس قبلاً"));
    }

    [Fact]
    public async Task Preview_and_confirm_allow_reusing_incoming_number_and_reference_after_period_is_closed()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        const long hawalaNumber = 92_600_001;
        const string reference = "STG-OLD-PERIOD-REFERENCE";
        var periodEnd = await AddClosedPeriodAsync(context, fixture.SourceCorrespondent.Id);
        context.Hawalas.Add(new Hawala
        {
            Number = hawalaNumber,
            HawalaType = "HawalaReceive",
            CorrespondentId = fixture.SourceCorrespondent.Id,
            PaymentLocationId = fixture.OwnLocation.Id,
            SenderName = "Old sender",
            ReceiverName = "Old receiver",
            FromCurrencyId = 2,
            ToCurrencyId = 2,
            FromAmount = 100,
            ToAmount = 100,
            ReferenceNumber = reference,
            Status = "Paid",
            CreatedBy = fixture.UserId,
            CreatedAt = periodEnd.AddMinutes(-1)
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await using var workbook = CreateWorkbook([
            [hawalaNumber, reference, "New sender", "New receiver", fixture.OwnLocation.Name, 1_000, "USD"]
        ]);
        var service = fixture.CreateImportService(context);

        var preview = await service.PreviewAsync(
            workbook, "staging-reused-reference.xlsx", fixture.SourceCorrespondent.Id);
        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto { BatchId = preview.BatchId });

        Assert.Equal(0, preview.InvalidRowCount);
        Assert.Equal(1, result.ImportedCount);
    }

    [Fact]
    public async Task Confirm_revalidates_many_outgoing_rows_with_bounded_database_queries()
    {
        const int rowCount = 100;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var destinationLocation = await EnsureDestinationLocationAsync(context);
        var rows = Enumerable.Range(0, rowCount).Select(index => new object[]
        {
            93_000_000L + index, $"STG-B-{index}", $"Sender {index}", $"Receiver {index}",
            destinationLocation.Name, 1_000m + index, "USD"
        }).ToList();
        await using var workbook = CreateWorkbook(rows);
        var service = fixture.CreateImportService(context);
        var preview = await service.PreviewAsync(workbook, "staging-bounded.xlsx", fixture.SourceCorrespondent.Id);

        fixture.Commands.Reset();
        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto { BatchId = preview.BatchId });

        Assert.Equal(rowCount, result.ImportedCount);
        Assert.Equal(rowCount, result.GeneratedSendCount);
        Assert.Equal(rowCount, result.MissingCommissionCount);
        Assert.InRange(fixture.Commands.Count, 1, 30);
    }

    [Fact]
    public async Task Confirm_allows_reusing_outgoing_number_after_destination_period_is_closed()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var destinationLocation = await EnsureDestinationLocationAsync(context);
        const long hawalaNumber = 95_100_001;
        await using var workbook = CreateWorkbook([
            [hawalaNumber, "STG-PERIOD-OLD", "Sender", "Receiver", destinationLocation.Name, 1_000, "USD"]
        ]);
        var service = fixture.CreateImportService(context);
        var preview = await service.PreviewAsync(
            workbook, "staging-period-old.xlsx", fixture.SourceCorrespondent.Id);

        var periodEnd = await AddClosedPeriodAsync(context, fixture.DestinationCorrespondent.Id);
        context.Hawalas.Add(CreateExistingOutgoing(hawalaNumber, periodEnd.AddMinutes(-1), "OLD-PERIOD"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await service.ConfirmAsync(new ConfirmHawalaImportDto { BatchId = preview.BatchId });

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.GeneratedSendCount);
    }

    [Fact]
    public async Task Confirm_rejects_duplicate_outgoing_number_in_destination_current_period()
    {
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var destinationLocation = await EnsureDestinationLocationAsync(context);
        const long hawalaNumber = 95_200_001;
        await using var workbook = CreateWorkbook([
            [hawalaNumber, "STG-PERIOD-CURRENT", "Sender", "Receiver", destinationLocation.Name, 1_000, "USD"]
        ]);
        var service = fixture.CreateImportService(context);
        var preview = await service.PreviewAsync(
            workbook, "staging-period-current.xlsx", fixture.SourceCorrespondent.Id);

        var periodEnd = await AddClosedPeriodAsync(context, fixture.DestinationCorrespondent.Id);
        context.Hawalas.Add(CreateExistingOutgoing(hawalaNumber, periodEnd.AddMinutes(1), "CURRENT-PERIOD"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmAsync(new ConfirmHawalaImportDto { BatchId = preview.BatchId }));

        Assert.Contains("نمبر حواله ارسالی", error.Message);
        Assert.Contains("قبلاً", error.Message);
    }

    [Fact]
    public async Task Measure_bulk_staging_for_10000_rows()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_HAWALA_PERF"), "1", StringComparison.Ordinal))
        {
            output.WriteLine("Import staging performance sample was not run. Set RUN_HAWALA_PERF=1 to enable it.");
            return;
        }

        const int rowCount = 10_000;
        const long numberBase = 94_000_000;
        await using var context = fixture.CreateContext();
        using var bypass = context.BypassSubscriptionEnforcement();
        await ConfigureOwnLocationAsync(context);
        var legacyBatch = new HawalaImportBatch
        {
            CorrespondentId = fixture.SourceCorrespondent.Id,
            OwnPaymentLocationId = fixture.OwnLocation.Id,
            FileName = "legacy-staging.xlsx",
            FileHash = new string('A', 64),
            Status = "Preview",
            RowCount = rowCount,
            CreatedBy = fixture.UserId,
            Rows = Enumerable.Range(0, rowCount).Select(index => new HawalaImportRow
            {
                ExcelRowNumber = index + 1,
                HawalaNumber = numberBase + index,
                ReferenceNumber = $"LEGACY-STG-{index}",
                SenderName = $"Sender {index}",
                ReceiverName = $"Receiver {index}",
                PaymentLocationText = fixture.OwnLocation.Name,
                PaymentLocationId = fixture.OwnLocation.Id,
                Amount = 1_000 + index,
                CurrencyCode = "USD",
                CurrencyId = 2
            }).ToList()
        };
        var legacyWatch = Stopwatch.StartNew();
        context.HawalaImportBatches.Add(legacyBatch);
        await context.SaveChangesAsync();
        legacyWatch.Stop();
        context.ChangeTracker.Clear();

        var workbookRows = Enumerable.Range(0, rowCount).Select(index => new object[]
        {
            numberBase + rowCount + index, $"BULK-STG-{index}", $"Sender {index}",
            $"Receiver {index}", fixture.OwnLocation.Name, 1_000m + index, "USD"
        }).ToList();
        await using var workbook = CreateWorkbook(workbookRows);
        var bulkWatch = Stopwatch.StartNew();
        var preview = await fixture.CreateImportService(context).PreviewAsync(
            workbook, "bulk-staging.xlsx", fixture.SourceCorrespondent.Id);
        bulkWatch.Stop();
        var confirmWatch = Stopwatch.StartNew();
        var result = await fixture.CreateImportService(context).ConfirmAsync(
            new ConfirmHawalaImportDto { BatchId = preview.BatchId });
        confirmWatch.Stop();

        Assert.Equal(rowCount, preview.ValidRowCount);
        Assert.Equal(rowCount, result.ImportedCount);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            rows = rowCount,
            legacyEfPersistenceMilliseconds = Math.Round(legacyWatch.Elapsed.TotalMilliseconds, 2),
            bulkEndToEndMilliseconds = Math.Round(bulkWatch.Elapsed.TotalMilliseconds, 2),
            confirmAndAccountingMilliseconds = Math.Round(confirmWatch.Elapsed.TotalMilliseconds, 2),
            improvementPercent = Math.Round((1 - bulkWatch.Elapsed.TotalMilliseconds /
                legacyWatch.Elapsed.TotalMilliseconds) * 100, 2)
        }));
    }

    private async Task ConfigureOwnLocationAsync(HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
    {
        var setting = await context.CompanySettings.SingleOrDefaultAsync();
        if (setting == null)
        {
            setting = new CompanySetting { CompanyName = "Performance Test" };
            context.CompanySettings.Add(setting);
        }
        setting.OwnPaymentLocationId = fixture.OwnLocation.Id;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private async Task<PaymentLocation> EnsureDestinationLocationAsync(
        HawalaExchange.Infrastructure.Data.ApplicationDbContext context)
    {
        var normalized = HawalaExchange.Application.Services.PaymentLocationNameNormalizer.Normalize(
            fixture.DestinationCorrespondent.Name);
        var existing = await context.PaymentLocations.FirstOrDefaultAsync(x => x.NormalizedName == normalized);
        if (existing != null)
            return existing;
        var location = new PaymentLocation
        {
            Name = fixture.DestinationCorrespondent.Name, NormalizedName = normalized,
            Address = "Remote", IsActive = true, CreatedBy = fixture.UserId
        };
        context.PaymentLocations.Add(location);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return location;
    }

    private async Task<DateTime> AddClosedPeriodAsync(
        HawalaExchange.Infrastructure.Data.ApplicationDbContext context,
        long correspondentId)
    {
        var latestPeriod = await context.CorrespondentAccountPeriods
            .Where(x => x.CorrespondentId == correspondentId)
            .OrderByDescending(x => x.PeriodNumber)
            .Select(x => new { x.PeriodNumber, x.PeriodTo })
            .FirstOrDefaultAsync();
        var periodEnd = latestPeriod == null
            ? DateTime.UtcNow.AddMinutes(-5)
            : latestPeriod.PeriodTo.AddMinutes(5);
        context.CorrespondentAccountPeriods.Add(new CorrespondentAccountPeriod
        {
            CorrespondentId = correspondentId,
            PeriodNumber = (latestPeriod?.PeriodNumber ?? 0) + 1,
            PeriodFrom = latestPeriod?.PeriodTo ?? periodEnd.AddDays(-1),
            PeriodTo = periodEnd,
            ClosedAt = periodEnd,
            ClosedBy = fixture.UserId,
            Note = "Bulk import period validation test"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return periodEnd;
    }

    private Hawala CreateExistingOutgoing(long number, DateTime createdAt, string reference) => new()
    {
        Number = number,
        HawalaType = "HawalaSend",
        CorrespondentId = fixture.DestinationCorrespondent.Id,
        PaymentLocationId = fixture.RemoteLocation.Id,
        SenderName = "Existing sender",
        ReceiverName = "Existing receiver",
        FromCurrencyId = 2,
        ToCurrencyId = 2,
        FromAmount = 100,
        ToAmount = 100,
        ReferenceNumber = reference,
        Status = "Pending",
        CreatedBy = fixture.UserId,
        CreatedAt = createdAt
    };

    private static MemoryStream CreateWorkbook(IReadOnlyList<object[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Hawalas");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            sheet.Cell(rowIndex + 1, columnIndex + 1).Value = XLCellValue.FromObject(rows[rowIndex][columnIndex]);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
