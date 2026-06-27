using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class AuditLogDto
    {
        public long Id { get; set; }
        public long? UserId { get; set; }
        public string? UserName { get; set; }
        public string Action { get; set; }
        public string TableName { get; set; }
        public long RecordId { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
