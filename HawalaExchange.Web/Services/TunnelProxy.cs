using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace HawalaExchange.Web.Services;

public static class TunnelProxy
{
    public static void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto;
        options.ForwardLimit=1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
        options.KnownProxies.Add(IPAddress.Loopback.MapToIPv6());
    }
}
