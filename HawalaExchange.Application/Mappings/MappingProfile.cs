using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaSystem.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // ===== Branch =====
            CreateMap<Branch, BranchDto>().ReverseMap();
            CreateMap<CreateBranchDto, Branch>();
            CreateMap<UpdateBranchDto, Branch>();

            // ===== User =====
            CreateMap<ApplicationUser, UserDto>()
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.LocalUserName))
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name));
            CreateMap<CreateUserDto, ApplicationUser>()
                .ForMember(dest => dest.UserName, opt => opt.Ignore())
                .ForMember(dest => dest.LocalUserName, opt => opt.MapFrom(src => src.UserName));
            CreateMap<UpdateUserDto, ApplicationUser>()
                .ForMember(dest => dest.UserName, opt => opt.Ignore())
                .ForMember(dest => dest.LocalUserName, opt => opt.Ignore());

            // ===== Customer =====
            CreateMap<Customer, CustomerDto>().ReverseMap();
            CreateMap<CreateCustomerDto, Customer>();
            CreateMap<UpdateCustomerDto, Customer>();

            // ===== Correspondent =====
            CreateMap<Correspondent, CorrespondentDto>().ReverseMap();
            CreateMap<CreateCorrespondentDto, Correspondent>();
            CreateMap<UpdateCorrespondentDto, Correspondent>();

            // ===== Currency =====
            CreateMap<Currency, CurrencyDto>().ReverseMap();
            CreateMap<CreateCurrencyDto, Currency>();
            CreateMap<UpdateCurrencyDto, Currency>();

            // ===== Account =====
            CreateMap<Account, AccountDto>();
            CreateMap<CreateAccountDto, Account>()
                .ForMember(dest => dest.ReferenceType, opt => opt.Ignore())
                .ForMember(dest => dest.ReferenceId, opt => opt.Ignore())
                .ForMember(dest => dest.CustomerId, opt => opt.MapFrom(src =>
                    src.ReferenceType == "Customer" ? src.ReferenceId : null))
                .ForMember(dest => dest.CorrespondentId, opt => opt.MapFrom(src =>
                    src.ReferenceType == "Correspondent" ? src.ReferenceId : null));
            CreateMap<UpdateAccountDto, Account>()
                .ForMember(dest => dest.ReferenceType, opt => opt.Ignore())
                .ForMember(dest => dest.ReferenceId, opt => opt.Ignore())
                .ForMember(dest => dest.CustomerId, opt => opt.MapFrom(src =>
                    src.ReferenceType == "Customer" ? src.ReferenceId : null))
                .ForMember(dest => dest.CorrespondentId, opt => opt.MapFrom(src =>
                    src.ReferenceType == "Correspondent" ? src.ReferenceId : null));

            // ===== ExchangeRate =====
            CreateMap<ExchangeRate, ExchangeRateDto>()
                .ForMember(dest => dest.FromCurrencyCode, opt => opt.MapFrom(src => src.FromCurrency.Code))
                .ForMember(dest => dest.ToCurrencyCode, opt => opt.MapFrom(src => src.ToCurrency.Code))
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser.FullName));
            CreateMap<CreateExchangeRateDto, ExchangeRate>();
            CreateMap<UpdateExchangeRateDto, ExchangeRate>();

            // ===== Transaction =====
            CreateMap<Transaction, TransactionDto>()
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser.FullName))
                .ForMember(dest => dest.CancelledByName, opt => opt.MapFrom(src => src.CancelledByUser != null ? src.CancelledByUser.FullName : null));
            CreateMap<CreateTransactionDto, Transaction>();
            CreateMap<UpdateTransactionDto, Transaction>();

            // ===== TransactionDetail =====
            CreateMap<TransactionDetail, TransactionDetailDto>()
                .ForMember(dest => dest.FromCurrencyCode, opt => opt.MapFrom(src => src.FromCurrency.Code))
                .ForMember(dest => dest.ToCurrencyCode, opt => opt.MapFrom(src => src.ToCurrency.Code))
                .ForMember(dest => dest.CommissionCurrencyCode, opt => opt.MapFrom(src => src.CommissionCurrency.Code))
                .ForMember(dest => dest.AgentCommissionCurrencyCode, opt => opt.MapFrom(src => src.AgentCommissionCurrency.Code))
                .ForMember(dest => dest.CorrespondentName, opt => opt.MapFrom(src => src.Correspondent.Name));
            CreateMap<CreateTransactionDetailDto, TransactionDetail>();

            // ===== LedgerEntry =====
            CreateMap<LedgerEntry, LedgerEntryDto>()
                .ForMember(dest => dest.AccountName, opt => opt.MapFrom(src => src.Account.AccountName))
                .ForMember(dest => dest.AccountCode, opt => opt.MapFrom(src => src.Account.AccountCode))
                .ForMember(dest => dest.CurrencyCode, opt => opt.MapFrom(src => src.Currency.Code));
            CreateMap<CreateLedgerEntryDto, LedgerEntry>();

            // ===== Transfer =====
            CreateMap<Transfer, TransferDto>()
                .ForMember(dest => dest.FromAccountName, opt => opt.MapFrom(src => src.FromAccount.AccountName))
                .ForMember(dest => dest.ToAccountName, opt => opt.MapFrom(src => src.ToAccount.AccountName))
                .ForMember(dest => dest.CurrencyCode, opt => opt.MapFrom(src => src.Currency.Code));
            CreateMap<CreateTransferDto, Transfer>();
            CreateMap<UpdateTransferDto, Transfer>();

            // ===== Expense =====
            // ===== Expense =====
            CreateMap<Expense, ExpenseDto>()
                .ForMember(dest => dest.CurrencyCode,
                    opt => opt.MapFrom(src => src.Currency != null ? src.Currency.Code : ""))
                .ForMember(dest => dest.ExpenseAccountName,
                    opt => opt.MapFrom(src => src.ExpenseAccount != null ? src.ExpenseAccount.AccountName : ""))
                .ForMember(dest => dest.PaidFromAccountName,
                    opt => opt.MapFrom(src => src.PaidFromAccount != null ? src.PaidFromAccount.AccountName : ""));

            CreateMap<CreateExpenseDto, Expense>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Currency, opt => opt.Ignore())
                .ForMember(dest => dest.ExpenseAccount, opt => opt.Ignore())
                .ForMember(dest => dest.PaidFromAccount, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());

            CreateMap<UpdateExpenseDto, Expense>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Currency, opt => opt.Ignore())
                .ForMember(dest => dest.ExpenseAccount, opt => opt.Ignore())
                .ForMember(dest => dest.PaidFromAccount, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());

            // ===== Document =====
            CreateMap<Document, DocumentDto>().ReverseMap();

            // ===== AuditLog =====
            CreateMap<AuditLog, AuditLogDto>()
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.FullName));

            // ===== AccountBadehkarLimit =====
            CreateMap<AccountBadehkarLimit, AccountBadehkarLimitDto>()
                .ForMember(dest => dest.AccountName, opt => opt.MapFrom(src => src.Account.AccountName))
                .ForMember(dest => dest.AccountType, opt => opt.MapFrom(src => src.Account.AccountType))
                .ForMember(dest => dest.CurrencyCode, opt => opt.MapFrom(src => src.Currency.Code))
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser.FullName));
            CreateMap<CreateAccountBadehkarLimitDto, AccountBadehkarLimit>();
            CreateMap<UpdateAccountBadehkarLimitDto, AccountBadehkarLimit>();


            // ===== CapitalInvestment =====
            CreateMap<CapitalInvestment, CapitalInvestmentDto>()
                .ForMember(dest => dest.CurrencyCode,
                    opt => opt.MapFrom(src => src.Currency != null ? src.Currency.Code : ""))
                .ForMember(dest => dest.ProfitCurrencyCode,
                    opt => opt.MapFrom(src => src.ProfitCurrency != null ? src.ProfitCurrency.Code : ""))
                .ForMember(dest => dest.ReceivingAccountName,
                    opt => opt.MapFrom(src => src.ReceivingAccount != null ? src.ReceivingAccount.AccountName : ""))
                .ForMember(dest => dest.CapitalAccountName,
                    opt => opt.MapFrom(src => src.CapitalAccount != null ? src.CapitalAccount.AccountName : ""));

            CreateMap<CreateCapitalInvestmentDto, CapitalInvestment>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Currency, opt => opt.Ignore())
                .ForMember(dest => dest.ProfitCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.ReceivingAccount, opt => opt.Ignore())
                .ForMember(dest => dest.CapitalAccount, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());

            CreateMap<UpdateCapitalInvestmentDto, CapitalInvestment>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Currency, opt => opt.Ignore())
                .ForMember(dest => dest.ProfitCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.ReceivingAccount, opt => opt.Ignore())
                .ForMember(dest => dest.CapitalAccount, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());

            // ===== AccountMoneyOperation =====

            // ===== AccountMoneyOperation / واریز برداشت =====
            CreateMap<AccountMoneyOperation, AccountMoneyOperationDto>()
                .ForMember(dest => dest.AccountName,
                    opt => opt.MapFrom(src => src.Account != null ? src.Account.AccountName : ""))
                .ForMember(dest => dest.AccountCode,
                    opt => opt.MapFrom(src => src.Account != null ? src.Account.AccountCode : ""))
                .ForMember(dest => dest.CashOrBankAccountName,
                    opt => opt.MapFrom(src => src.CashOrBankAccount != null ? src.CashOrBankAccount.AccountName : ""))
                .ForMember(dest => dest.CashOrBankAccountCode,
                    opt => opt.MapFrom(src => src.CashOrBankAccount != null ? src.CashOrBankAccount.AccountCode : ""))
                .ForMember(dest => dest.CurrencyCode,
                    opt => opt.MapFrom(src => src.Currency != null ? src.Currency.Code : ""));

            CreateMap<CreateAccountMoneyOperationDto, AccountMoneyOperation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Account, opt => opt.Ignore())
                .ForMember(dest => dest.CashOrBankAccount, opt => opt.Ignore())
                .ForMember(dest => dest.Currency, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());

            CreateMap<UpdateAccountMoneyOperationDto, AccountMoneyOperation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Account, opt => opt.Ignore())
                .ForMember(dest => dest.CashOrBankAccount, opt => opt.Ignore())
                .ForMember(dest => dest.Currency, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());

            // ===== MoneyExchangeOperation / تبدیل پول =====
            CreateMap<MoneyExchangeOperation, MoneyExchangeOperationDto>()
                .ForMember(dest => dest.FromAccountName,
                    opt => opt.MapFrom(src => src.FromAccount != null ? src.FromAccount.AccountName : ""))
                .ForMember(dest => dest.FromAccountCode,
                    opt => opt.MapFrom(src => src.FromAccount != null ? src.FromAccount.AccountCode : ""))
                .ForMember(dest => dest.ToAccountName,
                    opt => opt.MapFrom(src => src.ToAccount != null ? src.ToAccount.AccountName : ""))
                .ForMember(dest => dest.ToAccountCode,
                    opt => opt.MapFrom(src => src.ToAccount != null ? src.ToAccount.AccountCode : ""))
                .ForMember(dest => dest.FromCurrencyCode,
                    opt => opt.MapFrom(src => src.FromCurrency != null ? src.FromCurrency.Code : ""))
                .ForMember(dest => dest.ToCurrencyCode,
                    opt => opt.MapFrom(src => src.ToCurrency != null ? src.ToCurrency.Code : ""))
                .ForMember(dest => dest.ProfitCurrencyCode,
                    opt => opt.MapFrom(src => src.ProfitCurrency != null ? src.ProfitCurrency.Code : ""))
                .ForMember(dest => dest.RateBaseCurrencyCode,
                    opt => opt.MapFrom(src => src.RateBaseCurrency != null ? src.RateBaseCurrency.Code : ""))
                .ForMember(dest => dest.RateQuoteCurrencyCode,
                    opt => opt.MapFrom(src => src.RateQuoteCurrency != null ? src.RateQuoteCurrency.Code : ""));

            CreateMap<CreateMoneyExchangeOperationDto, MoneyExchangeOperation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.FromAccount, opt => opt.Ignore())
                .ForMember(dest => dest.ToAccount, opt => opt.Ignore())
                .ForMember(dest => dest.FromCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.ToCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.ProfitCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.RateBaseCurrencyId, opt => opt.Ignore())
                .ForMember(dest => dest.RateQuoteCurrencyId, opt => opt.Ignore())
                .ForMember(dest => dest.RateBaseCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.RateQuoteCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.CostAmount, opt => opt.Ignore())
                .ForMember(dest => dest.RealizedProfit, opt => opt.Ignore())
                .ForMember(dest => dest.ExchangeProfitAmount, opt => opt.Ignore())
                .ForMember(dest => dest.InventoryCostIncrease, opt => opt.Ignore())
                .ForMember(dest => dest.InventoryCostDecrease, opt => opt.Ignore())
                .ForMember(dest => dest.ShortLiabilityIncrease, opt => opt.Ignore())
                .ForMember(dest => dest.ShortLiabilityDecrease, opt => opt.Ignore())
                .ForMember(dest => dest.DeferredAmount, opt => opt.Ignore())
                .ForMember(dest => dest.ProfitStatus, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());

            CreateMap<UpdateMoneyExchangeOperationDto, MoneyExchangeOperation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedAt, opt => opt.Ignore())
                .ForMember(dest => dest.ModifiedBy, opt => opt.Ignore())
                .ForMember(dest => dest.FromAccount, opt => opt.Ignore())
                .ForMember(dest => dest.ToAccount, opt => opt.Ignore())
                .ForMember(dest => dest.FromCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.ToCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.ProfitCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.RateBaseCurrencyId, opt => opt.Ignore())
                .ForMember(dest => dest.RateQuoteCurrencyId, opt => opt.Ignore())
                .ForMember(dest => dest.RateBaseCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.RateQuoteCurrency, opt => opt.Ignore())
                .ForMember(dest => dest.CostAmount, opt => opt.Ignore())
                .ForMember(dest => dest.RealizedProfit, opt => opt.Ignore())
                .ForMember(dest => dest.ExchangeProfitAmount, opt => opt.Ignore())
                .ForMember(dest => dest.InventoryCostIncrease, opt => opt.Ignore())
                .ForMember(dest => dest.InventoryCostDecrease, opt => opt.Ignore())
                .ForMember(dest => dest.ShortLiabilityIncrease, opt => opt.Ignore())
                .ForMember(dest => dest.ShortLiabilityDecrease, opt => opt.Ignore())
                .ForMember(dest => dest.DeferredAmount, opt => opt.Ignore())
                .ForMember(dest => dest.ProfitStatus, opt => opt.Ignore())
                .ForMember(dest => dest.LedgerEntries, opt => opt.Ignore());
            // ===== Hawala =====
            // Hawala -> HawalaDto
            CreateMap<Hawala, HawalaDto>()
               .ForMember(dest => dest.CorrespondentName, opt => opt.MapFrom(src => src.Correspondent != null ? src.Correspondent.Name : null))
               .ForMember(dest => dest.FromCurrencyCode, opt => opt.MapFrom(src => src.FromCurrency != null ? src.FromCurrency.Code : ""))
               .ForMember(dest => dest.FromCurrencyName, opt => opt.MapFrom(src => src.FromCurrency != null ? src.FromCurrency.Name : ""))
               .ForMember(dest => dest.ToCurrencyCode, opt => opt.MapFrom(src => src.ToCurrency != null ? src.ToCurrency.Code : ""))
               .ForMember(dest => dest.ToCurrencyName, opt => opt.MapFrom(src => src.ToCurrency != null ? src.ToCurrency.Name : ""))
               .ForMember(dest => dest.CommissionCurrencyCode, opt => opt.MapFrom(src => src.CommissionCurrency != null ? src.CommissionCurrency.Code : null))
               .ForMember(dest => dest.CommissionCurrencyName, opt => opt.MapFrom(src => src.CommissionCurrency != null ? src.CommissionCurrency.Name : null))
               .ForMember(dest => dest.AgentCommissionCurrencyCode, opt => opt.MapFrom(src => src.AgentCommissionCurrency != null ? src.AgentCommissionCurrency.Code : null))
               .ForMember(dest => dest.AgentCommissionCurrencyName, opt => opt.MapFrom(src => src.AgentCommissionCurrency != null ? src.AgentCommissionCurrency.Name : null))
               .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser != null ? src.CreatedByUser.FullName : ""))
               .ForMember(dest => dest.HawalaTypeName, opt => opt.Ignore())
               .ForMember(dest => dest.StatusName, opt => opt.Ignore())
               .ForMember(dest => dest.FromAccountId, opt => opt.Ignore())
               .ForMember(dest => dest.GeneratedSendHawalaNumber, opt => opt.Ignore())
               .ForMember(dest => dest.PaymentLocationName, opt => opt.MapFrom(src => src.PaymentLocation != null ? src.PaymentLocation.Name : null))
               .ForMember(dest => dest.PaymentLocationAddress, opt => opt.MapFrom(src => src.PaymentLocation != null ? src.PaymentLocation.Address : null));


            // CreateHawalaDto -> Hawala
            CreateMap<CreateHawalaDto, Hawala>()
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status ?? "Pending"))
                .ForMember(dest => dest.PaymentLocation, opt => opt.Ignore()); // فقط Id ذخیره می‌شود


            // UpdateHawalaDto -> Hawala
            CreateMap<UpdateHawalaDto, Hawala>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

            // در MappingProfile.cs
            CreateMap<PaymentLocation, PaymentLocationDto>()
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser != null ? src.CreatedByUser.FullName : null));

            CreateMap<CreatePaymentLocationDto, PaymentLocation>();
            CreateMap<UpdatePaymentLocationDto, PaymentLocation>();


        }
    }
}
