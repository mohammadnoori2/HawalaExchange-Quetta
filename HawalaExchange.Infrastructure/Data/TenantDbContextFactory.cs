using HawalaExchange.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.Infrastructure.Data;

public sealed class TenantDbContextFactory(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentTenant currentTenant) : IDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext() => new(options, currentTenant);
}
