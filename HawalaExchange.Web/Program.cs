using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.Web.Components;
using HawalaExchange.Web.Components.Account;
using HawalaExchange.Web.Services;
using HawalaSystem.Mappings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ============================================================
        // 1. Add services to the container
        // ============================================================
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // ============================================================
        // 2. Database Context
        // ============================================================
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

        // ============================================================
        // 3. Identity Configuration
        // ============================================================
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<long>>(options =>
        {
            // Password settings
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 6;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;

            // Lockout settings
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;

            // User settings
            // نام‌های محلی می‌توانند فارسی باشند و نام canonical شامل ':' است.
            options.User.AllowedUserNameCharacters = null;
            // ایمیل و نام کاربری محلی فقط در محدوده هر صرافی یکتا هستند.
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedAccount = false;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // ============================================================
        // 4. Authentication & Authorization
        // ============================================================
        // ✅ حذف AddAuthentication اضافی - فقط از Identity استفاده کنید
        // ❌ builder.Services.AddAuthentication(...) را حذف کنید

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("PlatformAccess", policy => policy
                .RequireClaim("platform_user", "true")
                .RequireRole(PlatformRoles.All));
            options.AddPolicy("PlatformWrite", policy => policy
                .RequireClaim("platform_user", "true")
                .RequireRole(PlatformRoles.Writers));
            options.AddPolicy("TenantAccess", policy => policy
                .RequireClaim(CurrentTenant.TenantIdClaim));
        });
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<CurrentTenant>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        builder.Services.AddScoped<IDbContextFactory<ApplicationDbContext>, TenantDbContextFactory>();
        builder.Services.AddScoped<CircuitHandler, TenantCircuitHandler>();
        builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, TenantUserClaimsPrincipalFactory>();

        // ============================================================
        // 5. Identity Services for Blazor
        // ============================================================
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        // ============================================================
        // 6. AutoMapper
        // ============================================================
        builder.Services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<MappingProfile>();
        }, typeof(MappingProfile).Assembly);

        // ============================================================
        // 7. Register Business Services
        // ============================================================
        builder.Services.AddScoped<IBranchService, BranchService>();
        builder.Services.AddScoped<ICustomerService, CustomerService>();
        builder.Services.AddScoped<ICorrespondentService, CorrespondentService>();
        builder.Services.AddScoped<ICurrencyService, CurrencyService>();
        builder.Services.AddScoped<IAccountService, AccountService>();
        builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
        builder.Services.AddScoped<ITransactionService, TransactionService>();
        builder.Services.AddScoped<ILedgerService, LedgerService>();
        builder.Services.AddScoped<ITransferService, TransferService>();
        builder.Services.AddScoped<IExpenseService, ExpenseService>();
        builder.Services.AddScoped<IDocumentService, DocumentService>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddScoped<IBalanceService, BalanceService>();
        builder.Services.AddScoped<IReportService, ReportService>();
        builder.Services.AddScoped<IFinancialReportService, FinancialReportService>();
        builder.Services.AddScoped<IAccountBadehkarLimitService, AccountBadehkarLimitService>();
        builder.Services.AddScoped<IHawalaService, HawalaService>();
        builder.Services.AddScoped<ICapitalInvestmentService, CapitalInvestmentService>();
        builder.Services.AddScoped<IPaymentLocationService, PaymentLocationService>();
        builder.Services.AddScoped<IFileService, FileService>();

        builder.Services.AddScoped<IAccountMoneyOperationService, AccountMoneyOperationService>();
        builder.Services.AddScoped<IMoneyExchangeOperationService, MoneyExchangeOperationService>();
        builder.Services.AddScoped<ICurrencyCostService, CurrencyCostService>();
        builder.Services.AddScoped<IJournalService, JournalService>();
        builder.Services.AddScoped<ICompanySettingService, CompanySettingService>();
        builder.Services.AddScoped<ITenantAdministrationService, TenantAdministrationService>();
        builder.Services.AddScoped<ISaasAdministrationService, SaasAdministrationService>();
        builder.Services.AddScoped<ISaasBillingService, SaasBillingService>();
        builder.Services.AddScoped<ISubscriptionAccessService, SubscriptionAccessService>();
        builder.Services.AddScoped<IPlatformUserService, PlatformUserService>();
        builder.Services.AddScoped<ISaasAutomationService, SaasAutomationService>();
        builder.Services.AddScoped<ISaasReportingService, SaasReportingService>();
        builder.Services.AddSingleton<IPlatformMessageSender, SmtpPlatformMessageSender>();
        builder.Services.AddHostedService<SaasAutomationWorker>();
        // ============================================================
        // 8. Email Sender
        // ============================================================
        builder.Services.Configure<SmtpEmailOptions>(
            builder.Configuration.GetSection(SmtpEmailOptions.SectionName));
        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, SmtpIdentityEmailSender>();

        var app = builder.Build();

        // ============================================================
        // 9. Configure HTTP Request Pipeline
        // ============================================================
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();

        // ✅ Authentication & Authorization Middleware
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // ✅ Map Identity Endpoints
        app.MapAdditionalIdentityEndpoints();

        app.MapGet("/api/platform/reports/excel", async (
            ISaasReportingService reportingService,
            DateTime? fromDate,
            DateTime? toDate,
            long? planId,
            SubscriptionStatus? status,
            string? currencyCode,
            string? search,
            string? sortBy,
            bool sortDescending,
            CancellationToken cancellationToken) =>
        {
            var file = await reportingService.ExportExcelAsync(new SaasReportFilterDto
            {
                FromDate = fromDate, ToDate = toDate, PlanId = planId, Status = status,
                CurrencyCode = currencyCode, Search = search, SortBy = sortBy ?? "name", SortDescending = sortDescending
            }, cancellationToken);
            return Results.File(file.Content, file.ContentType, file.FileName);
        }).RequireAuthorization("PlatformAccess");

        // ============================================================
        // 10. Seed Data
        // ============================================================
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var context = services.GetRequiredService<ApplicationDbContext>();
                context.Database.Migrate();

                var roleManager = services.GetRequiredService<RoleManager<IdentityRole<long>>>();
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

                SeedData.InitializeAsync(
                    roleManager,
                    userManager,
                    context,
                    app.Environment.IsDevelopment(),
                    builder.Configuration).Wait();

                var tenantIds = context.Tenants
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .Select(x => x.Id)
                    .ToList();
                var currencyCostService = services.GetRequiredService<ICurrencyCostService>();
                foreach (var tenantId in tenantIds)
                {
                    using var tenantScope = context.UseTenantScope(tenantId);
                    using var subscriptionBypass = context.BypassSubscriptionEnforcement();
                    context.EnsureSystemAccountsAsync(tenantId).GetAwaiter().GetResult();
                    // Recalculate cost positions and valuation entries independently.
                    currencyCostService.RebuildAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "An error occurred while seeding the database.");
            }
        }

        app.Run();
    }
}
