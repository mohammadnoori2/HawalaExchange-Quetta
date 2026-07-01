using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class CustomerDto
    {
        public long Id { get; set; }
        public string CustomerCode { get; set; }
        public string FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
        public string? Remarks { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateCustomerDto
    {
        public string FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? Address { get; set; }
        public string? Remarks { get; set; }

        public bool HasInitialBalance { get; set; } = false;
        public List<InitialBalanceDto>? InitialBalances { get; set; }
    }



    public class UpdateCustomerDto
    {
        public string FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
        public string? Remarks { get; set; }
    }
}
