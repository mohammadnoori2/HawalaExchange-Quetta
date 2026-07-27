using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Application.Services;

/// <summary>
/// Rebuilds moving-weighted-average currency cost without using a market/reference rate.
/// Treasury conversions move inventory in the entered direction. Customer conversions
/// move the office's inventory in the opposite direction and recognize the spread between
/// the customer's rate and the office's moving weighted-average cost.
/// </summary>
public sealed class CurrencyCostService : ICurrencyCostService
{
    private readonly ApplicationDbContext _context;

    public CurrencyCostService(ApplicationDbContext context) => _context = context;

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        var capitals = await _context.CapitalInvestments
            .Where(x => !x.IsDeleted && x.ProfitCurrencyId != null && x.ProfitCurrencyAmount != null)
            .ToListAsync(cancellationToken);
        var exchanges = await _context.MoneyExchangeOperations
            .Where(x => !x.IsDeleted && x.ProfitCurrencyId != null)
            .ToListAsync(cancellationToken);

        var positions = new Dictionary<(long CurrencyId, long ProfitCurrencyId), Position>();
        var events = capitals.Select(x => new CostEvent(x.InvestmentDate, x.CreatedAt, x.Id, x, null))
            .Concat(exchanges.Select(x => new CostEvent(x.ExchangeDate, x.CreatedAt, x.Id, null, x)))
            .OrderBy(x => x.EffectiveDate).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id);

        foreach (var item in events)
        {
            if (item.Capital is not null)
            {
                ApplyCapital(item.Capital, positions);
                continue;
            }

            ApplyExchange(item.Exchange!, positions);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await RebuildLedgerAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyCostPositionDto>> GetPositionsAsync(
        CancellationToken cancellationToken = default)
    {
        await RebuildAsync(cancellationToken);

        var currencies = await _context.Currencies.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var operations = await _context.MoneyExchangeOperations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ProfitCurrencyId != null)
            .ToListAsync(cancellationToken);
        var capitals = await _context.CapitalInvestments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ProfitCurrencyId != null && x.ProfitCurrencyAmount != null)
            .ToListAsync(cancellationToken);

        var positions = new Dictionary<(long CurrencyId, long ProfitCurrencyId), Position>();
        var events = capitals.Select(x => new CostEvent(x.InvestmentDate, x.CreatedAt, x.Id, x, null))
            .Concat(operations.Select(x => new CostEvent(x.ExchangeDate, x.CreatedAt, x.Id, null, x)))
            .OrderBy(x => x.EffectiveDate).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id);
        foreach (var item in events)
        {
            if (item.Capital is not null) ApplyCapital(item.Capital, positions);
            else if (item.Exchange!.OperationType == "Customer")
                ApplyCustomer(item.Exchange, positions, updateOperation: false);
            else
                ApplyTreasury(item.Exchange, positions, updateOperation: false);
        }

        return positions
            .Where(x => x.Value.Quantity != 0 || x.Value.CarryingAmount != 0 || x.Value.ShortProceeds != 0)
            .Select(x => new CurrencyCostPositionDto
            {
                CurrencyId = x.Key.CurrencyId,
                CurrencyCode = currencies.GetValueOrDefault(x.Key.CurrencyId, x.Key.CurrencyId.ToString()),
                ProfitCurrencyId = x.Key.ProfitCurrencyId,
                ProfitCurrencyCode = currencies.GetValueOrDefault(x.Key.ProfitCurrencyId, x.Key.ProfitCurrencyId.ToString()),
                Quantity = x.Value.Quantity,
                CarryingAmount = x.Value.CarryingAmount,
                DeferredProceeds = x.Value.ShortProceeds
            })
            .OrderBy(x => x.ProfitCurrencyCode).ThenBy(x => x.CurrencyCode)
            .ToList();
    }

    private async Task RebuildLedgerAsync(CancellationToken cancellationToken)
    {
        var existingEntries = await _context.LedgerEntries
            .Where(x => x.MoneyExchangeOperationId != null)
            .ToListAsync(cancellationToken);
        _context.LedgerEntries.RemoveRange(existingEntries);

        var exchanges = await _context.MoneyExchangeOperations
            .Include(x => x.FromCurrency)
            .Include(x => x.ToCurrency)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ExchangeDate).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var exchange in exchanges)
            await MoneyExchangeOperationService.CreateLedgerEntriesAsync(_context, exchange);

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyCapital(
        CapitalInvestment capital,
        IDictionary<(long, long), Position> positions)
    {
        var profitCurrencyId = capital.ProfitCurrencyId!.Value;
        if (capital.CurrencyId == profitCurrencyId) return;
        Buy(Get(positions, capital.CurrencyId, profitCurrencyId), capital.Amount,
            capital.ProfitCurrencyAmount!.Value);
    }

    private static void ApplyExchange(
        MoneyExchangeOperation exchange,
        IDictionary<(long, long), Position> positions)
    {
        exchange.CostAmount = 0;
        exchange.RealizedProfit = 0;
        exchange.ExchangeProfitAmount = 0;
        exchange.InventoryCostIncrease = 0;
        exchange.InventoryCostDecrease = 0;
        exchange.ShortLiabilityIncrease = 0;
        exchange.ShortLiabilityDecrease = 0;
        exchange.DeferredAmount = 0;
        exchange.ProfitStatus = "Realized";

        if (exchange.OperationType == "Customer")
            ApplyCustomer(exchange, positions, updateOperation: true);
        else
            ApplyTreasury(exchange, positions, updateOperation: true);
    }

    /// <summary>
    /// A customer conversion is recorded from the customer's point of view. Therefore,
    /// the office acquires the customer's From currency and supplies the customer's To
    /// currency—the inverse inventory movement of a treasury conversion with the same
    /// currency direction.
    /// </summary>
    private static void ApplyCustomer(
        MoneyExchangeOperation exchange,
        IDictionary<(long, long), Position> positions,
        bool updateOperation)
    {
        var profitCurrencyId = exchange.ProfitCurrencyId!.Value;
        var profit = exchange.CommissionAmount;
        var cost = 0m;
        var deferred = 0m;
        var status = "Realized";
        var exchangeProfit = 0m;
        var inventoryIncrease = 0m;
        var inventoryDecrease = 0m;
        var shortIncrease = 0m;
        var shortDecrease = 0m;

        if (exchange.FromCurrencyId == profitCurrencyId)
        {
            var position = Get(positions, exchange.ToCurrencyId, profitCurrencyId);
            var sale = Sell(position, exchange.ToAmount, exchange.FromAmount);
            cost = sale.Cost;
            exchangeProfit = sale.Profit;
            profit += exchangeProfit - exchange.ExternalFeeAmount;
            inventoryDecrease = sale.Cost;
            shortIncrease = sale.DeferredProceeds;
            deferred = sale.DeferredQuantity;
            status = sale.Status;
        }
        else if (exchange.ToCurrencyId == profitCurrencyId)
        {
            var position = Get(positions, exchange.FromCurrencyId, profitCurrencyId);
            var purchase = Buy(position, exchange.FromAmount,
                exchange.ToAmount + exchange.ExternalFeeAmount);
            exchangeProfit = purchase.Profit;
            profit += purchase.Profit;
            cost = purchase.CoverCost;
            inventoryIncrease = purchase.RemainingCost;
            shortDecrease = purchase.CoverProceeds;
        }
        else
        {
            var supplied = Get(positions, exchange.ToCurrencyId, profitCurrencyId);
            var removed = RemoveAtCost(supplied, exchange.ToAmount);
            cost = removed.Cost;
            deferred = removed.MissingQuantity;
            status = deferred > 0 ? (cost > 0 ? "PartiallyDeferred" : "Deferred") : "Realized";

            if (deferred > 0)
                supplied.Quantity -= deferred;

            var acquired = Get(positions, exchange.FromCurrencyId, profitCurrencyId);
            var purchase = Buy(acquired, exchange.FromAmount, cost + exchange.ExternalFeeAmount);
            exchangeProfit = purchase.Profit;
            profit += purchase.Profit;
            inventoryDecrease = cost;
            inventoryIncrease = purchase.RemainingCost;
            shortDecrease = purchase.CoverProceeds;
        }

        if (!updateOperation) return;
        exchange.CostAmount = cost;
        exchange.RealizedProfit = profit;
        exchange.ExchangeProfitAmount = exchangeProfit;
        exchange.InventoryCostIncrease = inventoryIncrease;
        exchange.InventoryCostDecrease = inventoryDecrease;
        exchange.ShortLiabilityIncrease = shortIncrease;
        exchange.ShortLiabilityDecrease = shortDecrease;
        exchange.DeferredAmount = deferred;
        exchange.ProfitStatus = status;
    }

    private static void ApplyTreasury(
        MoneyExchangeOperation exchange,
        IDictionary<(long, long), Position> positions,
        bool updateOperation)
    {
        var profitCurrencyId = exchange.ProfitCurrencyId!.Value;
        var profit = exchange.CommissionAmount;
        var cost = 0m;
        var deferred = 0m;
        var status = "Realized";
        var exchangeProfit = 0m;
        var inventoryIncrease = 0m;
        var inventoryDecrease = 0m;
        var shortIncrease = 0m;
        var shortDecrease = 0m;

        if (exchange.FromCurrencyId == profitCurrencyId)
        {
            var position = Get(positions, exchange.ToCurrencyId, profitCurrencyId);
            var purchase = Buy(position, exchange.ToAmount,
                exchange.FromAmount + exchange.ExternalFeeAmount);
            exchangeProfit = purchase.Profit;
            profit += purchase.Profit;
            cost = purchase.CoverCost;
            inventoryIncrease = purchase.RemainingCost;
            shortDecrease = purchase.CoverProceeds;
        }
        else if (exchange.ToCurrencyId == profitCurrencyId)
        {
            var position = Get(positions, exchange.FromCurrencyId, profitCurrencyId);
            var sale = Sell(position, exchange.FromAmount, exchange.ToAmount);
            cost = sale.Cost;
            exchangeProfit = sale.Profit;
            profit += exchangeProfit - exchange.ExternalFeeAmount;
            inventoryDecrease = sale.Cost;
            shortIncrease = sale.DeferredProceeds;
            deferred = sale.DeferredQuantity;
            status = sale.Status;
        }
        else
        {
            var from = Get(positions, exchange.FromCurrencyId, profitCurrencyId);
            var removed = RemoveAtCost(from, exchange.FromAmount);
            cost = removed.Cost;
            deferred = removed.MissingQuantity;
            status = deferred > 0 ? (cost > 0 ? "PartiallyDeferred" : "Deferred") : "Realized";

            if (deferred > 0)
            {
                from.Quantity -= deferred;
            }

            var to = Get(positions, exchange.ToCurrencyId, profitCurrencyId);
            var purchase = Buy(to, exchange.ToAmount, cost + exchange.ExternalFeeAmount);
            exchangeProfit = purchase.Profit;
            profit += purchase.Profit;
            inventoryDecrease = cost;
            inventoryIncrease = purchase.RemainingCost;
            shortDecrease = purchase.CoverProceeds;
        }

        if (!updateOperation) return;
        exchange.CostAmount = cost;
        exchange.RealizedProfit = profit;
        exchange.ExchangeProfitAmount = exchangeProfit;
        exchange.InventoryCostIncrease = inventoryIncrease;
        exchange.InventoryCostDecrease = inventoryDecrease;
        exchange.ShortLiabilityIncrease = shortIncrease;
        exchange.ShortLiabilityDecrease = shortDecrease;
        exchange.DeferredAmount = deferred;
        exchange.ProfitStatus = status;
    }

    private static Position Get(
        IDictionary<(long, long), Position> positions,
        long currencyId,
        long profitCurrencyId)
    {
        var key = (currencyId, profitCurrencyId);
        if (!positions.TryGetValue(key, out var position))
        {
            position = new Position();
            positions[key] = position;
        }
        return position;
    }

    private static BuyResult Buy(Position position, decimal quantity, decimal cost)
    {
        var realizedProfit = 0m;
        var coverCost = 0m;
        var coverProceeds = 0m;
        if (position.Quantity < 0)
        {
            var shortQuantity = -position.Quantity;
            var covered = Math.Min(quantity, shortQuantity);
            var allocatedCost = cost * (covered / quantity);
            var allocatedProceeds = position.ShortProceeds * (covered / shortQuantity);
            realizedProfit += allocatedProceeds - allocatedCost;
            coverCost += allocatedCost;
            coverProceeds += allocatedProceeds;
            position.Quantity += covered;
            position.ShortProceeds -= allocatedProceeds;
            quantity -= covered;
            cost -= allocatedCost;
        }

        if (quantity > 0)
        {
            position.Quantity += quantity;
            position.CarryingAmount += cost;
        }
        var remainingCost = cost;
        Normalize(position);
        return new BuyResult(coverCost, coverProceeds, remainingCost, realizedProfit);
    }

    private static SaleResult Sell(Position position, decimal quantity, decimal proceeds)
    {
        var available = Math.Max(position.Quantity, 0);
        var soldFromInventory = Math.Min(quantity, available);
        var deferred = quantity - soldFromInventory;
        var cost = available == 0 ? 0 : position.CarryingAmount * (soldFromInventory / available);
        var realizedProceeds = proceeds * (soldFromInventory / quantity);

        position.Quantity -= soldFromInventory;
        position.CarryingAmount -= cost;
        if (deferred > 0)
        {
            position.Quantity -= deferred;
            position.ShortProceeds += proceeds - realizedProceeds;
        }
        Normalize(position);

        return new SaleResult(
            cost,
            realizedProceeds - cost,
            deferred,
            proceeds - realizedProceeds,
            deferred == 0 ? "Realized" : soldFromInventory > 0 ? "PartiallyDeferred" : "Deferred");
    }

    private static RemovedCost RemoveAtCost(Position position, decimal quantity)
    {
        var available = Math.Max(position.Quantity, 0);
        var removedQuantity = Math.Min(quantity, available);
        var cost = available == 0 ? 0 : position.CarryingAmount * (removedQuantity / available);
        position.Quantity -= removedQuantity;
        position.CarryingAmount -= cost;
        Normalize(position);
        return new RemovedCost(cost, quantity - removedQuantity);
    }

    private static void Normalize(Position position)
    {
        if (Math.Abs(position.Quantity) < 0.00000001m) position.Quantity = 0;
        if (Math.Abs(position.CarryingAmount) < 0.00000001m) position.CarryingAmount = 0;
        if (Math.Abs(position.ShortProceeds) < 0.00000001m) position.ShortProceeds = 0;
    }

    private sealed class Position
    {
        public decimal Quantity { get; set; }
        public decimal CarryingAmount { get; set; }
        public decimal ShortProceeds { get; set; }
    }

    private sealed record CostEvent(
        DateTime EffectiveDate, DateTime CreatedAt, long Id,
        CapitalInvestment? Capital, MoneyExchangeOperation? Exchange);
    private sealed record BuyResult(
        decimal CoverCost, decimal CoverProceeds, decimal RemainingCost, decimal Profit);
    private sealed record SaleResult(
        decimal Cost, decimal Profit, decimal DeferredQuantity, decimal DeferredProceeds, string Status);
    private sealed record RemovedCost(decimal Cost, decimal MissingQuantity);
}
