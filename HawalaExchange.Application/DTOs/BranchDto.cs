using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class BranchDto
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool CanDelete { get; set; }
    }

    public class CreateBranchDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
    }

    public class UpdateBranchDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsArchived { get; set; }
    }
}
