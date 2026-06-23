using HawalaExchange.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace HawalaExchange.Application.Interfaces
{
    public interface ICorrespondentService
    {
        Task<List<CorrespondentDtos>> GetAllAsync();
        Task<CorrespondentDtos?> GetByIdAsync(long id);
        Task<long> CreateAsync(CreateCorrespondentRequest request);
        Task UpdateAsync(long id, UpdateCorrespondentRequest request);
        Task DeleteAsync(long id);
    }
}
