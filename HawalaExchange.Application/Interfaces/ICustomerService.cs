using HawalaExchange.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.Interfaces
{
    public interface ICustomerService
    {
        Task<List<CustomerDto>> GetAllAsync();
        Task<CustomerDto?> GetByIdAsync(long id);
        Task<long> CreateAsync(CreateCustomerRequest request);
        Task UpdateAsync(long id, UpdateCustomerRequest request);
        Task ArchiveAsync(long id);
    }
}
