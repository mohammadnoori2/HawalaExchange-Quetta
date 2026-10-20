using System.Net;
using System.Text;
using AutoMapper;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Moq;
using HawalaExchange.Web.Services;

namespace HawalaExchange.PerformanceTests;

// Negative security requirements: failures deliberately remain visible until the
// production defect is fixed. All payloads are inert and all files are synthetic.
public sealed class SecurityAdversarialFileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_reader_must_reject_paths_outside_upload_storage(bool absolute)
    {
        using var sandbox = new SecurityFileSandbox();
        var secret = Path.Combine(sandbox.Root, "private-canary.txt");
        await File.WriteAllTextAsync(secret, "synthetic-private-data");
        byte[]? read = null;
        try { read = await sandbox.Files.GetFileBytesAsync(absolute ? secret : "../private-canary.txt"); }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException) { }
        Assert.Null(read);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_deletion_must_not_remove_files_outside_upload_storage(bool absolute)
    {
        using var sandbox = new SecurityFileSandbox();
        var secret = Path.Combine(sandbox.Root, "delete-canary.txt");
        await File.WriteAllTextAsync(secret, "synthetic-private-data");
        try { await sandbox.Files.DeleteFileAsync(absolute ? secret : "../delete-canary.txt"); }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException) { }
        Assert.True(File.Exists(secret), "A file outside upload storage was deleted.");
    }

    [Fact]
    public async Task File_upload_must_reject_directory_traversal()
    {
        using var sandbox = new SecurityFileSandbox();
        await using var stream = new MemoryStream("inert upload canary"u8.ToArray());
        FileUploadResult? result = null;
        try { result = await sandbox.Files.UploadFileAsync(stream, "canary.txt", "../../outside"); }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException) { }
        Assert.False(result?.Success ?? false, "The upload escaped the uploads directory.");
    }

    [Fact]
    public async Task Private_upload_must_not_be_served_to_anonymous_http_requests()
    {
        using var sandbox = new SecurityFileSandbox();
        // Matches the actual production middleware order; deny-all fallback stands
        // in for authentication. No main application, migrations or workers run.
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "HawalaExchange.Web", "Program.cs"));
        Assert.True(source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal)
            < source.IndexOf("app.UseMiddleware<ProtectedUploadsMiddleware>();", StringComparison.Ordinal));
        Assert.True(source.IndexOf("app.UseMiddleware<ProtectedUploadsMiddleware>();", StringComparison.Ordinal)
            < source.IndexOf("app.UseStaticFiles();", StringComparison.Ordinal));
        var folder = Path.Combine(sandbox.WebRoot, "uploads", "tazkira");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "synthetic-private.txt"), "not-public");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = sandbox.Root, WebRootPath = sandbox.WebRoot, EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseMiddleware<ProtectedUploadsMiddleware>();
        app.UseStaticFiles();
        app.Run(context => { context.Response.StatusCode = 401; return Task.CompletedTask; });
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected-control")).StatusCode);
            using var response = await client.GetAsync("/uploads/tazkira/synthetic-private.txt");
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        }
        finally { await app.StopAsync(); }
    }

    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
}

[Collection(SqlServerPerformanceCollection.Name)]
public sealed class SecurityAdversarialDatabaseTests(SqlServerPerformanceFixture fixture)
{
    [Theory]
    [InlineData("canary.html", "text/html")]
    [InlineData("canary.svg", "image/svg+xml")]
    [InlineData("canary.aspx", "application/octet-stream")]
    public async Task Document_upload_must_reject_active_content(string name, string contentType)
    {
        using var sandbox = new SecurityFileSandbox();
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var documents = new DocumentService(db, sandbox.Environment.Object, Mock.Of<IMapper>(), Mock.Of<ISubscriptionAccessService>());
        var exception = await Record.ExceptionAsync(() => documents.UploadDocumentAsync(new UploadDocumentDto
        {
            EntityType = "Account", EntityId = fixture.SourceAccount.Id,
            FileName = name, ContentType = contentType, FileContent = Encoding.UTF8.GetBytes("inert security-audit canary")
        }));
        Assert.True(exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException,
            "Active-content file type was accepted by the actual document service.");
    }

    [Fact]
    public async Task Document_entity_type_must_not_control_a_filesystem_path()
    {
        using var sandbox = new SecurityFileSandbox();
        await using var db = fixture.CreateContext(); using var bypass = db.BypassSubscriptionEnforcement();
        var documents = new DocumentService(db, sandbox.Environment.Object, Mock.Of<IMapper>(), Mock.Of<ISubscriptionAccessService>());
        var exception = await Record.ExceptionAsync(() => documents.UploadDocumentAsync(new UploadDocumentDto
        {
            EntityType = "../../outside", EntityId = 1, FileName = "canary.txt", ContentType = "text/plain", FileContent = "inert"u8.ToArray()
        }));
        Assert.True(exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException,
            "A traversal EntityType was accepted by the actual document service.");
        Assert.False(Directory.Exists(Path.Combine(sandbox.Root, "outside")));
    }

    [Fact]
    public async Task Another_tenants_account_is_hidden_and_foreign_writes_are_rejected()
    {
        await using var setup = fixture.CreateContext(); using var bypass = setup.BypassSubscriptionEnforcement();
        var tenant = new Tenant { Name = "Security audit isolation", IsActive = true };
        setup.Tenants.Add(tenant); await setup.SaveChangesAsync();
        var account = new Account { AccountCode = "ISOLATION", AccountName = "Foreign synthetic account", AccountType = "Cash" };
        using (setup.UseTenantScope(tenant.Id)) { setup.Accounts.Add(account); await setup.SaveChangesAsync(); }
        await using var local = fixture.CreateContext(); using var localBypass = local.BypassSubscriptionEnforcement();
        Assert.False(await local.Accounts.AnyAsync(x => x.Id == account.Id));
        Assert.Null(await local.Accounts.FindAsync(account.Id));
        local.Accounts.Add(new Account { TenantId = tenant.Id, AccountCode = "DENIED", AccountName = "Denied", AccountType = "Cash" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => local.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_foreign_keys_reject_cross_tenant_financial_links()
    {
        await using var setup = fixture.CreateContext(); using var bypass = setup.BypassSubscriptionEnforcement();
        var tenant = new Tenant { Name = "Security audit FK", IsActive = true };
        setup.Tenants.Add(tenant); await setup.SaveChangesAsync();
        var account = new Account { AccountCode = "FOREIGN-FK", AccountName = "Foreign account", AccountType = "Cash" };
        using (setup.UseTenantScope(tenant.Id)) { setup.Accounts.Add(account); await setup.SaveChangesAsync(); }
        await using var local = fixture.CreateContext(); using var localBypass = local.BypassSubscriptionEnforcement();
        local.AccountBadehkarLimits.Add(new AccountBadehkarLimit { AccountId = account.Id, CurrencyId = 2, BadehkarLimit = 100, IsActive = true });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => local.SaveChangesAsync());
        Assert.IsType<Microsoft.Data.SqlClient.SqlException>(exception.InnerException);
        Assert.Equal(547, ((Microsoft.Data.SqlClient.SqlException)exception.InnerException!).Number);
    }

    [Fact]
    public async Task Missing_tenant_cannot_read_tenant_owned_records()
    {
        using var source = fixture.CreateContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(source.Database.GetConnectionString()).Options;
        await using var anonymous = new ApplicationDbContext(options, new TestCurrentTenant { TenantId = 0, UserId = 0 });
        Assert.False(await anonymous.Accounts.AnyAsync());
        Assert.False(await anonymous.Hawalas.AnyAsync());
        Assert.False(await anonymous.Users.AnyAsync());
    }
}

internal sealed class SecurityFileSandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "hawala-security-audit-" + Guid.NewGuid().ToString("N"));
    public string WebRoot => Path.Combine(Root, "wwwroot");
    public Mock<IWebHostEnvironment> Environment { get; } = new();
    public Mock<IUserPermissionService> Permissions { get; } = new();
    public FileService Files { get; }
    public SecurityFileSandbox()
    {
        Directory.CreateDirectory(WebRoot);
        Environment.SetupGet(x => x.ContentRootPath).Returns(Root);
        Environment.SetupGet(x => x.WebRootPath).Returns(WebRoot);
        Environment.SetupGet(x => x.WebRootFileProvider).Returns(new PhysicalFileProvider(WebRoot));
        Permissions.Setup(x => x.RefreshAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);
        Permissions.Setup(x => x.Can(It.IsAny<string>())).Returns(true);
        Files = new FileService(Environment.Object, new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().Options, new TestCurrentTenant { TenantId = 1, UserId = 1 }), Permissions.Object);
    }
    public void Dispose()
    {
        var absolute = Path.GetFullPath(Root);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("hawala-security-audit-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to clean an unexpected test path.");
        (Environment.Object.WebRootFileProvider as IDisposable)?.Dispose();
        if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);
    }
}
