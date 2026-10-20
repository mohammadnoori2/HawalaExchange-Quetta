using System.Net;
using System.Security.Claims;
using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HawalaExchange.PerformanceTests;

public sealed class UploadSecurityRegressionTests
{
    internal static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3WQAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("../outside")]
    [InlineData("uploads/a/../../outside")]
    [InlineData("uploads/a/file.png:evil")]
    [InlineData("uploads/a/%2e%2e/file.png")]
    [InlineData("//uploads/a/file.png")]
    [InlineData("uploads/a/../file.png")]
    [InlineData("uploads/CON/file.png")]
    public void Noncanonical_paths_are_rejected(string path) => Assert.Throws<ArgumentException>(() => UploadStorage.Normalize(path));

    [Theory]
    [InlineData("photo.svg")]
    [InlineData("photo.html")]
    [InlineData("photo.aspx")]
    [InlineData("photo.png:evil")]
    [InlineData("../photo.png")]
    public void Active_extensions_and_filename_tricks_are_rejected(string name) => Assert.Throws<ArgumentException>(() => UploadContentPolicy.Validate(name, Png));

    [Fact]
    public void An_image_extension_does_not_bypass_signature_validation() =>
        Assert.Throws<ArgumentException>(() => UploadContentPolicy.Validate("photo.png", "<html>not an image</html>"u8.ToArray()));

    [Fact]
    public async Task New_images_are_private_and_remain_readable_and_deletable_by_the_owning_tenant()
    {
        using var sandbox = new SecurityFileSandbox();
        var result = await sandbox.Files.UploadFileAsync(new MemoryStream(Png), "photo.png", "tazkira");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("tenant_1/", result.FilePath);
        Assert.False(File.Exists(Path.Combine(sandbox.WebRoot, result.FilePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(UploadStorage.Physical(UploadStorage.PrivateRoot(sandbox.Root), result.FilePath[8..])));
        Assert.Equal(Png, await sandbox.Files.GetFileBytesAsync(result.FilePath));
        Assert.True(await sandbox.Files.DeleteFileAsync(result.FilePath));
        Assert.False(await sandbox.Files.FileExistsAsync(result.FilePath));
    }

    [Theory]
    [InlineData(1, true, true, HttpStatusCode.OK)]
    [InlineData(2, true, true, HttpStatusCode.NotFound)]
    [InlineData(1, true, false, HttpStatusCode.NotFound)]
    [InlineData(1, false, true, HttpStatusCode.Unauthorized)]
    public async Task Actual_upload_middleware_enforces_authentication_permissions_and_tenant(long tenant, bool signedIn, bool view, HttpStatusCode expected)
    {
        using var sandbox = new SecurityFileSandbox();
        var upload = await sandbox.Files.UploadFileAsync(new MemoryStream(Png), "photo.png", "tazkira");
        Assert.True(upload.Success, upload.ErrorMessage);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath=sandbox.Root, WebRootPath=sandbox.WebRoot });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant {TenantId=tenant,UserId=1});
        var permissions = new Mock<IUserPermissionService>();
        permissions.Setup(x=>x.Can(It.IsAny<string>())).Returns<string>(key=>view && key=="Hawalas.View");
        permissions.Setup(x=>x.RefreshAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<IUserPermissionService>(permissions.Object);
        builder.Services.AddScoped<IFileService,FileService>();
        await using var app = builder.Build();
        app.Use(async (http,next)=>
        {
            if(signedIn)http.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"1")],"SecurityTest"));
            await next();
        });
        app.UseMiddleware<ProtectedUploadsMiddleware>();
        app.UseStaticFiles();
        await app.StartAsync();
        try
        {
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient{BaseAddress=new Uri(address),Timeout=TimeSpan.FromSeconds(10)};
            using var response=await client.GetAsync("/"+upload.FilePath);
            Assert.Equal(expected,response.StatusCode);
            Assert.Contains("no-store",response.Headers.CacheControl!.ToString());
            if(expected==HttpStatusCode.OK) {Assert.Equal(Png,await response.Content.ReadAsByteArrayAsync());Assert.Equal("image/png",response.Content.Headers.ContentType!.MediaType);}
        }
        finally{await app.StopAsync();}
    }

    [Fact]
    public void Bootstrap_uses_configured_secrets_and_does_not_restore_existing_roles()
    {
        var root=Directory.GetParent(Path.GetDirectoryName(Source())!)!.FullName;
        var seed=File.ReadAllText(Path.Combine(root,"HawalaExchange.Infrastructure","Data","SeedData.cs"));
        Assert.Contains("configuration[\"BootstrapAdmin:Password\"]",seed);
        Assert.DoesNotContain("CreateAsync(adminUser, \"",seed);
        Assert.DoesNotContain("platformPassword = \"",seed);
        Assert.Contains("!await context.Users.IgnoreQueryFilters().AnyAsync",seed);
    }
    private static string Source([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class UploadSecurityDatabaseRegressionTests(SqlServerPerformanceFixture fixture)
{
    [Fact]
    public async Task Platform_users_without_a_tenant_claim_can_manage_only_their_own_private_profile()
    {
        using var sandbox=new SecurityFileSandbox();
        await using var setup=fixture.CreateContext();
        var user=new ApplicationUser{TenantId=1,IsPlatformUser=true,IsActive=true,UserName=Guid.NewGuid().ToString("N"),LocalUserName=Guid.NewGuid().ToString("N"),FullName="Profile security test"};
        setup.Users.Add(user);await setup.SaveChangesAsync();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(setup.Database.GetConnectionString()).Options;
        await using var db=new ApplicationDbContext(options,new TestCurrentTenant{TenantId=0,UserId=user.Id});
        var files=new FileService(sandbox.Environment.Object,db,sandbox.Permissions.Object);
        var upload=await files.UploadFileAsync(new MemoryStream(UploadSecurityRegressionTests.Png),"profile.png","profiles");
        Assert.True(upload.Success,upload.ErrorMessage);
        var stored=await db.Users.IgnoreQueryFilters().SingleAsync(x=>x.Id==user.Id);
        stored.ProfileImagePath=upload.FilePath;await db.SaveChangesAsync();
        Assert.Equal(UploadSecurityRegressionTests.Png,await files.GetFileBytesAsync(upload.FilePath));
        var foreign=upload.FilePath.Replace(user.Id+"_","999999_",StringComparison.Ordinal);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>files.GetFileBytesAsync(foreign));
        stored.ProfileImagePath=null;await db.SaveChangesAsync();
        Assert.True(await files.DeleteFileAsync(upload.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forging_another_tenants_legacy_attachment_reference_is_rejected_before_writing(bool synchronous)
    {
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();
        var legacy="/uploads/customers/photo/foreign-"+Guid.NewGuid().ToString("N")+".png";
        // Trusted pre-upgrade fixture record, not a new user-submitted path.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Documents(TenantId,AccountId,FileName,FilePath,ContentType,FileSizeBytes,UploadedAt) VALUES({db.CurrentTenantId},{fixture.SourceAccount.Id},N'legacy.png',{legacy},N'image/png',0,SYSUTCDATETIME())");
        var tenant=new Tenant{Name="Forged attachment isolation",IsActive=true};db.Tenants.Add(tenant);await db.SaveChangesAsync();
        using(db.UseTenantScope(tenant.Id))
        {
            db.Customers.Add(new Customer{CustomerCode="FORGE",FullName="Must not save",PhotoPath=legacy});
            if(synchronous)Assert.Throws<UnauthorizedAccessException>(()=>db.SaveChanges());
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Existing_private_document_remains_accessible_only_to_its_tenant()
    {
        using var sandbox=new SecurityFileSandbox();
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();
        var file=UploadStorage.Physical(Path.Combine(sandbox.WebRoot,"uploads"),"Account/legacy.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);await File.WriteAllBytesAsync(file,"%PDF-1.7\n%%EOF"u8.ToArray());
        var row=new Document{AccountId=fixture.SourceAccount.Id,FileName="legacy.pdf",FilePath="/uploads/Account/legacy.pdf",ContentType="application/pdf"};
        // Simulate a pre-upgrade record; new writes cannot introduce unowned legacy paths.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Documents(TenantId,AccountId,FileName,FilePath,ContentType,FileSizeBytes,UploadedAt) VALUES({db.CurrentTenantId},{row.AccountId},{row.FileName},{row.FilePath},{row.ContentType},0,SYSUTCDATETIME())");
        var files=new FileService(sandbox.Environment.Object,db,sandbox.Permissions.Object);
        Assert.NotEmpty((await files.GetFileBytesAsync(row.FilePath))!);
        var foreign=new Tenant{Name="File security isolation",IsActive=true};db.Tenants.Add(foreign);await db.SaveChangesAsync();
        using(db.UseTenantScope(foreign.Id))await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>files.GetFileBytesAsync(row.FilePath));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Valid_document_upload_is_linked_to_its_entity_and_downloads_from_private_storage()
    {
        using var sandbox=new SecurityFileSandbox();
        await using var db=fixture.CreateContext();using var bypass=db.BypassSubscriptionEnforcement();
        var documents=new DocumentService(db,sandbox.Environment.Object,Mock.Of<IMapper>(),Mock.Of<ISubscriptionAccessService>());
        var name="safe-"+Guid.NewGuid().ToString("N")+".pdf";var content="%PDF-1.7\n%%EOF"u8.ToArray();
        await documents.UploadDocumentAsync(new(){EntityType="Account",EntityId=fixture.SourceAccount.Id,FileName=name,ContentType="text/html",FileContent=content});
        var row=await db.Documents.AsNoTracking().SingleAsync(x=>x.FileName==name);
        Assert.Equal(fixture.SourceAccount.Id,row.AccountId);Assert.Equal("application/pdf",row.ContentType);
        Assert.Equal(content,await documents.DownloadDocumentAsync(row.Id));
        Assert.False(File.Exists(Path.Combine(sandbox.WebRoot,row.FilePath.TrimStart('/').Replace('/',Path.DirectorySeparatorChar))));
        await documents.DeleteDocumentAsync(row.Id);
        Assert.False(File.Exists(UploadStorage.Physical(UploadStorage.PrivateRoot(sandbox.Root),row.FilePath.TrimStart('/')[8..])));
    }
}
