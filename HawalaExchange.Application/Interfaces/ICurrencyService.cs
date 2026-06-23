using HawalaExchange.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.Interfaces
{
    public interface ICurrencyService
    {
        Task<List<CurrencyDtos>> GetAllAsync();
        Task<CurrencyDtos?> GetByIdAsync(long id);
        Task<long> CreateAsync(CreateCurrencyRequest request);
        Task UpdateAsync(long id, UpdateCurrencyRequest request);
        Task DeleteAsync(long id);
    }
}
