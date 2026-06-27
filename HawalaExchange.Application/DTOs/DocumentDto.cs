using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.DTOs
{
    public class DocumentDto
    {
        public long Id { get; set; }
        public string EntityType { get; set; }
        public long EntityId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string? ContentType { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class UploadDocumentDto
    {
        public string EntityType { get; set; }
        public long EntityId { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public byte[] FileContent { get; set; }
    }
}
