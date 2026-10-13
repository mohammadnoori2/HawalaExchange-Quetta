namespace HawalaExchange.Application.Interfaces.Services;

public interface IUserPermissionService
{
    event Action? Changed;
    bool IsPlatformUser { get; }
    bool Can(string permission);
    IReadOnlySet<string> CurrentPermissions { get; }
    Task RefreshAsync(bool force=false,CancellationToken cancellationToken=default);
    Task RequireAsync(string permission,CancellationToken cancellationToken=default);
    Task<HashSet<string>> GetForUserAsync(long userId,CancellationToken cancellationToken=default);
    Task SaveForUserAsync(long userId,IEnumerable<string> permissions,CancellationToken cancellationToken=default);
    Task ValidateGrantAsync(IEnumerable<string> permissions,CancellationToken cancellationToken=default);
    Task EnsureUserCanBeDisabledAsync(long userId,CancellationToken cancellationToken=default);
    void StartWatching();
}
