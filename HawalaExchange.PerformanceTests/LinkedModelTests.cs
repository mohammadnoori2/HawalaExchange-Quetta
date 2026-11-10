using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
namespace HawalaExchange.PerformanceTests;
public sealed class LinkedModelTests
{
    [Fact] public void Schema_snapshot_matches_runtime_model()
    {
        var fixture=new SqlServerPerformanceFixture(); using var db=fixture.CreateContext();
        var snapshot=db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized=db.GetService<IModelRuntimeInitializer>().Initialize(snapshot,designTime:true);
        var changes=db.GetService<IMigrationsModelDiffer>().GetDifferences(initialized.GetRelationalModel(),db.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        Assert.True(changes.Count==0,string.Join("\n",changes.Select(c=>c.GetType().Name+": "+System.Text.Json.JsonSerializer.Serialize(c,c.GetType()))));
    }
}
