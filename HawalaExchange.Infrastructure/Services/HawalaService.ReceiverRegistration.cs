using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HawalaExchange.Application.Services;

public partial class HawalaService
{
    private async Task RegisterPaidReceiverAsync(PayHawalaDto payment)
    {
        // The payment transaction owns this tenant-specific lock. Concurrent payments
        // cannot both insert the same receiver; a rollback removes both payment and customer.
        if (_context.CurrentTenantId <= 0 || _context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("ثبت گیرنده باید در تراکنش صرافی جاری انجام شود.");
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)_context.Database.CurrentTransaction.GetDbTransaction();
        command.CommandTimeout = 35;
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @result;";
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        { Value = $"RegisteredReceiver:{_context.CurrentTenantId}" });
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) < 0)
            throw new InvalidOperationException("ثبت گیرنده در حال انجام است؛ لطفاً دوباره تلاش کنید.");

        var name = payment.ReceiverName.Trim();
        var father = EmptyToNull(payment.ReceiverFatherName);
        var phone = EmptyToNull(payment.ReceiverPhone);
        var tazkira = EmptyToNull(payment.ReceiverTazkiraNumber);
        // Strong ID first; phone/name fallback only when one side has no ID.
        // Names alone must not merge people with different known identity numbers.
        var exists = await _context.Customers.AnyAsync(x =>
            (tazkira != null && x.TazkiraNumber == tazkira) ||
            ((tazkira == null || x.TazkiraNumber == null || x.TazkiraNumber == "") &&
                x.FullName == name &&
                ((phone != null && x.PhoneNumber == phone) ||
                 (phone == null && (x.PhoneNumber == null || x.PhoneNumber == "") &&
                    (x.FatherName == father || (father == null && x.FatherName == ""))))));
        if (exists) return; // Never change an existing person's identity or financial accounts.

        if (name.Length > 200 || father?.Length > 200 || phone?.Length > 50 || tazkira?.Length > 50 ||
            payment.ReceiverAddress?.Length > 500 || payment.ReceiverTazkiraImagePath?.Length > 500)
            throw new InvalidOperationException("طول مشخصات گیرنده بیشتر از حد مجاز است.");
        _context.Customers.Add(new Customer
        {
            FullName = name, FatherName = father, PhoneNumber = phone, TazkiraNumber = tazkira,
            Address = EmptyToNull(payment.ReceiverAddress), TazkiraImagePath = payment.ReceiverTazkiraImagePath,
            CustomerCode = $"RCV-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow
        });
        // Intentionally no Account, opening balance, debt limit, or ledger entry.
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
