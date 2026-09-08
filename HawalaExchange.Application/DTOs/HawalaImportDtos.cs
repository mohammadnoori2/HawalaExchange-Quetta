namespace HawalaExchange.Application.DTOs
{
    public class HawalaImportRowDto
    {
        public long Id { get; set; }
        public int ExcelRowNumber { get; set; }
        public long? HawalaNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? SenderName { get; set; }
        public string? ReceiverName { get; set; }
        public string? PaymentLocationText { get; set; }
        public long? PaymentLocationId { get; set; }
        public string? PaymentLocationName { get; set; }
        public decimal? Amount { get; set; }
        public string? CurrencyCode { get; set; }
        public string? ValidationErrors { get; set; }
        public bool IsValid => string.IsNullOrWhiteSpace(ValidationErrors);
    }

    public class HawalaImportTotalDto
    {
        public string CurrencyCode { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public decimal Amount { get; set; }
    }

    public class HawalaImportPreviewDto
    {
        public long BatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string CorrespondentName { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public int ValidRowCount { get; set; }
        public int InvalidRowCount { get; set; }
        public List<string> MissingLocations { get; set; } = [];
        public List<HawalaImportTotalDto> Totals { get; set; } = [];
        public List<HawalaImportRowDto> Rows { get; set; } = [];
    }

    public class ConfirmHawalaImportDto
    {
        public long BatchId { get; set; }
        public List<string> LocationsToCreate { get; set; } = [];
    }

    public class HawalaImportResultDto
    {
        public long BatchId { get; set; }
        public int ImportedCount { get; set; }
        public List<HawalaImportTotalDto> Totals { get; set; } = [];
    }
}
