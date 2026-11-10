using System.Text.Json;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

internal static class HawalaPostingHistoryReader
{
    internal static string StageLabel(string? stage) => stage switch
    {
        "Reversal" => "برگشت ثبت قبلی به دلیل ویرایش حواله",
        "Replacement" => "ثبت جدید پس از ویرایش حواله",
        "Original" => "ثبت قبلی حواله",
        _ => "حواله"
    };

    internal static async Task DecorateAsync(ApplicationDbContext db, List<LedgerEntry> entries, CancellationToken token = default)
    {
        var ids = entries.Where(x => x.HawalaId.HasValue).Select(x => x.HawalaId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return;
        var json = JsonSerializer.Serialize(ids);
        var rows = await (db.Database.IsNpgsql()
            ? db.Database.SqlQuery<PostingMap>($"SELECT \"LedgerId\",\"HawalaId\",\"PostingId\",\"PostingStage\" FROM public.ufn_hawalaledgerpostings_v1({db.CurrentTenantId},{json}) WHERE \"IsCorrected\"=1")
            : db.Database.SqlQuery<PostingMap>($"SELECT LedgerId, HawalaId, PostingId, PostingStage FROM dbo.ufn_HawalaLedgerPostings_v1({db.CurrentTenantId},{json}) WHERE IsCorrected=1")).ToListAsync(token);
        var map = rows.ToDictionary(x => x.LedgerId);
        foreach (var entry in entries)
            if (map.TryGetValue(entry.Id, out var posting))
            {
                entry.ReportingPostingId = posting.PostingId;
                entry.ReportingPostingStage = posting.PostingStage;
            }
    }

    internal static async Task<List<TransactionDto>> ReadTransactionsAsync(ApplicationDbContext db, long? postingId = null)
    {
        var tenant = db.CurrentTenantId;
        var ids = await db.LedgerEntries.AsNoTracking()
            .Where(x => x.HawalaId.HasValue && x.Description != null && x.Description.StartsWith("اصلاح حواله آپلود گروهی — برگشت مانده ثبت قبلی"))
            .Select(x => x.HawalaId!.Value).Distinct().ToArrayAsync();
        if (ids.Length == 0) return [];
        var json = JsonSerializer.Serialize(ids);
        var maps = await (db.Database.IsNpgsql()
            ? db.Database.SqlQuery<PostingMap>($"SELECT \"LedgerId\",\"HawalaId\",\"PostingId\",\"PostingStage\" FROM public.ufn_hawalaledgerpostings_v1({tenant},{json}) WHERE \"IsCorrected\"=1")
            : db.Database.SqlQuery<PostingMap>($"SELECT LedgerId,HawalaId,PostingId,PostingStage FROM dbo.ufn_HawalaLedgerPostings_v1({tenant},{json}) WHERE IsCorrected=1"))
            .Where(x => !postingId.HasValue || x.PostingId == postingId.Value).ToListAsync();
        if (maps.Count == 0) return [];
        var ledgerIds = maps.Select(x => x.LedgerId).ToArray();
        var entries = await db.LedgerEntries.AsNoTracking().Include(x => x.Account).Include(x => x.Currency)
            .Where(x => ledgerIds.Contains(x.Id) && x.TransactionId == null).ToListAsync();
        var byEntry = maps.ToDictionary(x => x.LedgerId);
        var hawalas = await db.Hawalas.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        if (entries.Count == 0) return [];
        var rootIds = hawalas.Values.Select(x => x.SourceHawalaId ?? x.Id).Distinct().ToArray();
        var earliest = entries.Min(x => x.CreatedAt);
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(x => x.TableName == "Hawalas" && x.Action == "UPDATE" && rootIds.Contains(x.RecordId) && x.CreatedAt >= earliest)
            .OrderBy(x => x.CreatedAt).Select(x => new { x.RecordId, x.NewValue, x.UserId }).ToListAsync();
        var userIds = audit.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Concat(hawalas.Values.Select(x => x.CreatedBy)).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName);
        var editors = new Dictionary<long, long>();
        foreach (var log in audit.Where(x => x.UserId.HasValue && x.NewValue != null))
        {
            // Link to exact ledger IDs recorded by the audit graph; never infer an editor from a nearby timestamp.
            try
            {
                var graph = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(log.NewValue!);
                if (graph == null) continue;
                foreach (var key in graph.Keys)
                {
                    var parts = key.Replace("\\u001F", "\u001f", StringComparison.OrdinalIgnoreCase).Split('\u001f');
                    if (parts.Length >= 3 && parts[0] == "LedgerEntries" && long.TryParse(parts[1], out var ledgerId))
                        editors.TryAdd(ledgerId, log.UserId!.Value);
                }
            }
            catch (JsonException) { /* Legacy description-only audit rows have no reliable editor link. */ }
        }
        var result = new List<TransactionDto>();
        foreach (var group in entries.GroupBy(x => byEntry[x.Id].PostingId))
        {
            var map = byEntry[group.First().Id];
            if (!hawalas.TryGetValue(map.HawalaId, out var h)) continue;
            var date = group.Min(x => x.CreatedAt);
            var editorId = map.PostingStage == "Original" ? h.CreatedBy : group.Select(x => editors.GetValueOrDefault(x.Id)).FirstOrDefault(x => x > 0);
            result.Add(new TransactionDto
            {
                Id = -group.Key, TransactionNo = $"HW-{h.Number}-{group.Key}",
                TransactionType = "Adjustment", Status = "Recorded", BranchId = 0, BranchName = "—",
                CreatedAt = date, CreatedBy = editorId, CreatedByName = users.GetValueOrDefault(editorId) ?? "—",
                Remarks = $"{StageLabel(map.PostingStage)}؛ حواله شماره {h.Number}؛ {h.ReferenceNumber}",
                LedgerEntries = group.OrderBy(x => x.Id).Select(e => new LedgerEntryDto
                {
                    Id = e.Id, AccountId = e.AccountId, AccountName = e.Account?.AccountName ?? "", AccountCode = e.Account?.AccountCode ?? "",
                    CurrencyId = e.CurrencyId, CurrencyCode = e.Currency?.Code ?? "", TalabKar = e.TalabKar, BadehKar = e.BadehKar,
                    Description = e.Description, CreatedAt = e.CreatedAt
                }).ToList()
            });
        }
        return result;
    }

    internal sealed class PostingMap
    {
        public long LedgerId { get; set; }
        public long HawalaId { get; set; }
        public long PostingId { get; set; }
        public string PostingStage { get; set; } = "";
    }
}
