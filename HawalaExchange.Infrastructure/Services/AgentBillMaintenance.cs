using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace HawalaExchange.Infrastructure.Services;
internal static class AgentBillMaintenance
{
    public static async Task CancelUnpaidAsync(ApplicationDbContext db, IReadOnlyCollection<long> ids, CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null) throw new InvalidOperationException("بیل باید در تراکنش حواله اصلاح شود.");
        var bills=await db.AgentPaymentBills.Include(x => x.Items)
            .Where(x => x.Status=="Pending" && x.Items.Any(i => ids.Contains(i.HawalaId))).ToListAsync(token);
        foreach(var bill in bills) {
            bill.Status="Cancelled"; bill.CancelledAt=DateTime.UtcNow; bill.CancelledBy=db.RequireCurrentUserId();
            foreach(var item in bill.Items) item.IsActive=false;
        }
        if(bills.Count>0) await db.SaveChangesAsync(token);
    }
}
