using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaSystem.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Branch mappings
            CreateMap<Branch, BranchDto>().ReverseMap();
            CreateMap<CreateBranchDto, Branch>();
            CreateMap<UpdateBranchDto, Branch>();

            // User mappings
            CreateMap<User, UserDto>()
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name));
            CreateMap<CreateUserDto, User>();
            CreateMap<UpdateUserDto, User>();

            // Customer mappings
            CreateMap<Customer, CustomerDto>().ReverseMap();
            CreateMap<CreateCustomerDto, Customer>();
            CreateMap<UpdateCustomerDto, Customer>();

            // Correspondent mappings
            CreateMap<Correspondent, CorrespondentDto>().ReverseMap();
            CreateMap<CreateCorrespondentDto, Correspondent>();
            CreateMap<UpdateCorrespondentDto, Correspondent>();

            // Currency mappings
            CreateMap<Currency, CurrencyDto>().ReverseMap();
            CreateMap<CreateCurrencyDto, Currency>();
            CreateMap<UpdateCurrencyDto, Currency>();

            // Account mappings
            CreateMap<Account, AccountDto>().ReverseMap();
            CreateMap<CreateAccountDto, Account>();
            CreateMap<UpdateAccountDto, Account>();

            // ExchangeRate mappings
            CreateMap<ExchangeRate, ExchangeRateDto>()
                .ForMember(dest => dest.FromCurrencyCode, opt => opt.MapFrom(src => src.FromCurrency.Code))
                .ForMember(dest => dest.ToCurrencyCode, opt => opt.MapFrom(src => src.ToCurrency.Code))
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser.FullName));
            CreateMap<CreateExchangeRateDto, ExchangeRate>();
            CreateMap<UpdateExchangeRateDto, ExchangeRate>();

            // Transaction mappings
            CreateMap<Transaction, TransactionDto>()
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.Name))
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser.FullName))
                .ForMember(dest => dest.CancelledByName, opt => opt.MapFrom(src => src.CancelledByUser != null ? src.CancelledByUser.FullName : null));
            CreateMap<CreateTransactionDto, Transaction>();
            CreateMap<UpdateTransactionDto, Transaction>();

            // TransactionDetail mappings
            CreateMap<TransactionDetail, TransactionDetailDto>()
                .ForMember(dest => dest.FromCurrencyCode, opt => opt.MapFrom(src => src.FromCurrency.Code))
                .ForMember(dest => dest.ToCurrencyCode, opt => opt.MapFrom(src => src.ToCurrency.Code))
                .ForMember(dest => dest.CommissionCurrencyCode, opt => opt.MapFrom(src => src.CommissionCurrency.Code))
                .ForMember(dest => dest.AgentCommissionCurrencyCode, opt => opt.MapFrom(src => src.AgentCommissionCurrency.Code))
                .ForMember(dest => dest.CorrespondentName, opt => opt.MapFrom(src => src.Correspondent.Name));
            CreateMap<CreateTransactionDetailDto, TransactionDetail>();

            // LedgerEntry mappings
            CreateMap<LedgerEntry, LedgerEntryDto>()
                .ForMember(dest => dest.AccountName, opt => opt.MapFrom(src => src.Account.AccountName))
                .ForMember(dest => dest.AccountCode, opt => opt.MapFrom(src => src.Account.AccountCode))
                .ForMember(dest => dest.CurrencyCode, opt => opt.MapFrom(src => src.Currency.Code));
            CreateMap<CreateLedgerEntryDto, LedgerEntry>();

            // Transfer mappings
            CreateMap<Transfer, TransferDto>()
                .ForMember(dest => dest.FromAccountName, opt => opt.MapFrom(src => src.FromAccount.AccountName))
                .ForMember(dest => dest.ToAccountName, opt => opt.MapFrom(src => src.ToAccount.AccountName))
                .ForMember(dest => dest.CurrencyCode, opt => opt.MapFrom(src => src.Currency.Code));
            CreateMap<CreateTransferDto, Transfer>();

            // Expense mappings
            CreateMap<Expense, ExpenseDto>()
                .ForMember(dest => dest.CurrencyCode, opt => opt.MapFrom(src => src.Currency.Code));
            CreateMap<CreateExpenseDto, Expense>();

            // Document mappings
            CreateMap<Document, DocumentDto>().ReverseMap();

            // AuditLog mappings
            CreateMap<AuditLog, AuditLogDto>()
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.FullName));

            // AccountBadehkarLimit mappings
            CreateMap<AccountBadehkarLimit, AccountBadehkarLimitDto>()
                .ForMember(dest => dest.AccountName, opt => opt.MapFrom(src => src.Account.AccountName))
                .ForMember(dest => dest.CurrencyCode, opt => opt.MapFrom(src => src.Currency.Code))
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser.FullName));
            CreateMap<CreateAccountBadehkarLimitDto, AccountBadehkarLimit>();
            CreateMap<UpdateAccountBadehkarLimitDto, AccountBadehkarLimit>();

            // ===== Hawala mappings =====
            // Hawala to HawalaDto
            CreateMap<Hawala, HawalaDto>()
                .ForMember(dest => dest.TransactionNo, opt => opt.MapFrom(src => src.Transaction != null ? src.Transaction.TransactionNo : ""))
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch != null ? src.Branch.Name : ""))
                .ForMember(dest => dest.CustomerFullName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.FullName : null))
                .ForMember(dest => dest.CorrespondentName, opt => opt.MapFrom(src => src.Correspondent != null ? src.Correspondent.Name : null))
                .ForMember(dest => dest.FromCurrencyCode, opt => opt.MapFrom(src => src.FromCurrency != null ? src.FromCurrency.Code : ""))
                .ForMember(dest => dest.ToCurrencyCode, opt => opt.MapFrom(src => src.ToCurrency != null ? src.ToCurrency.Code : ""))
                .ForMember(dest => dest.CommissionCurrencyCode, opt => opt.MapFrom(src => src.CommissionCurrency != null ? src.CommissionCurrency.Code : null))
                .ForMember(dest => dest.AgentCommissionCurrencyCode, opt => opt.MapFrom(src => src.AgentCommissionCurrency != null ? src.AgentCommissionCurrency.Code : null))
                .ForMember(dest => dest.CreatedByName, opt => opt.MapFrom(src => src.CreatedByUser != null ? src.CreatedByUser.FullName : ""))
                .ForMember(dest => dest.PaidByName, opt => opt.MapFrom(src => src.PaidByUser != null ? src.PaidByUser.FullName : null))
                .ForMember(dest => dest.CancelledByName, opt => opt.MapFrom(src => src.CancelledByUser != null ? src.CancelledByUser.FullName : null))
                .ForMember(dest => dest.HawalaTypeName, opt => opt.Ignore())
                .ForMember(dest => dest.StatusName, opt => opt.Ignore());

            // CreateHawalaDto to Hawala
            CreateMap<CreateHawalaDto, Hawala>()
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status ?? "Pending"));

            // UpdateHawalaDto to Hawala
            CreateMap<UpdateHawalaDto, Hawala>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
        }
    }
}