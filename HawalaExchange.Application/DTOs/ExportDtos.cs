using System;
using System.Collections.Generic;

namespace HawalaExchange.Application.DTOs
{
    public class ExportFilterDto
    {
        public long? CustomerId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public List<string>? TransactionTypes { get; set; }
        public string? CurrencyCode { get; set; }
        public string? Search { get; set; }
        public string? Direction { get; set; }
        public decimal? MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
    }

    public enum ExportFormat
    {
        Excel,
        Pdf
    }
}
