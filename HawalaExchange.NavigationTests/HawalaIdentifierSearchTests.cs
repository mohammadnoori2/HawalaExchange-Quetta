using HawalaExchange.Application.DTOs;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Services;
using Xunit;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using HawalaExchange.Web.Components.Shared;
using HawalaExchange.Application.Interfaces;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using AutoMapper;
using Microsoft.Extensions.DependencyInjection;

namespace HawalaExchange.NavigationTests;

public class HawalaIdentifierSearchTests
{
    [Fact]
    public async Task Search_control_renders_accessible_reference_and_serial_choices()
    {
        using var services=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var view=await renderer.RenderComponentAsync<HawalaIdentifierSearchBox>(ParameterView.Empty);
            var html=view.ToHtmlString();
            Assert.Contains("maxlength=\"200\"",html); Assert.Contains("value=\"Reference\"",html); Assert.Contains("value=\"Serial\"",html);
            var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!Directory.Exists(Path.Combine(dir.FullName,"HawalaExchange.Web")))dir=dir.Parent;
            var output=Path.Combine(dir!.FullName,"HawalaExchange.Web","obj","identifier-search-qa");Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output,"search.html"),html);
        });
    }

    [Fact]
    public async Task Typing_does_not_search_and_enter_submits_once_with_validation()
    {
        var box = new HawalaIdentifierSearchBox();
        var calls = new List<HawalaIdentifierSearch>();
        box.Changed = EventCallback.Factory.Create<HawalaIdentifierSearch>(new object(), (Action<HawalaIdentifierSearch>)(s => calls.Add(s)));
        var type = typeof(HawalaIdentifierSearchBox);
        type.GetField("kind", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(box,"Serial");
        type.GetField("draft", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(box,"۹۵۵");
        async Task Key(string key, bool repeat=false) => await (Task)type.GetMethod("KeyDown",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(box,[new KeyboardEventArgs{Key=key,Repeat=repeat}])!;
        await Key("5"); await Key("Tab"); await Key("Enter",true);
        Assert.Empty(calls);
        await Key("Enter"); Assert.Equal(955, Assert.Single(calls).Serial);
        type.GetField("draft",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(box,"1.5");
        await Key("Enter"); Assert.Single(calls);
    }

    [Theory]
    [InlineData("Serial", "955")]
    [InlineData("Reference", "REF-955")]
    public void Real_list_query_uses_equality_and_retains_period_tenant_and_correspondent(string kind,string value)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=NotOpened;Integrated Security=True;TrustServerCertificate=True").Options,
            Mock.Of<ICurrentTenant>(x => x.TenantId == 1));
        var service = new HawalaService(db, Mock.Of<IMapper>(), Mock.Of<ILedgerService>(), Mock.Of<IAccountService>(), Mock.Of<IAuditLogService>(), Mock.Of<IFileService>());
        var filter = new HawalaFilterDto { Identifier=HawalaIdentifierSearch.Create(kind,value), SearchTerm="ignored broad search", Number=999,
            CorrespondentId=10, Extra=new(){PeriodId=7} };
        var query = (IQueryable<Hawala>)typeof(HawalaService).GetMethod("BuildHawalaListQuery",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[filter,false])!;
        var sql=query.ToQueryString();
        Assert.DoesNotContain("LIKE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CorrespondentAccountPeriodHawalas",sql);
        Assert.Contains("PeriodId",sql); Assert.Contains("TenantId",sql); Assert.Contains("CorrespondentId",sql);
        Assert.Contains(kind=="Serial" ? "[Number] =" : "[ReferenceNumber] =",sql);
    }

    [Theory]
    [InlineData("Reference", " A44559 ", "A44559", null)]
    [InlineData("Serial", "۹۵۵", "955", 955L)]
    [InlineData("Serial", "٩٥٥", "955", 955L)]
    public void Identifier_is_normalized_without_amount_or_partial_matching(string kind, string input, string value, long? serial)
    {
        var search = HawalaIdentifierSearch.Create(kind, input);
        Assert.Equal(value, search.Value);
        Assert.Equal(serial, search.Serial);
    }

    [Theory]
    [InlineData("Serial", "1.5")]
    [InlineData("Serial", "-5")]
    [InlineData("Serial", "0")]
    [InlineData("Serial", "9223372036854775808")]
    [InlineData("Unknown", "955")]
    public void Invalid_identifier_is_rejected(string kind, string value) =>
        Assert.Throws<ArgumentException>(() => HawalaIdentifierSearch.Create(kind, value));

    [Theory]
    [InlineData("Reference", "REF-955", 1L)]
    [InlineData("Serial", "955", 1L)]
    [InlineData("Reference", "REF", 0L)]
    [InlineData("Serial", "55", 0L)]
    public void Exact_search_preserves_existing_scope_and_never_falls_back(string kind, string term, long expectedId)
    {
        var rows = new[] {
            new Hawala { Id=1, Number=955, ReferenceNumber="REF-955", CorrespondentId=10 },
            new Hawala { Id=2, Number=955, ReferenceNumber="REF-955", CorrespondentId=20 },
            new Hawala { Id=3, Number=1955, ReferenceNumber="REF-955-extra", CorrespondentId=10 }
        }.AsQueryable();
        var result = rows.Where(x => x.CorrespondentId == 10)
            .WhereIdentifier(HawalaIdentifierSearch.Create(kind,term), x => x.Number, x => x.ReferenceNumber).ToList();
        if (expectedId == 0) Assert.Empty(result);
        else Assert.Equal(expectedId, Assert.Single(result).Id);
    }
}
