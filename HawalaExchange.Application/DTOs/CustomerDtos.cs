using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class CustomerDto
    {
        public long Id { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? Address { get; set; }
        public string? Remarks { get; set; }
        public bool IsArchived { get; set; }
    }
    public class CreateCustomerRequest
    {
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? Address { get; set; }
        public string? Remarks { get; set; }
    }

    public class UpdateCustomerRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? Address { get; set; }
        public string? Remarks { get; set; }
    }
}
