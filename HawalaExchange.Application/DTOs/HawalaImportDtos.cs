namespace HawalaExchange.Application.DTOs
{
    public class HawalaImportProgressDto
    {
        public int Percent { get; set; }
        public string Message { get; set; } = string.Empty;
    }

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
        public long? CurrencyId { get; set; }
        public bool RequiresOutgoingHawala { get; set; }
        public long? DestinationCorrespondentId { get; set; }
        public string? DestinationCorrespondentName { get; set; }
        public decimal? AgentCommissionAmount { get; set; }
        public long? AgentCommissionCurrencyId { get; set; }
        public string? AgentCommissionCurrencyCode { get; set; }
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
        public string OwnPaymentLocationName { get; set; } = string.Empty;
        public long? OwnPaymentLocationId { get; set; }
        public List<HawalaImportLocationOptionDto> PaymentLocations { get; set; } = [];
        public int RowCount { get; set; }
        public int ValidRowCount { get; set; }
        public int InvalidRowCount { get; set; }
        public List<string> MissingLocations { get; set; } = [];
        public List<string> MissingCorrespondents { get; set; } = [];
        public List<HawalaImportTotalDto> Totals { get; set; } = [];
        public List<HawalaImportRowDto> Rows { get; set; } = [];
    }

    public class ConfirmHawalaImportDto
    {
        public long BatchId { get; set; }
        public string? OwnPaymentLocationName { get; set; }
        public List<HawalaImportLocationMappingDto> LocationMappings { get; set; } = [];
        public List<string> LocationsToCreate { get; set; } = [];
        public List<string> CorrespondentsToCreate { get; set; } = [];
        public List<HawalaImportCommissionDto> Commissions { get; set; } = [];
    }

    public class HawalaImportLocationOptionDto
    {
        public string Name { get; set; } = string.Empty;
        public long? PaymentLocationId { get; set; }
        public long? ResponsibleCorrespondentId { get; set; }
        public string? ResponsibleCorrespondentName { get; set; }
    }

    public class HawalaImportLocationMappingDto
    {
        public string PaymentLocationName { get; set; } = string.Empty;
        public long? CorrespondentId { get; set; }
        public bool CreateCorrespondent { get; set; }
    }

    public class HawalaImportCommissionDto
    {
        public long RowId { get; set; }
        public decimal? Amount { get; set; }
        public long? CurrencyId { get; set; }
    }

    public class HawalaImportResultDto
    {
        public long BatchId { get; set; }
        public int ImportedCount { get; set; }
        public int GeneratedSendCount { get; set; }
        public int MissingCommissionCount { get; set; }
        public List<HawalaImportTotalDto> Totals { get; set; } = [];
    }
}
