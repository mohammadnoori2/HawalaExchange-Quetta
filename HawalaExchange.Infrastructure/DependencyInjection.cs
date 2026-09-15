using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HawalaExchange.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // Register DbContext
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));

            // ===== Register All Services =====

            // Base/Common Services
            services.AddScoped<IBranchService, BranchService>();
            

            // Entity Services
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<ICorrespondentService, CorrespondentService>();
            services.AddScoped<ICorrespondentCommissionService, CorrespondentCommissionService>();
            services.AddScoped<ICurrencyService, CurrencyService>();
            services.AddScoped<IAccountService, AccountService>();
            services.AddScoped<ICashBalanceAlertService, CashBalanceAlertService>();

            // ✅ Fixed: Correct name (singular, not plural)
            services.AddScoped<IExchangeRateService, ExchangeRateService>();

            // Financial Services
            services.AddScoped<ITransactionService, TransactionService>();
            services.AddScoped<ILedgerService, LedgerService>();
            services.AddScoped<ITransferService, TransferService>();
            services.AddScoped<IExpenseService, ExpenseService>();
            services.AddScoped<ICurrencyCostService, CurrencyCostService>();
            services.AddScoped<IExportService, ExportService>();

            // Document and Audit
            services.AddScoped<IDocumentService, DocumentService>();
            services.AddScoped<IAuditLogService, AuditLogService>();

            // Balance and Reports
            services.AddScoped<IBalanceService, BalanceService>();
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<IFinancialReportService, FinancialReportService>();

            // PDF converter (Haukcode.WkHtmlToPdfDotNet — native bundled via NuGet)
            services.AddSingleton(typeof(WkHtmlToPdfDotNet.Contracts.IConverter), new WkHtmlToPdfDotNet.SynchronizedConverter(new WkHtmlToPdfDotNet.PdfTools()));

            return services;
        }
    }
}
