using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YourNamespace.Entities
{
    [Table("Documents")]
    public class Document
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string EntityType { get; set; }

        [Required]
        public long EntityId { get; set; }

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; }

        [Required]
        [MaxLength(1000)]
        public string FilePath { get; set; }

        [MaxLength(100)]
        public string ContentType { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}