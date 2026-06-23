
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using YourNamespace.Data;

namespace HawalaExchange.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IAccountService, AccountService>();

        return services;
    }
}