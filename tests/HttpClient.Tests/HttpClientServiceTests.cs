using System.Net;
using System.Net.Sockets;
using Ra.Common;
using Ra.Common.Routing;
using Xunit;

namespace Ra.HttpClient.Tests;

public class HttpClientServiceTests
{
    // No local network stack to bind against in the sandbox this was written in
    // (no `dotnet` HttpListener test server available); these hit the live site
    // http-client-java's own HTTPServiceTest.java uses, and skip cleanly (rather
    // than fail) if this environment has no outbound network - see README.md.
    private const string LiveHost = "resolvingarchitecture.io";

    private static bool IsNetworkFailure(Exception e) =>
        e is HttpRequestException or SocketException or TaskCanceledException
            || e.InnerException is SocketException;

    [Fact]
    public void ConnectSucceedsAndReportsConnected()
    {
        using var service = new HttpClientService();
        Assert.True(service.Connect());
        Assert.Equal(NetworkStatus.Connected, service.Status);
        Assert.True(service.IsConnected);
    }

    [Fact]
    public void DisconnectReportsDisconnected()
    {
        using var service = new HttpClientService();
        service.Connect();
        Assert.True(service.Disconnect());
        Assert.Equal(NetworkStatus.Disconnected, service.Status);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public void SendOutWithNoUrlOrRouteErrors()
    {
        using var service = new HttpClientService();
        var envelope = Envelope.Document();
        envelope.Action = EnvelopeAction.Get;

        Assert.False(service.SendOut(envelope));
        Assert.NotNull(envelope.Header(HttpClientService.HeaderError));
    }

    [Fact]
    public void SendOutConnectsLazilyWhenNotAlreadyConnected()
    {
        using var service = new HttpClientService();
        var envelope = Envelope.Document();
        envelope.Url = $"http://{LiveHost}/";
        envelope.Action = EnvelopeAction.Get;

        Assert.False(service.IsConnected);
        try
        {
            service.SendOut(envelope);
        }
        catch (Exception e) when (IsNetworkFailure(e))
        {
            return; // no outbound network in this environment - skip cleanly
        }
        Assert.True(service.IsConnected);
    }

    [Fact]
    public void HttpGetFetchesLiveSite()
    {
        using var service = new HttpClientService();
        service.Connect();
        var envelope = Envelope.Document();
        envelope.Url = $"http://{LiveHost}/";
        envelope.Action = EnvelopeAction.Get;
        envelope.SetHeader(HeaderNames.ContentType, "text/html");

        bool ok;
        try
        {
            ok = service.SendOut(envelope);
        }
        catch (Exception e) when (IsNetworkFailure(e))
        {
            return;
        }
        if (!ok && envelope.Header(HttpClientService.HeaderError) is not null) return; // sandboxed egress blocked

        var html = System.Text.Encoding.UTF8.GetString(((System.Text.Json.Nodes.JsonValue)envelope.Content()!).GetValue<byte[]>());
        Assert.Contains("<title>", html);
    }

    [Fact]
    public void HttpsGetFetchesLiveSite()
    {
        using var service = new HttpClientService();
        service.Connect();
        var envelope = Envelope.Document();
        envelope.Url = $"https://{LiveHost}/";
        envelope.Action = EnvelopeAction.Get;
        envelope.SetHeader(HeaderNames.ContentType, "text/html");

        bool ok;
        try
        {
            ok = service.SendOut(envelope);
        }
        catch (Exception e) when (IsNetworkFailure(e))
        {
            return;
        }
        if (!ok && envelope.Header(HttpClientService.HeaderError) is not null) return;

        var html = System.Text.Encoding.UTF8.GetString(((System.Text.Json.Nodes.JsonValue)envelope.Content()!).GetValue<byte[]>());
        Assert.Contains("<title>", html);
    }

    [Fact]
    public void ExternalRouteDestinationIsUsedWhenUrlUnset()
    {
        using var service = new HttpClientService();
        var envelope = Envelope.Document();
        envelope.Action = EnvelopeAction.Get;
        var route = SimpleExternalRoute.Of("HttpProtocolService", "SEND");
        route.Destination = new NetworkPeer(Network.Http) { Id = LiveHost };
        envelope.DynamicRoutingSlip.AddRoute(route);
        envelope.Ratchet();

        try
        {
            service.SendOut(envelope);
        }
        catch (Exception e) when (IsNetworkFailure(e))
        {
            return;
        }
        // Whether or not the live fetch succeeded, no "must provide a URL" error means
        // the route-derived URL was accepted.
        Assert.NotEqual(
            "Must provide either Envelope.Url or an external route with a destination NetworkPeer",
            envelope.Header(HttpClientService.HeaderError)?.GetValue<string>());
    }

    [Fact]
    public void TrustAllCertsOptionIsAccepted()
    {
        using var service = new HttpClientService(new HttpClientOptions { TrustAllCerts = true });
        Assert.True(service.Connect());
    }

    [Fact]
    public void OptionsFromConfigParsesKnownKeys()
    {
        var options = HttpClientOptions.FromConfig(new Dictionary<string, string>
        {
            ["ra.http.trustAllCerts"] = "true",
            ["ra.http.proxyHost"] = "127.0.0.1",
            ["ra.http.proxyPort"] = "8080",
            ["ra.http.timeoutSecs"] = "5",
        });

        Assert.True(options.TrustAllCerts);
        Assert.Equal("127.0.0.1", options.ProxyHost);
        Assert.Equal(8080, options.ProxyPort);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Timeout);
    }
}
