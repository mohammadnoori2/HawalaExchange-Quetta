using HawalaExchange.Application.Security;

namespace HawalaExchange.Infrastructure.Services;

/// <summary>Explicit public service boundary map. Unlisted operations fail closed.</summary>
public static class PermissionOperationMap
{
    public static readonly IReadOnlyDictionary<string,string> Modules=new Dictionary<string,string>
    {
        ["IBranchService"]="Branches",["ICustomerService"]="Customers",["ICorrespondentService"]="Correspondents",
        ["ICurrencyService"]="Currencies",["IAccountService"]="Accounts",["IExchangeRateService"]="Rates",
        ["ITransactionService"]="Transactions",["ILedgerService"]="Ledger",["ITransferService"]="Transfers",
        ["IExpenseService"]="Expenses",["IDocumentService"]="Documents",["IAuditLogService"]="AuditLogs",
        ["IBalanceService"]="Accounts",["IReportService"]="Reports",["IFinancialReportService"]="Reports",
        ["IAccountBadehkarLimitService"]="AccountLimits",["ICashBalanceAlertService"]="CashAlerts",
        ["IHawalaService"]="Hawalas",["IHawalaImportService"]="BulkImports",["IHawalaImportQueue"]="BulkImports",
        ["IHawalaImportJobExecutor"]="BulkImports",
        ["ICapitalInvestmentService"]="Capital",["IPaymentLocationService"]="PaymentLocations",
        ["IPaymentLocationAssignmentService"]="PaymentLocations",["ICorrespondentDailyRateService"]="Settlement",
        ["IAccountMoneyOperationService"]="Deposits",["IMoneyExchangeOperationService"]="Exchanges",
        ["IJournalService"]="Journal",["ICompanySettingService"]="Settings",["IAedDealService"]="AedDeals",
        ["ICorrespondentCommissionService"]="IncomingCommission",["ICorrespondentSettlementService"]="Settlement",
        ["ISelectedRecordsService"]="Hawalas",["IHawalaCommissionReportService"]="HawalaReports",
        ["IAccountStatementService"]="AccountReports",["IExportService"]="CustomerReports",
        ["IHawalaPaymentHistoryExportService"]="HawalaExecution",["ICorrespondentSettlementHistoryExportService"]="Settlement"
    };
    private static readonly Dictionary<string,string> Actions=new(StringComparer.Ordinal);
    static PermissionOperationMap()
    {
        Add("View","GetByIdAsync GetAllAsync FindAsync ExistsAsync CountAsync GetByCodeAsync GetActiveAsync GetActiveBranchesAsync GetActiveCurrenciesAsync GetActiveAccountsAsync GetByCustomerCodeAsync SearchAsync GetArchivedAsync GetByAccountCodeAsync GetByAccountTypeAsync GetByReferenceAsync GetAccountBalanceAsync GetAllAccountBalancesAsync GetNextAccountCodeAsync GetByCountryAsync GetDetailsPageAsync GetStatusPageAsync GetByTransactionNoAsync GetByBranchAsync GetByCustomerAsync GetByDateRangeAsync GetByStatusAsync GetPendingTransactionsAsync GenerateTransactionNumberAsync GetHawalaByIdAsync GetHawalasAsync GetStatisticsAsync GetCorrespondentRangeAsync GetNextNumberAsync GetHistoryAsync GetDetailsAsync GetPreviewAsync GetHawalaPreviewAsync GetRatesAsync GetHistoryDetailsAsync PreviewReadOnlyAsync PreviewConversionAsync GetAsync GetImpactAsync GetPositionsAsync GetCostPositionsAsync GetProfitSummaryAsync GetTransfersByAccountAsync GetTransfersByMethodAsync GetTransferByIdAsync GetTransfersByDateRangeAsync GetExpensesByDateRangeAsync GetExpensesByCurrencyAsync GetTotalExpensesByCurrencyAsync GetLatestRateAsync ConvertAsync GetRateByDateAsync GetRateHistoryAsync GetCurrentBuyRateAsync GetCurrentSellRateAsync GetDocumentsByEntityAsync GetDocumentByIdAsync GetDocumentsByTypeAsync GetEntriesByTransactionAsync GetEntriesByAccountAsync GetEntriesByDateRangeAsync GetAccountBalancesAsync GetCustomerLedgerAsync GetCorrespondentLedgerAsync GetTrialBalanceAsync GetDailyReportAsync GetDailyReportRangeAsync GetTransactionReportAsync GetCommissionReportAsync GetCustomerTransactionReportAsync GetCorrespondentTransactionReportAsync GetTotalCommissionAsync GetTotalExpensesAsync GetNetProfitAsync GetBalanceSheetAsync GetProfitLossStatementAsync GetDashboardSummaryAsync GetByAccountAndCurrencyAsync GetByAccountAsync GetByCurrencyAsync GetActiveLimitsAsync GetSettingsAsync GetSettingAsync GetActiveAlertsAsync GetRecipientUsersAsync GetLogsByTableAsync GetLogsByUserAsync GetLogsByDateRangeAsync GetLogsByActionAsync GetAllLogsAsync GetFilteredLogsAsync GetFilteredLogsNoPagingAsync GetDistinctActionsAsync GetDistinctTablesAsync GetAllCustomerBalancesAsync GetCustomerBalanceAsync GetAllCorrespondentBalancesAsync GetCorrespondentBalanceAsync GetAllCashBalancesAsync GetCashBalanceAsync GetBranchBalanceAsync ValidateBadehkarLimitAsync GetAccountsWithLimitsAsync GetDailyJournalAsync GetJournalAsync GetAccountOperationsAsync GetAccountOperationsPageAsync GetAccountOperationDetailsAsync GetCashDailyBalancesAsync GetCurrentCashBalancesAsync GetDailyCommissionRateAsync FindByNameOrAliasAsync GetCustomerActivitiesPreviewAsync ReadAsync");
        Add("Create","CreateAsync CreateHawalaAsync CreateHawalasAsync CreateExpenseAsync CreateTransferAsync CreateLedgerEntryAsync UploadDocumentAsync ProcessHawalaSendAsync ProcessHawalaReceiveAsync ProcessExchangeAsync EnqueueAsync RetryAsync SaveDraftAsync ConfirmAsync PrepareLocationsAsync");
        Add("Edit","UpdateAsync UpdateHawalaAsync UpdateExpenseAsync UpdateTransferAsync ArchiveAsync UnarchiveAsync ActivateAsync DeactivateAsync SetActiveAsync ToggleActiveAsync SetForAccountAsync AddOpeningBalancesAsync SaveAsync SaveConfirmedAsync UpdateHawalaRateAsync AssignAsync SaveCashOpeningBalancesAsync SaveDailyCommissionRateAsync AddAgentCommissionAsync");
        Add("Delete","DeleteAsync DeleteHawalaAsync DeleteExpenseAsync DeleteTransferAsync DeleteDocumentAsync DeleteBatchAsync CancelHawalaAsync CancelTransactionAsync ReverseTransactionAsync CancelAsync");
        Add("Reverse","ReverseAsync ReverseConversionAsync");
        Add("Export","ExportAsync ExportInputFormatAsync DownloadDocumentAsync ExportCustomerActivitiesAsync ExportCustomerActivitiesPreviewAsync");
        Actions["ReadAsync"]="Export";
        Actions["GetDeletionPreviewAsync"]="Delete";
        Actions["ConfirmQueuedAsync"]="Create";
    }
    private static void Add(string action,string names) { foreach(var name in names.Split(' '))Actions.Add(name,action); }
    public static string Resolve(string service,string method,string? overrideModule=null)
    {
        var module=overrideModule ?? Modules.GetValueOrDefault(service) ?? throw new UnauthorizedAccessException("Unmapped service");
        var action=Actions.GetValueOrDefault(method);
        if(service=="ICompanySettingService" && method=="SaveAsync")action="Edit";
        if(service=="IHawalaImportService" && method=="PreviewAsync")action="Create";
        if(service=="ICorrespondentCommissionService" && method is "PostAsync" or "PreviewAsync")action="Calculate";
        if(service=="ICorrespondentSettlementService" && method is "ConvertHawalasAsync" or "ConvertBalanceAsync")action="Create";
        if(service=="IAedDealService" && method=="ConvertAsync")action="Edit";
        if(service=="IHawalaService" && method is "MarkAsPaidAsync" or "PreviewBulkPaymentAsync" or "PayBulkAsync") { module="HawalaExecution"; action="Execute"; }
        if(service=="ITransactionService" && method=="MarkAsPaidAsync") { module="HawalaExecution"; action="Execute"; }
        if(service=="IHawalaService" && method is "GetPaymentHistoryAsync" or "GetPaymentBatchAsync") { module="HawalaExecution"; action="View"; }
        if(service=="ICorrespondentService" && method is "ClosePeriodAsync" or "GetPeriodClosePreviewAsync" or "GetPeriodsAsync" or "GetPeriodAsync") { module="Periods"; action=method=="ClosePeriodAsync"?"Close":"View"; }
        if(service=="IJournalService" && method=="CloseCashDayAsync")action="Close";
        if(action is null)throw new UnauthorizedAccessException("Unmapped operation: "+service+"."+method);
        var key=$"{module}.{action}";
        if(!PermissionCatalog.Keys.Contains(key))throw new UnauthorizedAccessException("Unmapped permission: "+key);
        return key;
    }
}
