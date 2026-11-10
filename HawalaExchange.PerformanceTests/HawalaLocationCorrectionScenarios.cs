using System.Text.Json;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaSystem.Mappings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HawalaExchange.PerformanceTests;

using Xunit;
public abstract class LocationCorrectionScenarios
{
    protected abstract ApplicationDbContext Context();
    protected static HawalaService Service(ApplicationDbContext db)
    {
        var mapper=new MapperConfiguration(c=>c.AddProfile<MappingProfile>(),NullLoggerFactory.Instance).CreateMapper();
        return new(db,mapper,Mock.Of<ILedgerService>(),Mock.Of<IAccountService>(),Mock.Of<IAuditLogService>(),Mock.Of<IFileService>());
    }
    protected sealed record Sample(long Root,long Child,long Source,long OldOwner,long NewOwner,long OldAccount,long NewAccount,long OldLocation,long NewLocation,long Currency,long Usd);
    protected static async Task<Sample> Seed(ApplicationDbContext db)
    {
        var key=Guid.NewGuid().ToString("N")[..10];
        var afn=await db.Currencies.Where(x=>x.Code=="AFN").Select(x=>x.Id).SingleAsync();
        var usd=await db.Currencies.Where(x=>x.Code=="USD").Select(x=>x.Id).SingleAsync();
        Correspondent Owner(string suffix)=>new(){Code="LC-"+key+suffix,Name="Location correction "+suffix,CommissionMethod="PeriodicPerLakh",SettlementCurrencyId=usd};
        var source=Owner("S");var old=Owner("O");var next=Owner("N");db.AddRange(source,old,next);await db.SaveChangesAsync();
        Account Account(Correspondent c)=>new(){AccountCode=c.Code,AccountName=c.Name,AccountType="Correspondent",CorrespondentId=c.Id};
        var sourceAccount=Account(source);var oldAccount=Account(old);var nextAccount=Account(next);db.AddRange(sourceAccount,oldAccount,nextAccount);
        PaymentLocation Location(string suffix)=>new(){Name="LC "+key+suffix,NormalizedName=PaymentLocationNameNormalizer.Normalize("LC "+key+suffix),CreatedBy=db.RequireCurrentUserId()};
        var a=Location("Old");var b=Location("sangmasha");var own=Location("Own");db.AddRange(a,b,own);await db.SaveChangesAsync();
        var settings=await db.CompanySettings.FirstOrDefaultAsync();
        if(settings is null){settings=new(){CompanyName="Location correction test"};db.CompanySettings.Add(settings);}
        settings.OwnPaymentLocationId=own.Id;await db.SaveChangesAsync();
        db.PaymentLocationCorrespondentAssignments.AddRange(new(){PaymentLocationId=a.Id,CorrespondentId=old.Id,EffectiveFrom=DateTime.Today.AddDays(-10)},new(){PaymentLocationId=b.Id,CorrespondentId=next.Id,EffectiveFrom=DateTime.Today.AddDays(-10)});
        await db.SaveChangesAsync();
        var root=await Service(db).CreateHawalaAsync(new(){Number=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),HawalaType="HawalaReceive",CorrespondentId=source.Id,
            PaymentLocationId=a.Id,FromAccountId=oldAccount.Id,FromCurrencyId=afn,ToCurrencyId=afn,FromAmount=70000,ToAmount=70000,ExchangeRate=1,Status="Paid",
            SenderName="Sender",ReceiverName="Receiver",ReferenceNumber="LC-"+key,GeneratedSendAgentCommissionAmount=100,GeneratedSendAgentCommissionCurrencyId=afn});
        var child=await db.Hawalas.Where(x=>x.SourceHawalaId==root.Id).Select(x=>x.Id).SingleAsync();
        return new(root.Id,child,source.Id,old.Id,next.Id,oldAccount.Id,nextAccount.Id,a.Id,b.Id,afn,usd);
    }
    protected static async Task<string> SourceFootprint(ApplicationDbContext db,Sample s,long transaction)=>JsonSerializer.Serialize(new{
        Entries=await db.LedgerEntries.AsNoTracking().Where(x=>x.HawalaId==s.Root || x.TransactionId==transaction).OrderBy(x=>x.Id)
            .Select(x=>new{x.Id,x.AccountId,x.CurrencyId,x.TalabKar,x.BadehKar,x.CreatedAt,x.Description,x.SettlementHawalaItemId}).ToListAsync(),
        Links=await db.CorrespondentSettlementConversionHawalas.AsNoTracking().Where(x=>x.HawalaId==s.Root).Select(x=>new{x.ConversionId,x.HawalaId}).ToListAsync(),
        Items=await db.CorrespondentSettlementConversionHawalaItems.AsNoTracking().Where(x=>x.HawalaId==s.Root).Select(x=>new{x.Id,x.ConversionId,x.ExchangeRate,x.SourceBadehKar,x.SourceTalabKar,x.TargetBadehKar,x.TargetTalabKar}).ToListAsync(),
        Stored=await SourceStoredRows(db,s)
    });
    private static async Task<string> SourceStoredRows(ApplicationDbContext db,Sample s)
    {
        var schema=db.Database.IsNpgsql()?"public":"dbo";
        var rows=new List<string>();
        foreach(var table in new[]{"CorrespondentSettlementValuations","CorrespondentSettlementHistorySnapshots","CorrespondentSettlementConversions"})
        {
            await using var command=db.Database.GetDbConnection().CreateCommand();command.Transaction=db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText=$"SELECT * FROM {schema}.\"{table}\" WHERE \"TenantId\"=@tenant AND "+(table=="CorrespondentSettlementValuations"?"\"HawalaId\"=@root ORDER BY \"HawalaId\"":"\""+(table=="CorrespondentSettlementConversions"?"Id":"ConversionId")+"\" IN(SELECT \"ConversionId\" FROM "+schema+".\"CorrespondentSettlementConversionHawalas\" WHERE \"TenantId\"=@tenant AND \"HawalaId\"=@root) ORDER BY \"Id\"");
            var p=command.CreateParameter();p.ParameterName="tenant";p.Value=db.CurrentTenantId;command.Parameters.Add(p);
            p=command.CreateParameter();p.ParameterName="root";p.Value=s.Root;command.Parameters.Add(p);
            await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync()){var values=new object[reader.FieldCount];reader.GetValues(values);rows.Add(JsonSerializer.Serialize(values));}
        }
        return JsonSerializer.Serialize(rows);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelatedCommissionsMoveButIncomingCommissionAndClosedSnapshotsRemain(bool closePeriod)
    {
        await using var db=Context();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var s=await Seed(db);
        // Direct agent fees and periodic destination fees are mutually exclusive.
        var child=await db.Hawalas.SingleAsync(x=>x.Id==s.Child);child.AgentCommissionAmount=null;child.AgentCommissionCurrencyId=null;
        db.LedgerEntries.RemoveRange(await db.LedgerEntries.Where(x=>x.HawalaId==s.Child && (x.TalabKar==100 || x.BadehKar==100)).ToListAsync());
        await db.SaveChangesAsync();
        var conversion=await new CorrespondentSettlementService(db).ConvertHawalasAsync(new(){CorrespondentId=s.Source,HawalaIds=[s.Root],HawalaRates=[new(){HawalaId=s.Root,SourceCurrencyId=s.Currency,Rate=70}]});
        var dummy=new Hawala{Number=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+100,HawalaType="HawalaSend",CorrespondentId=s.NewOwner,PaymentLocationId=s.NewLocation,
            FromCurrencyId=s.Currency,ToCurrencyId=s.Currency,FromAmount=100000,ToAmount=100000,Status="Pending",CreatedBy=db.RequireCurrentUserId()};
        db.Hawalas.Add(dummy);await db.SaveChangesAsync();
        foreach(var scope in new[]{"Forwarding","Destination"})
        {
            var doc=new Transaction{TransactionNo="LC-RATE-"+Guid.NewGuid().ToString("N"),TransactionType="PeriodicOutgoingCommission",Status="Paid",CreatedBy=db.RequireCurrentUserId(),BranchId=await db.GetDefaultBranchIdAsync()};
            db.Transactions.Add(doc);await db.SaveChangesAsync();
            db.CorrespondentCommissionBatches.Add(new(){CorrespondentId=scope=="Destination"?s.NewOwner:s.Source,CommissionScope=scope,AccountingVersion=3,
                PeriodFrom=DateTime.Today,PeriodTo=DateTime.Today,PostingTransactionId=doc.Id,CreatedBy=db.RequireCurrentUserId(),CommissionPerLakhAfn=scope=="Destination"?300:800,UsdToAfnRate=70,
                Items=[new(){HawalaId=dummy.Id,CommissionScope=scope,SourceCurrencyId=s.Currency,SourceAmount=100000,SourceToAfnRate=70,
                    PaymentLocationId=s.NewLocation,PerLakhRate=scope=="Destination"?300:800,IsActive=false}]});
            await db.SaveChangesAsync();
        }
        var commission=new CorrespondentCommissionService(db);
        var batches=new Dictionary<string,CorrespondentCommissionBatchDto>();
        // Seed posted financial documents inside the disposable fixture transaction.
        foreach(var scope in new[]{"Incoming","Forwarding","Destination"})
            batches[scope]=await PostCommissionInTransaction(db,new(){CorrespondentId=scope=="Destination"?s.OldOwner:s.Source,
                CommissionScope=scope,HawalaType=scope=="Incoming"?"HawalaReceive":"HawalaSend",PeriodFrom=DateTime.Today,PeriodTo=DateTime.Today,
                CommissionPerLakhAfn=400,UsdToAfnRate=70,PaymentLocationRates=[new(){PaymentLocationId=s.OldLocation,PerLakhRate=400}],
                LocationCurrencyRates=[new(){PaymentLocationId=s.OldLocation,CurrencyId=s.Currency,PerLakhRate=100}]});
        var before=await SourceFootprint(db,s,conversion.TransactionId);
        var incomingId=batches["Incoming"].Id;
        var incoming=await db.CorrespondentCommissionBatches.AsNoTracking().SingleAsync(x=>x.Id==incomingId);
        var incomingJson=JsonSerializer.Serialize(new{incoming.Status,incoming.PostingTransactionId,incoming.TotalCommissionUsd});
        if(closePeriod)
        {
            db.CorrespondentAccountPeriods.AddRange(new(){CorrespondentId=s.Source,PeriodNumber=1,PeriodFrom=DateTime.UtcNow.AddDays(-1),PeriodTo=DateTime.UtcNow,ClosedAt=DateTime.UtcNow,ClosedBy=db.RequireCurrentUserId()},
                new(){CorrespondentId=s.OldOwner,PeriodNumber=1,PeriodFrom=DateTime.UtcNow.AddDays(-1),PeriodTo=DateTime.UtcNow,ClosedAt=DateTime.UtcNow,ClosedBy=db.RequireCurrentUserId()});
            await db.SaveChangesAsync();
        }
        var periods=JsonSerializer.Serialize(await db.CorrespondentAccountPeriods.AsNoTracking().Select(x=>new{x.Id,x.PeriodTo,x.ClosedAt}).ToListAsync());
        await Service(db).ChangePaymentLocationAsync(s.Root,s.NewLocation);
        Assert.Equal(before,await SourceFootprint(db,s,conversion.TransactionId));
        incoming=await db.CorrespondentCommissionBatches.AsNoTracking().SingleAsync(x=>x.Id==incomingId);
        Assert.Equal(incomingJson,JsonSerializer.Serialize(new{incoming.Status,incoming.PostingTransactionId,incoming.TotalCommissionUsd}));
        var forwarded=await db.CorrespondentCommissionBatchItems.AsNoTracking().Include(x=>x.Batch).SingleAsync(x=>x.HawalaId==s.Child && x.IsActive && x.CommissionScope=="Forwarding");
        Assert.Equal(8,forwarded.Batch.TotalCommissionUsd);
        var destination=await db.CorrespondentCommissionBatchItems.AsNoTracking().Include(x=>x.Batch).SingleAsync(x=>x.HawalaId==s.Child && x.IsActive && x.CommissionScope=="Destination");
        Assert.Equal(s.NewOwner,destination.Batch.CorrespondentId);Assert.Equal(210,destination.Batch.TotalCommissionAfn);
        Assert.Equal(periods,JsonSerializer.Serialize(await db.CorrespondentAccountPeriods.AsNoTracking().Select(x=>new{x.Id,x.PeriodTo,x.ClosedAt}).ToListAsync()));
        await tx.RollbackAsync();
    }

    private static async Task<CorrespondentCommissionBatchDto> PostCommissionInTransaction(ApplicationDbContext db,CorrespondentCommissionPreviewRequestDto r)
    {
        var rates=new DataTable();rates.Columns.Add("CurrencyId",typeof(long));rates.Columns.Add("SourceToAfnRate",typeof(decimal));
        void Configure(SqlParameterCollection p)
        {
            p.AddWithValue("Mode","Post");p.AddWithValue("TenantId",db.CurrentTenantId);p.AddWithValue("CurrentUserId",db.RequireCurrentUserId());
            p.AddWithValue("CorrespondentId",r.CorrespondentId);p.AddWithValue("HawalaType",r.HawalaType);p.AddWithValue("CommissionScope",r.CommissionScope);
            p.AddWithValue("UtcOffsetMinutes",(int)TimeZoneInfo.Local.GetUtcOffset(r.PeriodFrom).TotalMinutes);
            p.Add("PeriodFrom",SqlDbType.Date).Value=r.PeriodFrom;p.Add("PeriodTo",SqlDbType.Date).Value=r.PeriodTo;
            p.Add("FromUtc",SqlDbType.DateTime2).Value=r.PeriodFrom.ToUniversalTime();p.Add("ToUtcExclusive",SqlDbType.DateTime2).Value=r.PeriodTo.AddDays(1).ToUniversalTime();
            p.AddWithValue("CommissionPerLakhAfn",r.CommissionPerLakhAfn);p.AddWithValue("UsdToAfnRate",r.UsdToAfnRate);
            p.Add(new SqlParameter("Rates",SqlDbType.Structured){TypeName="dbo.CommissionRateTableType_v1",Value=rates});
            p.AddWithValue("LocationRatesJson",JsonSerializer.Serialize(r.PaymentLocationRates));p.AddWithValue("CurrencyRatesJson","[]");
            p.AddWithValue("LocationCurrencyRatesJson",JsonSerializer.Serialize(r.LocationCurrencyRates));
        }
        if(db.Database.IsNpgsql())
        {
            using var parameters=new SqlCommand();Configure(parameters.Parameters);
            var values=parameters.Parameters.Cast<SqlParameter>().ToDictionary(p=>p.ParameterName,p=>p.Value is DataTable?(object)Array.Empty<object>():p.Value);
            await using var native=new Npgsql.NpgsqlCommand("SELECT hawala.periodic_commission(@request)::text",(Npgsql.NpgsqlConnection)db.Database.GetDbConnection(),(Npgsql.NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());
            native.Parameters.AddWithValue("request",NpgsqlTypes.NpgsqlDbType.Jsonb,JsonSerializer.Serialize(values));
            using var json=JsonDocument.Parse((string)(await native.ExecuteScalarAsync())!);return new(){Id=json.RootElement[0][0].GetProperty("Id").GetInt64()};
        }
        await using var command=new SqlCommand("dbo.usp_ProcessPeriodicCommission_v1",(SqlConnection)db.Database.GetDbConnection(),(SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction()){CommandType=CommandType.StoredProcedure};
        Configure(command.Parameters);await using var rows=await command.ExecuteReaderAsync();Assert.True(await rows.ReadAsync());return new(){Id=rows.GetInt64(0)};
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocationCorrectionKeepsQuettaConversionAndSourceEntriesExactly(bool wholeAccount)
    {
        await using var db=Context();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var s=await Seed(db);var settlement=new CorrespondentSettlementService(db);
        var conversion=wholeAccount?await settlement.ConvertBalanceAsync(new(){CorrespondentId=s.Source,RateDate=DateTime.Today,Rates=[new(){SourceCurrencyId=s.Currency,Rate=70}]}):
            await settlement.ConvertHawalasAsync(new(){CorrespondentId=s.Source,HawalaIds=[s.Root],HawalaRates=[new(){HawalaId=s.Root,SourceCurrencyId=s.Currency,Rate=70}]});
        var before=await SourceFootprint(db,s,conversion.TransactionId);
        var oldIds=await db.LedgerEntries.Where(x=>x.HawalaId==s.Child).Select(x=>x.Id).ToArrayAsync();
        await Service(db).ChangePaymentLocationAsync(s.Child,s.NewLocation);
        Assert.Equal(before,await SourceFootprint(db,s,conversion.TransactionId));
        var root=await db.Hawalas.AsNoTracking().SingleAsync(x=>x.Id==s.Root);var sent=await db.Hawalas.AsNoTracking().SingleAsync(x=>x.Id==s.Child);
        Assert.Equal(s.Source,root.CorrespondentId);Assert.Equal(70000,root.FromAmount);Assert.Equal(s.NewLocation,root.PaymentLocationId);
        Assert.Equal(s.NewOwner,sent.CorrespondentId);Assert.Equal(s.NewLocation,sent.PaymentLocationId);
        Assert.Equal(0,await db.LedgerEntries.Where(x=>x.HawalaId==s.Child && x.AccountId==s.OldAccount).SumAsync(x=>x.TalabKar-x.BadehKar));
        Assert.Equal(70100,await db.LedgerEntries.Where(x=>x.HawalaId==s.Child && x.AccountId==s.NewAccount).SumAsync(x=>x.TalabKar-x.BadehKar));
        Assert.Equal(oldIds.Length,await db.LedgerEntries.CountAsync(x=>oldIds.Contains(x.Id)));
        var count=await db.LedgerEntries.CountAsync();await Service(db).ChangePaymentLocationAsync(s.Root,s.NewLocation);Assert.Equal(count,await db.LedgerEntries.CountAsync());
        await tx.RollbackAsync();
    }
    [Fact]
    public async Task InvalidDestinationCannotChangeEitherHawalaOrLedger()
    {
        await using var db=Context();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var s=await Seed(db);var count=await db.LedgerEntries.CountAsync();
        await Assert.ThrowsAnyAsync<Exception>(()=>Service(db).ChangePaymentLocationAsync(s.Root,long.MaxValue));
        Assert.Equal(s.OldLocation,await db.Hawalas.Where(x=>x.Id==s.Root).Select(x=>x.PaymentLocationId).SingleAsync());Assert.Equal(count,await db.LedgerEntries.CountAsync());
        await tx.RollbackAsync();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ConvertedOutgoingShareMovesWithoutChangingQuettaOrOtherHawalasInItsConversion(bool newOwnerUsesSettlement, bool hasDirectFee)
    {
        await using var db=Context();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var s=await Seed(db);var settlement=new CorrespondentSettlementService(db);
        if(!hasDirectFee)
        {
            var sent=await db.Hawalas.FindAsync(s.Child);sent!.AgentCommissionAmount=null;sent.AgentCommissionCurrencyId=null;
            db.LedgerEntries.RemoveRange(await db.LedgerEntries.Where(x=>x.HawalaId==s.Child && (x.TalabKar==100 || x.BadehKar==100)).ToListAsync());await db.SaveChangesAsync();
        }
        var origin=await settlement.ConvertHawalasAsync(new(){CorrespondentId=s.Source,HawalaIds=[s.Root],HawalaRates=[new(){HawalaId=s.Root,SourceCurrencyId=s.Currency,Rate=70}]});
        async Task<Hawala> Dummy(long owner,long account,long location)
        {
            var h=new Hawala{Number=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+owner,HawalaType="HawalaSend",CorrespondentId=owner,PaymentLocationId=location,
                FromCurrencyId=s.Currency,ToCurrencyId=s.Currency,FromAmount=10000,ToAmount=10000,Status="Pending",CreatedBy=db.RequireCurrentUserId()};
            db.Hawalas.Add(h);await db.SaveChangesAsync();
            var cash=await db.Accounts.Where(x=>x.AccountType=="Cash").Select(x=>x.Id).FirstAsync();
            db.LedgerEntries.AddRange(new LedgerEntry{HawalaId=h.Id,AccountId=account,CurrencyId=s.Currency,TalabKar=10000},new LedgerEntry{HawalaId=h.Id,AccountId=cash,CurrencyId=s.Currency,BadehKar=10000});
            await db.SaveChangesAsync();return h;
        }
        var other=await Dummy(s.OldOwner,s.OldAccount,s.OldLocation);
        var oldConversion=await settlement.ConvertHawalasAsync(new(){CorrespondentId=s.OldOwner,HawalaIds=[s.Child,other.Id],HawalaRates=[new(){HawalaId=s.Child,SourceCurrencyId=s.Currency,Rate=65},new(){HawalaId=other.Id,SourceCurrencyId=s.Currency,Rate=65}]});
        if(newOwnerUsesSettlement)
        {
            var newRate=await Dummy(s.NewOwner,s.NewAccount,s.NewLocation);
            await settlement.ConvertHawalasAsync(new(){CorrespondentId=s.NewOwner,HawalaIds=[newRate.Id],HawalaRates=[new(){HawalaId=newRate.Id,SourceCurrencyId=s.Currency,Rate=80}]});
        }
        else
        {
            var owner=await db.Correspondents.SingleAsync(x=>x.Id==s.NewOwner);owner.SettlementCurrencyId=null;await db.SaveChangesAsync();
        }
        var before=await SourceFootprint(db,s,origin.TransactionId);
        var otherItems=JsonSerializer.Serialize(await db.CorrespondentSettlementConversionHawalaItems.AsNoTracking().Where(x=>x.HawalaId==other.Id).Select(x=>new{x.Id,x.ExchangeRate,x.TargetTalabKar}).ToListAsync());
        var oldOriginal=await db.LedgerEntries.Where(x=>x.TransactionId==oldConversion.TransactionId).Select(x=>x.Id).ToArrayAsync();
        await Service(db).ChangePaymentLocationAsync(s.Root,s.NewLocation);
        Assert.Equal(before,await SourceFootprint(db,s,origin.TransactionId));
        Assert.Equal(otherItems,JsonSerializer.Serialize(await db.CorrespondentSettlementConversionHawalaItems.AsNoTracking().Where(x=>x.HawalaId==other.Id).Select(x=>new{x.Id,x.ExchangeRate,x.TargetTalabKar}).ToListAsync()));
        Assert.Equal(oldOriginal.Length,await db.LedgerEntries.CountAsync(x=>oldOriginal.Contains(x.Id)));
        if(newOwnerUsesSettlement)
        {
            var moved=await db.CorrespondentSettlementConversionHawalaItems.AsNoTracking().Include(x=>x.Conversion).SingleAsync(x=>x.HawalaId==s.Child);
            Assert.Equal(s.NewOwner,moved.Conversion.CorrespondentId);Assert.Equal(80,moved.ExchangeRate);Assert.Equal(876.25m,moved.TargetTalabKar);
        }
        else
        {
            Assert.False(await db.CorrespondentSettlementConversionHawalas.AnyAsync(x=>x.HawalaId==s.Child));
            Assert.Equal(hasDirectFee?70100m:70000m,await db.LedgerEntries.Where(x=>x.AccountId==s.NewAccount && x.CurrencyId==s.Currency).SumAsync(x=>x.TalabKar-x.BadehKar));
        }
        Assert.Equal(0m,await db.LedgerEntries.Where(x=>x.AccountId==s.OldAccount && x.CurrencyId==s.Currency).SumAsync(x=>x.TalabKar-x.BadehKar));
        Assert.Equal(153.85m,await db.LedgerEntries.Where(x=>x.AccountId==s.OldAccount && x.CurrencyId==s.Usd).SumAsync(x=>x.TalabKar-x.BadehKar));
        await tx.RollbackAsync();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("bill")]
    [InlineData("executed")]
    [InlineData("tenant")]
    public async Task UnsafeDestinationCorrectionsAreRejectedWithoutAccountingChanges(string reason)
    {
        await using var db=Context();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var s=await Seed(db);
        if(reason=="inactive"){var loc=await db.PaymentLocations.FindAsync(s.NewLocation);loc!.IsActive=false;}
        if(reason=="executed"){var owner=await db.Correspondents.FindAsync(s.OldOwner);owner!.RequiresManualExecution=true;}
        if(reason=="bill")db.AgentPaymentBills.Add(new(){CorrespondentId=s.OldOwner,CreatedBy=db.RequireCurrentUserId(),Items=[new(){HawalaId=s.Child}]});
        await db.SaveChangesAsync();var count=await db.LedgerEntries.CountAsync();
        using(var scope=reason=="tenant"?db.UseTenantScope(999999):null)
            await Assert.ThrowsAnyAsync<Exception>(()=>Service(db).ChangePaymentLocationAsync(s.Root,s.NewLocation));
        Assert.Equal(count,await db.LedgerEntries.CountAsync());
        Assert.Equal(s.OldLocation,await db.Hawalas.Where(x=>x.Id==s.Root).Select(x=>x.PaymentLocationId).SingleAsync());
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task MissingNewSettlementRateRollsBackPartialReversalsAndBothHawalas()
    {
        await using var db=Context();using var bypass=db.BypassSubscriptionEnforcement();await using var tx=await db.Database.BeginTransactionAsync();
        var s=await Seed(db);var settlement=new CorrespondentSettlementService(db);
        var source=await settlement.ConvertHawalasAsync(new(){CorrespondentId=s.Source,HawalaIds=[s.Root],HawalaRates=[new(){HawalaId=s.Root,SourceCurrencyId=s.Currency,Rate=70}]});
        var destination=await settlement.ConvertHawalasAsync(new(){CorrespondentId=s.OldOwner,HawalaIds=[s.Child],HawalaRates=[new(){HawalaId=s.Child,SourceCurrencyId=s.Currency,Rate=65}]});
        var before=await SourceFootprint(db,s,source.TransactionId);
        var oldHistory=JsonSerializer.Serialize(await settlement.GetHistoryDetailsAsync(s.OldOwner,destination.ConversionId));
        var count=await db.LedgerEntries.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(db).ChangePaymentLocationAsync(s.Root,s.NewLocation));
        Assert.Equal(before,await SourceFootprint(db,s,source.TransactionId));Assert.Equal(count,await db.LedgerEntries.CountAsync());
        Assert.Equal(oldHistory,JsonSerializer.Serialize(await settlement.GetHistoryDetailsAsync(s.OldOwner,destination.ConversionId)));
        Assert.Equal(s.OldLocation,await db.Hawalas.Where(x=>x.Id==s.Root).Select(x=>x.PaymentLocationId).SingleAsync());
        Assert.Equal(s.OldOwner,await db.Hawalas.Where(x=>x.Id==s.Child).Select(x=>x.CorrespondentId).SingleAsync());
        await tx.RollbackAsync();
    }
}
