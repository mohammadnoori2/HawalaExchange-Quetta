using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class ExpenseDto
    {
        public long Id { get; set; }
        public long TransactionId { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string Title { get; set; }
        public long CurrencyId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
    }

    public class CreateExpenseDto
    {
        public DateTime ExpenseDate { get; set; }
        public string Title { get; set; }
        public long CurrencyId { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
    }
}
