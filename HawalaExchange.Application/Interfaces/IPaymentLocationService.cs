using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services
{
    public interface IPaymentLocationService
    {
        Task<IEnumerable<PaymentLocationDto>> GetAllAsync();
        Task<IEnumerable<PaymentLocationDto>> GetActiveAsync();
        Task<IEnumerable<PaymentLocationDto>> GetByCorrespondentAsync(long correspondentId, bool includeInactive = false);
        Task<PaymentLocationDto?> GetByIdAsync(long id);
        Task<PaymentLocationDto> CreateAsync(CreatePaymentLocationDto dto);
        Task<PaymentLocationDto> UpdateAsync(long id, UpdatePaymentLocationDto dto);
        Task DeleteAsync(long id);
        Task<PaymentLocationDto> ToggleActiveAsync(long id);
    }
}
