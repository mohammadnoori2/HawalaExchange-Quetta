using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
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
            services.AddScoped<ICurrencyService, CurrencyService>();
            services.AddScoped<IAccountService, AccountService>();

            // ✅ Fixed: Correct name (singular, not plural)
            services.AddScoped<IExchangeRateService, ExchangeRateService>();

            // Financial Services
            services.AddScoped<ITransactionService, TransactionService>();
            services.AddScoped<ILedgerService, LedgerService>();
            services.AddScoped<ITransferService, TransferService>();
            services.AddScoped<IExpenseService, ExpenseService>();

            // Document and Audit
            services.AddScoped<IDocumentService, DocumentService>();
            services.AddScoped<IAuditLogService, AuditLogService>();

            // Balance and Reports
            services.AddScoped<IBalanceService, BalanceService>();
            services.AddScoped<IReportService, ReportService>();

            return services;
        }
    }
}