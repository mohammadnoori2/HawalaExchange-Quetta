using System;

namespace HawalaExchange.Application.DTOs
{
    public class PaymentLocationDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? ContactPerson { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public long CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
    }

    public class CreatePaymentLocationDto
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? ContactPerson { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdatePaymentLocationDto
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? ContactPerson { get; set; }
        public bool IsActive { get; set; }
    }
}