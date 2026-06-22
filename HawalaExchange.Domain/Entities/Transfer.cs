using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
{
    [Table("Transfers")]
    public class Transfer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public long TransactionId { get; set; }

        [Required]
        public long FromAccountId { get; set; }

        [Required]
        public long ToAccountId { get; set; }

        [Required]
        public long CurrencyId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransferMethod { get; set; }

        [MaxLength(100)]
        public string ReferenceNumber { get; set; }

        [MaxLength(1000)]
        public string Remarks { get; set; }

        // Navigation Properties
        [ForeignKey("TransactionId")]
        public virtual Transaction Transaction { get; set; }

        [ForeignKey("FromAccountId")]
        public virtual Account FromAccount { get; set; }

        [ForeignKey("ToAccountId")]
        public virtual Account ToAccount { get; set; }

        [ForeignKey("CurrencyId")]
        public virtual Currency Currency { get; set; }
    }
}