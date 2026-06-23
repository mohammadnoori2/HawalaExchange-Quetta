using HawalaExchange.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.Interfaces
{
    public interface IExchangeRatesService
    {
        public Task<List<ExchangeRatesDtos>> GetAllAsync();
        public Task<ExchangeRatesDtos?> GetByIdAsync(long id);
        public Task<long> CreateAsync(CreateExchangeRatesRequest request);
        public Task UpdateAsync(long id, UpdateExchangeRatesRequest request);
        public Task DeleteAsync(long id);
    }
}
