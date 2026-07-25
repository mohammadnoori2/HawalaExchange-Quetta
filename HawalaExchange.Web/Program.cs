using AutoMapper;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.Web.Components;
using HawalaExchange.Web.Components.Account;
using HawalaSystem.Mappings;
using Microsoft.AspNetCore.Components.Authorization;
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

        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();

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
        // ============================================================
        // 8. Email Sender
        // ============================================================
        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

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

                SeedData.InitializeAsync(roleManager, userManager, context).Wait();

                // Recalculate cost positions and recreate all exchange valuation ledger
                // entries after migrations or application restarts.
                var currencyCostService = services.GetRequiredService<ICurrencyCostService>();
                currencyCostService.RebuildAsync().GetAwaiter().GetResult();
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
