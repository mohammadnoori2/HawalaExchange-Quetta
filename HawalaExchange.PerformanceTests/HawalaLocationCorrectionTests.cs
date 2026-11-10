using HawalaExchange.Infrastructure.Data;
using HawalaExchange.PerformanceTests.Infrastructure;
namespace HawalaExchange.PerformanceTests;

[Collection(SqlServerPerformanceCollection.Name)]
public class HawalaLocationCorrectionTests(SqlServerPerformanceFixture fixture) : LocationCorrectionScenarios
{
    protected override ApplicationDbContext Context()=>fixture.CreateContext();
}
