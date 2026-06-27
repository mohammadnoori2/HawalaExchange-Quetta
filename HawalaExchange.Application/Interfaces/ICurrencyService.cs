using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface ICurrencyService : IBaseService<Currency, CurrencyDto, CreateCurrencyDto, UpdateCurrencyDto>
    {
        Task<CurrencyDto?> GetByCodeAsync(string code);
        Task<IEnumerable<CurrencyDto>> GetActiveCurrenciesAsync();
        Task<CurrencyDto> DeactivateAsync(long id);
        Task<CurrencyDto> ActivateAsync(long id);
    }
}