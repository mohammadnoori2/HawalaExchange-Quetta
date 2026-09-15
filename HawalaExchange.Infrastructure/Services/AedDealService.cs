using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Services;

public sealed class AedDealService(ApplicationDbContext context) : IAedDealService
{
    public async Task<IReadOnlyList<AedDealDto>> GetAllAsync(
        long? correspondentId = null,
        CancellationToken cancellationToken = default)
    {
        var deals = await context.AedDeals.AsNoTracking()
            .Include(x => x.SourceCorrespondent)
            .Include(x => x.DubaiCorrespondent)
            .Include(x => x.SourceCurrency)
            .Include(x => x.Conversions)
            .Where(x => !correspondentId.HasValue ||
                        x.SourceCorrespondentId == correspondentId.Value ||
                        x.DubaiCorrespondentId == correspondentId.Value)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        return deals.Select(Map).ToList();
    }

    public async Task<AedDealDto> CreateAsync(
        CreateAedDealDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateCreate(dto);
        var dealId = await ExecuteDealProcedureAsync("[dbo].[usp_CreateAedDeal_v1]", parameters =>
        {
            parameters.Add("@DealNumber", SqlDbType.NVarChar, 50).Value = dto.DealNumber.Trim();
            parameters.Add("@SourceCorrespondentId", SqlDbType.BigInt).Value = dto.SourceCorrespondentId;
            parameters.Add("@DubaiCorrespondentId", SqlDbType.BigInt).Value = dto.DubaiCorrespondentId;
            parameters.Add("@SourceCurrencyId", SqlDbType.BigInt).Value = dto.SourceCurrencyId;
            AddDecimal(parameters, "@Amount", dto.Amount, 18, 4);
            parameters.Add("@RoundingDecimalPlaces", SqlDbType.Int).Value = dto.RoundingDecimalPlaces;
            parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = Db(dto.Note?.Trim());
        }, cancellationToken);
        return await GetByIdAsync(dealId, cancellationToken);
    }

    public async Task<AedConversionPreviewDto> PreviewConversionAsync(
        PreviewAedConversionDto dto,
        CancellationToken cancellationToken = default)
    {
        var deal = await GetDealForCalculationAsync(dto.DealId, cancellationToken);
        return Calculate(deal, dto);
    }

    public async Task<AedDealDto> ConvertAsync(
        PreviewAedConversionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.DealId <= 0 || dto.SourceAmount <= 0)
            throw new InvalidOperationException("معامله و مبلغ تبدیل معتبر الزامی است.");
        var dealId = await ExecuteDealProcedureAsync("[dbo].[usp_ConvertAedDeal_v1]", parameters =>
        {
            parameters.Add("@DealId", SqlDbType.BigInt).Value = dto.DealId;
            AddDecimal(parameters, "@Amount", dto.SourceAmount, 18, 4);
            AddDecimal(parameters, "@ActualMarker", dto.ActualMarker, 18, 4);
            AddDecimal(parameters, "@DeclaredMarker", dto.DeclaredMarker, 18, 4);
            parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = Db(dto.Note?.Trim());
        }, cancellationToken);
        return await GetByIdAsync(dealId, cancellationToken);
    }

    public async Task ReverseConversionAsync(
        long conversionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        if (conversionId <= 0)
            throw new InvalidOperationException("تبدیل معامله معتبر نیست.");
        await ExecuteDealProcedureAsync("[dbo].[usp_ReverseAedDealConversion_v1]", parameters =>
        {
            parameters.Add("@ConversionId", SqlDbType.BigInt).Value = conversionId;
            parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason.Trim();
        }, cancellationToken);
    }

    public async Task CancelAsync(long dealId, string reason, CancellationToken cancellationToken = default)
    {
        ValidateReason(reason);
        if (dealId <= 0)
            throw new InvalidOperationException("معامله معتبر نیست.");
        await ExecuteDealProcedureAsync("[dbo].[usp_CancelAedDeal_v1]", parameters =>
        {
            parameters.Add("@DealId", SqlDbType.BigInt).Value = dealId;
            parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason.Trim();
        }, cancellationToken);
    }

    private static void ValidateCreate(CreateAedDealDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DealNumber)) throw new InvalidOperationException("نمبر معامله الزامی است.");
        if (dto.SourceCorrespondentId <= 0 || dto.DubaiCorrespondentId <= 0) throw new InvalidOperationException("طرف کویته و طرف دبی الزامی است.");
        if (dto.SourceCorrespondentId == dto.DubaiCorrespondentId) throw new InvalidOperationException("طرف کویته و طرف دبی نمی‌تواند یکسان باشد.");
        if (dto.Amount <= 0) throw new InvalidOperationException("مبلغ معامله باید بزرگ‌تر از صفر باشد.");
        if (dto.RoundingDecimalPlaces is < 0 or > 4) throw new InvalidOperationException("تعداد اعشار باید بین صفر تا چهار باشد.");
    }

    private AedConversionPreviewDto Calculate(AedDeal deal, PreviewAedConversionDto dto)
    {
        if (deal.Status == "Cancelled") throw new InvalidOperationException("معامله لغو شده قابل تبدیل نیست.");
        var remaining = deal.OriginalAmount - deal.ConvertedAmount;
        if (dto.SourceAmount <= 0 || dto.SourceAmount > remaining)
            throw new InvalidOperationException($"مبلغ تبدیل باید بین صفر و {remaining} {deal.SourceCurrency.Code} باشد.");
        var actualAdjusted = dto.SourceAmount + dto.SourceAmount / 100_000m * dto.ActualMarker;
        var declaredAdjusted = dto.SourceAmount + dto.SourceAmount / 100_000m * dto.DeclaredMarker;
        if (actualAdjusted <= 0 || declaredAdjusted <= 0)
            throw new InvalidOperationException("حاصل مشخصه معامله نباید منفی یا صفر شود.");
        var finalUsd = Round(ToUsd(deal.SourceCurrency.Code, actualAdjusted, deal.AedPerUsdRate), deal.RoundingDecimalPlaces);
        var declaredUsd = Round(ToUsd(deal.SourceCurrency.Code, declaredAdjusted, deal.AedPerUsdRate), deal.RoundingDecimalPlaces);
        if (finalUsd <= 0 || declaredUsd <= 0)
            throw new InvalidOperationException("حاصل تبدیل پس از گردکردن باید بزرگ‌تر از صفر باشد.");
        return new AedConversionPreviewDto
        {
            DealId = deal.Id, SourceCurrencyCode = deal.SourceCurrency.Code,
            SourceAmount = dto.SourceAmount, RemainingAmountBefore = remaining,
            AedPerUsdRate = deal.AedPerUsdRate, ActualMarker = dto.ActualMarker,
            DeclaredMarker = dto.DeclaredMarker, FinalUsdAmount = finalUsd,
            DeclaredUsdAmount = declaredUsd, ProfitUsd = finalUsd - declaredUsd,
            RoundingDecimalPlaces = deal.RoundingDecimalPlaces
        };
    }

    private async Task<AedDeal> GetDealForCalculationAsync(long id, CancellationToken cancellationToken, bool tracked = false)
    {
        var query = context.AedDeals.Include(x => x.SourceCurrency).Include(x => x.Conversions).AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
               ?? throw new KeyNotFoundException("معامله درهم یافت نشد.");
    }

    private async Task<AedDealDto> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var deal = await context.AedDeals.AsNoTracking().Include(x => x.SourceCorrespondent).Include(x => x.DubaiCorrespondent)
            .Include(x => x.SourceCurrency).Include(x => x.Conversions)
            .SingleAsync(x => x.Id == id, cancellationToken);
        return Map(deal);
    }

    private static AedDealDto Map(AedDeal x) => new()
    {
        Id = x.Id, DealNumber = x.DealNumber,
        SourceCorrespondentId = x.SourceCorrespondentId, SourceCorrespondentName = x.SourceCorrespondent.Name,
        DubaiCorrespondentId = x.DubaiCorrespondentId, DubaiCorrespondentName = x.DubaiCorrespondent.Name,
        SourceCurrencyId = x.SourceCurrencyId, SourceCurrencyCode = x.SourceCurrency.Code,
        OriginalAmount = x.OriginalAmount, ConvertedAmount = x.ConvertedAmount,
        TotalFinalUsd = x.TotalFinalUsd,
        TotalDeclaredUsd = x.Conversions.Where(c => c.Status == "Posted").Sum(c => c.DeclaredUsdAmount),
        TotalProfitUsd = x.TotalProfitUsd,
        AedPerUsdRate = x.AedPerUsdRate, RoundingDecimalPlaces = x.RoundingDecimalPlaces,
        Status = x.Status, Note = x.Note, CreatedAt = x.CreatedAt,
        Conversions = x.Conversions.OrderByDescending(c => c.CreatedAt).Select(c => new AedDealConversionDto
        {
            Id = c.Id, SourceAmount = c.SourceAmount, AedPerUsdRate = c.AedPerUsdRate,
            ActualMarker = c.ActualMarker, DeclaredMarker = c.DeclaredMarker,
            FinalUsdAmount = c.FinalUsdAmount, DeclaredUsdAmount = c.DeclaredUsdAmount,
            ProfitUsd = c.ProfitUsd,
            Status = c.Status, CreatedAt = c.CreatedAt
        }).ToList()
    };

    private async Task<long> ExecuteDealProcedureAsync(
        string procedureName,
        Action<SqlParameterCollection> configure,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = procedureName;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 60;
            command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = context.CurrentTenantId;
            command.Parameters.Add("@CurrentUserId", SqlDbType.BigInt).Value = context.RequireCurrentUserId();
            configure(command.Parameters);
            var result = await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("عملیات معامله درهم نتیجه معتبر برنگرداند.");
            context.ChangeTracker.Clear();
            return Convert.ToInt64(result);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static void AddDecimal(
        SqlParameterCollection parameters,
        string name,
        decimal value,
        byte precision,
        byte scale)
    {
        parameters.Add(new SqlParameter(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value
        });
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static decimal ToUsd(string sourceCode, decimal amount, decimal rate) =>
        sourceCode == "AED" ? amount / rate : amount;
    private static decimal Round(decimal value, int places) =>
        decimal.Round(value, Math.Clamp(places, 0, 4), MidpointRounding.AwayFromZero);
    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("دلیل برگشت الزامی است.");
        if (reason.Trim().Length > 500) throw new InvalidOperationException("دلیل برگشت نمی‌تواند بیشتر از ۵۰۰ حرف باشد.");
    }
}
