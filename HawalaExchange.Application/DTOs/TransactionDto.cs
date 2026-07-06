using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class TransactionDto
    {
        public long Id { get; set; }
        public string TransactionNo { get; set; }
        public string TransactionType { get; set; }
        public long BranchId { get; set; }
        public string BranchName { get; set; }
        public long? CustomerId { get; set; }
        public string? CustomerFullName { get; set; }
        public string Status { get; set; }
        public string? Remarks { get; set; }
        public long CreatedBy { get; set; }
        public string CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public long? CancelledBy { get; set; }
        public string? CancelledByName { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }
        public long? ReversedTransactionId { get; set; }

        public List<TransactionDetailDto>? TransactionDetails { get; set; }
        public List<LedgerEntryDto>? LedgerEntries { get; set; }
        public List<TransferDto>? Transfers { get; set; }
        public List<ExpenseDto>? Expenses { get; set; }
    }

    public class CreateTransactionDto
    {
        public string TransactionType { get; set; }
        public long BranchId { get; set; }
        public long? CustomerId { get; set; }
        public string? CustomerFullName { get; set; }
        public string? Remarks { get; set; }
        public List<CreateTransactionDetailDto> TransactionDetails { get; set; }
        public List<CreateTransferDto>? Transfers { get; set; }
        public List<CreateExpenseDto>? Expenses { get; set; }
    }

    public class UpdateTransactionDto
    {
        public string? Remarks { get; set; }
        public List<CreateTransactionDetailDto>? TransactionDetails { get; set; }
        public List<CreateTransferDto>? Transfers { get; set; }
        public List<CreateExpenseDto>? Expenses { get; set; }
    }

    public class CancelTransactionDto
    {
        public string CancelReason { get; set; }
    }
}
