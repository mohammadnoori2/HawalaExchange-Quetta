using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HawalaExchange.Domain.Entities
{

    [Table("Documents")]
    public class Document : ITenantEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TenantId { get; set; }

        public long? TransactionId { get; set; }
        public long? CustomerId { get; set; }
        public long? CorrespondentId { get; set; }
        public long? AccountId { get; set; }

        public virtual Transaction? Transaction { get; set; }
        public virtual Customer? Customer { get; set; }
        public virtual Correspondent? Correspondent { get; set; }
        public virtual Account? Account { get; set; }

        [NotMapped]
        public string EntityType
        {
            get => TransactionId.HasValue ? "Transaction" :
                   CustomerId.HasValue ? "Customer" :
                   CorrespondentId.HasValue ? "Correspondent" :
                   AccountId.HasValue ? "Account" : string.Empty;
            set
            {
                TransactionId = value == "Transaction" ? TransactionId : null;
                CustomerId = value == "Customer" ? CustomerId : null;
                CorrespondentId = value == "Correspondent" ? CorrespondentId : null;
                AccountId = value == "Account" ? AccountId : null;
            }
        }

        [NotMapped]
        public long EntityId
        {
            get => TransactionId ?? CustomerId ?? CorrespondentId ?? AccountId ?? 0;
            set
            {
                if (EntityType == "Transaction") TransactionId = value;
                else if (EntityType == "Customer") CustomerId = value;
                else if (EntityType == "Correspondent") CorrespondentId = value;
                else if (EntityType == "Account") AccountId = value;
            }
        }

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; }

        [Required]
        [MaxLength(1000)]
        public string FilePath { get; set; }

        [MaxLength(100)]
        public string? ContentType { get; set; }

        public long FileSizeBytes { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
