using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HawalaExchange.Application.Services
{
    public class TransactionService : BaseService<Transaction, TransactionDto, CreateTransactionDto, UpdateTransactionDto>, ITransactionService
    {
        private readonly ILedgerService _ledgerService;
        private readonly IAccountService _accountService;
        private readonly IExchangeRateService _exchangeRateService;
        private readonly IAuditLogService _auditLogService;

        public TransactionService(
            ApplicationDbContext context,
            IMapper mapper,
            ILedgerService ledgerService,
            IAccountService accountService,
            IExchangeRateService exchangeRateService,
            IAuditLogService auditLogService)
            : base(context, mapper)
        {
            _ledgerService = ledgerService;
            _accountService = accountService;
            _exchangeRateService = exchangeRateService;
            _auditLogService = auditLogService;
        }

        // ✅ Override CreateAsync to set CreatedBy
        public override async Task<TransactionDto> CreateAsync(CreateTransactionDto createDto)
        {
            var transaction = _mapper.Map<Transaction>(createDto);
            transaction.TransactionNo = await GenerateTransactionNumberAsync(createDto.TransactionType);
            transaction.Status = "Pending";
            transaction.CreatedAt = DateTime.UtcNow;
            transaction.CreatedBy = 1; // ✅ Set to current user ID (hardcoded for now)

            await _dbSet.AddAsync(transaction);
            await _context.SaveChangesAsync();

            if (createDto.TransactionDetails != null)
            {
                foreach (var detailDto in createDto.TransactionDetails)
                {
                    var detail = _mapper.Map<TransactionDetail>(detailDto);
                    detail.TransactionId = transaction.Id;
                    await _context.TransactionDetails.AddAsync(detail);
                }
                await _context.SaveChangesAsync();
            }

            await GenerateLedgerEntries(transaction);
            await _auditLogService.LogAsync("CREATE", "Transactions", transaction.Id, null, "Created", transaction.CreatedBy);

            return _mapper.Map<TransactionDto>(transaction);
        }

        // ===== Custom Query Methods =====
        public async Task<TransactionDto?> GetByTransactionNoAsync(string transactionNo)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(t => t.TransactionNo == transactionNo);
            return entity == null ? null : _mapper.Map<TransactionDto>(entity);
        }

        public async Task<IEnumerable<TransactionDto>> GetByBranchAsync(long branchId)
        {
            var transactions = await _dbSet
                .Where(t => t.BranchId == branchId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransactionDto>>(transactions);
        }

        public async Task<IEnumerable<TransactionDto>> GetByCustomerAsync(long customerId)
        {
            var transactions = await _dbSet
                .Where(t => t.CustomerId == customerId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransactionDto>>(transactions);
        }

        public async Task<IEnumerable<TransactionDto>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            var transactions = await _dbSet
                .Where(t => t.CreatedAt >= fromDate && t.CreatedAt <= toDate)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransactionDto>>(transactions);
        }

        public async Task<IEnumerable<TransactionDto>> GetByStatusAsync(string status)
        {
            var transactions = await _dbSet
                .Where(t => t.Status == status)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransactionDto>>(transactions);
        }

        public async Task<IEnumerable<TransactionDto>> GetPendingTransactionsAsync()
        {
            return await GetByStatusAsync("Pending");
        }

        // ===== Business Operations =====
        public async Task<TransactionDto> MarkAsPaidAsync(long id)
        {
            var transaction = await _dbSet.FindAsync(id);
            if (transaction == null)
                throw new KeyNotFoundException($"Transaction with ID {id} not found.");

            if (transaction.Status != "Pending")
                throw new InvalidOperationException("Only pending transactions can be marked as paid.");

            transaction.Status = "Paid";
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync("UPDATE", "Transactions", id, "Pending", "Paid", transaction.CreatedBy);
            return _mapper.Map<TransactionDto>(transaction);
        }

        public async Task<TransactionDto> CancelTransactionAsync(long id, CancelTransactionDto cancelDto)
        {
            var transaction = await _dbSet.FindAsync(id);
            if (transaction == null)
                throw new KeyNotFoundException($"Transaction with ID {id} not found.");

            if (transaction.Status == "Cancel")
                throw new InvalidOperationException("Transaction is already cancelled.");

            transaction.Status = "Cancel";
            transaction.CancelledAt = DateTime.UtcNow;
            transaction.CancelReason = cancelDto.CancelReason;
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync("CANCEL", "Transactions", id, "Active", "Cancelled", transaction.CreatedBy);
            return _mapper.Map<TransactionDto>(transaction);
        }

        public async Task<TransactionDto> ReverseTransactionAsync(long id, CancelTransactionDto cancelDto)
        {
            var transaction = await _dbSet.FindAsync(id);
            if (transaction == null)
                throw new KeyNotFoundException($"Transaction with ID {id} not found.");

            var reversalTransaction = new Transaction
            {
                TransactionNo = await GenerateTransactionNumberAsync("ADJ"),
                TransactionType = "Adjustment",
                BranchId = transaction.BranchId,
                CustomerId = transaction.CustomerId,
                CustomerFullName = transaction.CustomerFullName,
                Status = "Pending",
                Remarks = $"Reversal of {transaction.TransactionNo}: {cancelDto.CancelReason}",
                CreatedBy = transaction.CreatedBy,
                CreatedAt = DateTime.UtcNow,
                ReversedTransactionId = id
            };

            await _dbSet.AddAsync(reversalTransaction);
            await _context.SaveChangesAsync();

            var entries = await _ledgerService.GetEntriesByTransactionAsync(id);
            foreach (var entry in entries)
            {
                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                {
                    AccountId = entry.AccountId,
                    CurrencyId = entry.CurrencyId,
                    TalabKar = entry.BadehKar,
                    BadehKar = entry.TalabKar,
                    Description = $"Reversal: {entry.Description ?? "No description"}"
                });
            }

            await _auditLogService.LogAsync("REVERSE", "Transactions", id, "Active", "Reversed", transaction.CreatedBy);
            return _mapper.Map<TransactionDto>(reversalTransaction);
        }

        public async Task<TransactionDto> ProcessHawalaSendAsync(CreateTransactionDto createDto)
        {
            createDto.TransactionType = "HawalaSend";
            return await CreateAsync(createDto);
        }

        public async Task<TransactionDto> ProcessHawalaReceiveAsync(CreateTransactionDto createDto)
        {
            createDto.TransactionType = "HawalaReceive";
            return await CreateAsync(createDto);
        }

        public async Task<TransactionDto> ProcessExchangeAsync(CreateTransactionDto createDto)
        {
            createDto.TransactionType = "Exchange";
            return await CreateAsync(createDto);
        }

        public async Task<string> GenerateTransactionNumberAsync(string transactionType)
        {
            var prefix = transactionType switch
            {
                "HawalaSend" => "HSN",
                "HawalaReceive" => "HRC",
                "Exchange" => "EXC",
                "Transfer" => "TRF",
                "Expense" => "EXP",
                "Adjustment" => "ADJ",
                _ => "TXN"
            };

            var count = await _dbSet.CountAsync() + 1;
            return $"{prefix}-{DateTime.Now:yyyyMMdd}-{count:D4}";
        }

        // ===== Private Helpers =====
        private async Task GenerateLedgerEntries(Transaction transaction)
        {
            var details = await _context.TransactionDetails
                .Where(d => d.TransactionId == transaction.Id)
                .ToListAsync();

            foreach (var detail in details)
            {
                var cashAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountType == "Cash" && a.ReferenceId == transaction.BranchId);

                var customerAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.ReferenceType == "Customer" && a.ReferenceId == transaction.CustomerId);

                var correspondentAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.ReferenceType == "Correspondent" && a.ReferenceId == detail.CorrespondentId);

                var commissionAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountType == "Income" && a.AccountName.Contains("Commission"));

                switch (transaction.TransactionType)
                {
                    case "HawalaSend":
                        if (customerAccount != null && detail.FromAmount.HasValue)
                        {
                            await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                            {
                                AccountId = customerAccount?.Id ?? cashAccount.Id,
                                CurrencyId = detail.FromCurrencyId ?? 0,
                                TalabKar = detail.FromAmount.Value,
                                BadehKar = 0,
                                Description = $"Hawala Send from {detail.SenderName}"
                            });

                            if (correspondentAccount != null && detail.ToAmount.HasValue)
                            {
                                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                                {
                                    AccountId = correspondentAccount.Id,
                                    CurrencyId = detail.ToCurrencyId ?? 0,
                                    TalabKar = 0,
                                    BadehKar = detail.ToAmount.Value,
                                    Description = $"Hawala Send to {detail.ReceiverName}"
                                });
                            }

                            if (commissionAccount != null && detail.CommissionAmount > 0)
                            {
                                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                                {
                                    AccountId = commissionAccount.Id,
                                    CurrencyId = detail.CommissionCurrencyId ?? 0,
                                    TalabKar = detail.CommissionAmount,
                                    BadehKar = 0,
                                    Description = $"Commission for Hawala Send"
                                });
                            }
                        }
                        break;

                    case "HawalaReceive":
                        if (correspondentAccount != null && detail.FromAmount.HasValue)
                        {
                            await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                            {
                                AccountId = correspondentAccount.Id,
                                CurrencyId = detail.FromCurrencyId ?? 0,
                                TalabKar = detail.FromAmount.Value,
                                BadehKar = 0,
                                Description = $"Hawala Receive from {detail.SenderName}"
                            });

                            if (customerAccount != null && detail.ToAmount.HasValue)
                            {
                                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                                {
                                    AccountId = customerAccount?.Id ?? cashAccount.Id,
                                    CurrencyId = detail.ToCurrencyId ?? 0,
                                    TalabKar = 0,
                                    BadehKar = detail.ToAmount.Value,
                                    Description = $"Hawala Receive for {detail.ReceiverName}"
                                });
                            }

                            if (commissionAccount != null && detail.CommissionAmount > 0)
                            {
                                await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                                {
                                    AccountId = commissionAccount.Id,
                                    CurrencyId = detail.CommissionCurrencyId ?? 0,
                                    TalabKar = detail.CommissionAmount,
                                    BadehKar = 0,
                                    Description = $"Commission for Hawala Receive"
                                });
                            }
                        }
                        break;

                    case "Exchange":
                        if (cashAccount != null)
                        {
                            await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                            {
                                AccountId = cashAccount.Id,
                                CurrencyId = detail.FromCurrencyId ?? 0,
                                TalabKar = detail.FromAmount ?? 0,
                                BadehKar = 0,
                                Description = $"Exchange from {detail.FromCurrency?.Code}"
                            });

                            await _ledgerService.CreateLedgerEntryAsync(new CreateLedgerEntryDto
                            {
                                AccountId = cashAccount.Id,
                                CurrencyId = detail.ToCurrencyId ?? 0,
                                TalabKar = 0,
                                BadehKar = detail.ToAmount ?? 0,
                                Description = $"Exchange to {detail.ToCurrency?.Code}"
                            });
                        }
                        break;
                }
            }
        }

        
    }
}