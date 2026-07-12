using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ICompanySettingService
{
    Task<CompanySettingDto> GetAsync();

    Task<CompanySettingDto> SaveAsync(CompanySettingDto dto);
}