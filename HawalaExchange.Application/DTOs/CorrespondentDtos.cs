using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class CorrespondentDtos
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public bool IsArchived { get; set; }
        public string Remarks { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateCorrespondentRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public string Remarks { get; set; }
    }

    public class UpdateCorrespondentRequest
    {
        public string Name { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public string Remarks { get; set; }
    }
}
