using HawalaExchange.Application.DTOs;

namespace HawalaExchange.Application.Interfaces.Services;

public interface ISelectedRecordsService
{
    Task<IReadOnlyList<SelectedRecordDto>> ReadAsync(IReadOnlyCollection<long> ids, bool transactions = false,
        CancellationToken cancellationToken = default);
    Task<SelectedRecordsFileDto> ExportAsync(IReadOnlyCollection<long> ids, bool transactions = false,
        CancellationToken cancellationToken = default);
}
