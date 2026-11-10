using System.ComponentModel.DataAnnotations;
namespace HawalaExchange.Domain.Entities;
public sealed class AgentPaymentBill : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long CorrespondentId { get; set; }
    public Correspondent Correspondent { get; set; } = null!;
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    public DateTime? PaidAt { get; set; }
    public long? PaidBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? CancelledBy { get; set; }
    public string SnapshotJson { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public List<AgentPaymentBillItem> Items { get; set; } = [];
}
public sealed class AgentPaymentBillItem : ITenantEntity
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long BillId { get; set; }
    public AgentPaymentBill Bill { get; set; } = null!;
    public long HawalaId { get; set; }
    public Hawala Hawala { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
