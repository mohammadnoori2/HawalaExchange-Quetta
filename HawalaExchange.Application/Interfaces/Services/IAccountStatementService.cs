using HawalaExchange.Application.DTOs;
namespace HawalaExchange.Application.Interfaces.Services;
public interface IAccountStatementService
{
    Task<AccountStatementResult> GetAsync(AccountStatementFilter filter, CancellationToken cancellationToken = default);
}
