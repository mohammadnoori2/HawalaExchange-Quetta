using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class TransferDto
    {
        public long Id { get; set; }
        public long FromAccountId { get; set; }
        public string FromAccountName { get; set; }
        public long ToAccountId { get; set; }
        public string ToAccountName { get; set; }
        public long CurrencyId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal Amount { get; set; }
        public long? ProfitCurrencyId { get; set; }
        public string? ProfitCurrencyCode { get; set; }
        public decimal? ProfitCurrencyAmount { get; set; }
        public string TransferMethod { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }
    }

    public class CreateTransferDto
    {

        public long FromAccountId { get; set; }
        public long ToAccountId { get; set; }
        public long CurrencyId { get; set; }
        public decimal Amount { get; set; }
        public long? ProfitCurrencyId { get; set; }
        public decimal? ProfitCurrencyAmount { get; set; }
        public string TransferMethod { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }
    }

    public class UpdateTransferDto
    {
        public long FromAccountId { get; set; }
        public long ToAccountId { get; set; }
        public long CurrencyId { get; set; }
        public decimal Amount { get; set; }
        public long? ProfitCurrencyId { get; set; }
        public decimal? ProfitCurrencyAmount { get; set; }
        public string TransferMethod { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }
    }
}
