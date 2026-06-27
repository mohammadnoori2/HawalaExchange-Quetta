using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IExchangeRateService : IBaseService<ExchangeRate, ExchangeRateDto, CreateExchangeRateDto, UpdateExchangeRateDto>
    {
        Task<ExchangeRateDto?> GetLatestRateAsync(long fromCurrencyId, long toCurrencyId);
        Task<decimal> ConvertAsync(long fromCurrencyId, long toCurrencyId, decimal amount);
        Task<ExchangeRateDto?> GetRateByDateAsync(long fromCurrencyId, long toCurrencyId, DateTime date);
        Task<IEnumerable<ExchangeRateDto>> GetRateHistoryAsync(long fromCurrencyId, long toCurrencyId, DateTime fromDate, DateTime toDate);
        Task<ExchangeRateDto?> GetCurrentBuyRateAsync(long fromCurrencyId, long toCurrencyId);
        Task<ExchangeRateDto?> GetCurrentSellRateAsync(long fromCurrencyId, long toCurrencyId);
    }
}