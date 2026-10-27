using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 2 || !Regex.IsMatch(args[1], "^HawalaExchangeQuettaDemo_[0-9]{8}_[a-f0-9]{8}$"))
    throw new ArgumentException("Supply repository path and a NEW HawalaExchangeQuettaDemo_yyyyMMdd_8hex database name.");
var root = Path.GetFullPath(args[0]);
var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, "HawalaExchange.Web", "appsettings.Development.json")).Build();
var sql = new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection")) { InitialCatalog = args[1] };
var masterSql = new SqlConnectionStringBuilder(sql.ConnectionString) { InitialCatalog = "master" };
await using (var master = new SqlConnection(masterSql.ConnectionString))
{
    await master.OpenAsync();
    await using var command = master.CreateCommand();
    command.CommandText = "SELECT DB_ID(@name)";
    command.Parameters.AddWithValue("@name", sql.InitialCatalog);
    if (await command.ExecuteScalarAsync() is not DBNull)
        throw new InvalidOperationException("Database already exists. This tool NEVER clears or reseeds an existing database.");
}
var tenant = new DemoTenant();
var services = new ServiceCollection().AddLogging();
services.AddSingleton<ICurrentTenant>(tenant);
services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(sql.ConnectionString, x => x.CommandTimeout(180)));
services.AddIdentityCore<ApplicationUser>(options => options.User.AllowedUserNameCharacters = null!)
    .AddRoles<IdentityRole<long>>().AddEntityFrameworkStores<ApplicationDbContext>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
Console.WriteLine($"Creating NEW demo database: {sql.InitialCatalog}");
await db.Database.MigrateAsync();
using var bypass = db.BypassSubscriptionEnforcement();
await db.EnsureApplicationSchemaAsync();
await SeedData.InitializeAsync(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<long>>>(),
    scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), db, false, new ConfigurationBuilder().Build());
var admin = await db.Users.SingleAsync(x => x.LocalUserName == "admin");
tenant.UserId = admin.Id;
var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + "a1";
admin.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(admin, password);
admin.SecurityStamp = Guid.NewGuid().ToString();
admin.FullName = "مدیر آزمایشی";
var demoTenant = await db.Tenants.SingleAsync(x => x.Id == 1);
demoTenant.Name = "صرافی آزمایشی — اطلاعات غیرواقعی";
await db.EnsureSystemAccountsAsync(1);
var afn = await db.Currencies.SingleAsync(x => x.Code == "AFN");
var usd = await db.Currencies.SingleAsync(x => x.Code == "USD");
var source = new Correspondent { Code = "DEMO-QUETTA", Name = "کویته", CommissionMethod = "PeriodicPerLakh", SettlementCurrencyId = usd.Id };
var destination = new Correspondent { Code = "DEMO-DEST", Name = "نمایندگی مقصد آزمایشی", CommissionMethod = "PerTransaction", SettlementCurrencyId = usd.Id };
db.Correspondents.AddRange(source, destination);
await db.SaveChangesAsync();
var sourceAccount = new Account { AccountCode = "DEMO-QUETTA", AccountName = "حساب کویته", AccountType = "Correspondent", CorrespondentId = source.Id };
var destinationAccount = new Account { AccountCode = "DEMO-DEST", AccountName = "حساب مقصد آزمایشی", AccountType = "Correspondent", CorrespondentId = destination.Id };
db.Accounts.AddRange(sourceAccount, destinationAccount);
var locations = new[] { "کابل برچی", "هرات", "قندهار", "دفتر خود صرافی" }.Select(name => new PaymentLocation
{ Name = name, NormalizedName = PaymentLocationNameNormalizer.Normalize(name), Address = "آدرس آزمایشی", CreatedBy = admin.Id }).ToList();
db.PaymentLocations.AddRange(locations);
await db.SaveChangesAsync();
db.CompanySettings.Add(new CompanySetting { CompanyName = "محیط آزمایشی — اطلاعات غیرواقعی", DefaultProfitCurrencyId = usd.Id,
    OwnPaymentLocationId = locations[3].Id, UsePersianCalendar = true });
// Dates are explicit so a later rerun cannot silently move the scenario.
var today = new DateTime(2026, 10, 5);
foreach (var day in new[] { today.AddDays(-1), today })
    db.CorrespondentDailyCommissionRates.Add(new CorrespondentDailyCommissionRate
    { CorrespondentId = source.Id, RateDate = day, UsdToAfnRate = day == today ? 67m : 66m, CreatedBy = admin.Id });
await db.SaveChangesAsync();
var cash = await db.Accounts.SingleAsync(x => x.AccountCode == "1001");
var number = 900000L;
var zone = TimeZoneInfo.FindSystemTimeZoneById("Afghanistan Standard Time");
async Task AddSet(int set)
{
    foreach (var day in new[] { today.AddDays(-1), today })
    for (var locationIndex = 0; locationIndex < locations.Count; locationIndex++)
    foreach (var currency in new[] { afn, usd })
    {
        var amount = currency.Code == "AFN" ? 330000m + locationIndex * 66000m : 5000m + locationIndex * 1000m;
        var date = TimeZoneInfo.ConvertTimeToUtc(day.AddHours(set == 1 ? 10 : 14).AddMinutes(locationIndex * 5), zone);
        var receive = new Hawala { Number = ++number, HawalaType = "HawalaReceive", CorrespondentId = source.Id,
            PaymentLocationId = locations[3].Id, FromCurrencyId = currency.Id, ToCurrencyId = currency.Id,
            FromAmount = amount, ToAmount = amount, CreatedAt = date, CreatedBy = admin.Id, Status = "Paid",
            SenderName = "فرستنده آزمایشی کویته", ReceiverName = "گیرنده آزمایشی", Notes = $"DEMO set {set} - synthetic data" };
        db.Hawalas.Add(receive);
        await db.SaveChangesAsync();
        var send = new Hawala { Number = ++number, HawalaType = "HawalaSend", CorrespondentId = destination.Id,
            SourceHawalaId = receive.Id, IsSystemGenerated = true, PaymentLocationId = locations[locationIndex].Id,
            FromCurrencyId = currency.Id, ToCurrencyId = currency.Id, FromAmount = amount / 2, ToAmount = amount / 2,
            CreatedAt = date.AddMinutes(20), CreatedBy = admin.Id, Status = "Paid", SenderName = "فرستنده آزمایشی",
            ReceiverName = "گیرنده آزمایشی", Notes = $"DEMO set {set} - synthetic data" };
        db.Hawalas.Add(send);
        await db.SaveChangesAsync();
        db.LedgerEntries.AddRange(
            Entry(receive, cash.Id, amount, 0), Entry(receive, sourceAccount.Id, 0, amount),
            Entry(send, destinationAccount.Id, amount / 2, 0), Entry(send, cash.Id, 0, amount / 2));
        await db.SaveChangesAsync();
    }
}
LedgerEntry Entry(Hawala hawala, long account, decimal debit, decimal credit) => new()
{ HawalaId = hawala.Id, AccountId = account, CurrencyId = hawala.FromCurrencyId, BadehKar = debit, TalabKar = credit,
    CreatedAt = hawala.CreatedAt, Description = "سند اصل حواله آزمایشی" };
CorrespondentCommissionPreviewRequestDto Request() => new()
{ CorrespondentId = source.Id, CommissionScope = "Forwarding", HawalaType = "HawalaSend",
    PeriodFrom = today.AddDays(-1), PeriodTo = today, CommissionPerLakhAfn = 400m,
    PaymentLocationRates = locations.Take(3).Select((x, i) => new PaymentLocationCommissionRateDto
    { PaymentLocationId = x.Id, PerLakhRate = 400m + i * 100m }).ToList() };
await AddSet(1);
var commissions = new CorrespondentCommissionService(db);
var history = await commissions.PostAsync(Request());
await AddSet(2);
var preview = await commissions.PreviewAsync(Request());
var details = await commissions.GetDetailsAsync(history.Id);
if (preview.HawalaCount != 12 || details.HawalaCount != 12 || preview.Items.Any(x => x.PaymentLocationId == locations[3].Id)
    || preview.Items.Select(x => x.HawalaDate.Date).Distinct().Count() != 2
    || details.TotalCommissionUsd != preview.TotalCommissionUsd)
    throw new InvalidOperationException("Demo verification failed.");
foreach (var group in (await db.LedgerEntries.ToListAsync()).GroupBy(x => x.CurrencyId))
    if (group.Sum(x => x.BadehKar) != group.Sum(x => x.TalabKar)) throw new InvalidOperationException("Unbalanced demo ledger.");
Console.WriteLine(JsonSerializer.Serialize(new { Database = sql.InitialCatalog, Server = sql.DataSource,
    UserName = "admin", Password = password, Hawalas = await db.Hawalas.CountAsync(), QuettaId = source.Id,
    HistoryBatchId = history.Id, PreviewHawalas = preview.HawalaCount, PreviewCommissionUsd = preview.TotalCommissionUsd,
    Yesterday = "2026/10/04", Today = "2026/10/05" }));

sealed class DemoTenant : ICurrentTenant
{ public long TenantId => 1; public long UserId { get; set; } public bool HasTenant => true; }
