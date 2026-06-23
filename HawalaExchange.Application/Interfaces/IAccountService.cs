using HawalaExchange.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.Interfaces
{
    public interface IAccountService
    {
        Task<List<AccountDtos>> GetAllAsync();
        Task<AccountDtos?> GetByIdAsync(long id);
        Task<long> CreateAsync(CreateAccountRequest request);
        Task UpdateAsync(long id, UpdateAccountRequest request);
        Task ArchiveAsync(long id);
    }
}
