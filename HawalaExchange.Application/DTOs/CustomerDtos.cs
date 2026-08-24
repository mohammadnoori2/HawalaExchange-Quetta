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
        public string? FatherName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? PhotoPath { get; set; }
        public string? TazkiraImagePath { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
        public string? Remarks { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateCustomerDto
    {
        public string FullName { get; set; }
        public string? FatherName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? PhotoPath { get; set; }
        public string? TazkiraImagePath { get; set; }
        public string? Address { get; set; }
        public string? Remarks { get; set; }

        public bool OpenAccount { get; set; }

        public bool HasInitialBalance { get; set; } = false;
        public List<InitialBalanceDto>? InitialBalances { get; set; }
        public List<DebtLimitInputDto> DebtLimits { get; set; } = new();
    }



    public class UpdateCustomerDto
    {
        public string FullName { get; set; }
        public string? FatherName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? TazkiraNumber { get; set; }
        public string? PhotoPath { get; set; }
        public string? TazkiraImagePath { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
        public string? Remarks { get; set; }
    }
}
