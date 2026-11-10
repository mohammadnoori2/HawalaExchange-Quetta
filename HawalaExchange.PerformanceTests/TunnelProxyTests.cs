using System.Net;
using HawalaExchange.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HawalaExchange.PerformanceTests;

public class TunnelProxyTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.168.0.50", false)]
    public async Task OnlyLocalTunnelCanForwardSchemeAndClientAddress(string remote, bool trusted)
    {
        var options=new ForwardedHeadersOptions();TunnelProxy.Configure(options);
        var context=new DefaultHttpContext();context.Request.Scheme="http";
        context.Connection.RemoteIpAddress=IPAddress.Parse(remote);
        context.Request.Headers["X-Forwarded-Proto"]="https";
        context.Request.Headers["X-Forwarded-For"]="203.0.113.9";
        context.Request.Headers["X-Forwarded-Host"]="untrusted.example";
        context.Request.Host=new HostString("original.example");
        var middleware=new ForwardedHeadersMiddleware(_=>Task.CompletedTask,NullLoggerFactory.Instance,Options.Create(options));
        await middleware.Invoke(context);
        Assert.Equal(trusted?"https":"http",context.Request.Scheme);
        Assert.Equal(trusted?"203.0.113.9":remote,context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("original.example",context.Request.Host.Value);
    }
}
