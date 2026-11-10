using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HawalaExchange.PerformanceTests;

public class EditAgentCommissionPermissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Updating_commission_requires_hawala_edit_permission(bool allowed)
    {
        var permission = new Mock<IUserPermissionService>();
        permission.SetupGet(x => x.CurrentPermissions).Returns(new HashSet<string> { allowed ? "Hawalas.Edit" : "Hawalas.View" });
        permission.Setup(x => x.RequireAsync("Hawalas.Edit", It.IsAny<CancellationToken>()))
            .Returns(allowed ? Task.CompletedTask : Task.FromException(new UnauthorizedAccessException()));
        var target = new Mock<IHawalaService>();
        target.Setup(x => x.UpdateAgentCommissionAsync(13, It.IsAny<UpdateHawalaAgentCommissionDto>())).ReturnsAsync(new HawalaDto { Id = 13 });
        var services = new ServiceCollection();
        services.AddSingleton(permission.Object);
        services.AddSingleton(Mock.Of<IDbContextFactory<ApplicationDbContext>>());
        services.AddSingleton(target.Object);
        services.ProtectTenantServices();
        using var provider = services.BuildServiceProvider();
        var guarded = provider.GetRequiredService<IHawalaService>();
        if (allowed) Assert.Equal(13, (await guarded.UpdateAgentCommissionAsync(13, new())).Id);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guarded.UpdateAgentCommissionAsync(13, new()));
        target.Verify(x => x.UpdateAgentCommissionAsync(13, It.IsAny<UpdateHawalaAgentCommissionDto>()), allowed ? Times.Once() : Times.Never());
        permission.Verify(x => x.RequireAsync("Hawalas.Edit", It.IsAny<CancellationToken>()), Times.Once);
    }
}
