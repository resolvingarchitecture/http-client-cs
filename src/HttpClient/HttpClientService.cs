using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Ra.Common;
using Ra.Common.Routing;

namespace Ra.HttpClient;

/// <summary>
/// Configuration for <see cref="HttpClientService"/>. Config keys (see README.md):
/// <c>ra.http.trustAllCerts</c>, <c>ra.http.proxyHost</c>, <c>ra.http.proxyPort</c>,
/// <c>ra.http.timeoutSecs</c>.
/// </summary>
public sealed class HttpClientOptions
{
    public bool TrustAllCerts { get; set; }
    public string? ProxyHost { get; set; }
    public int? ProxyPort { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    public static HttpClientOptions FromConfig(IReadOnlyDictionary<string, string> config)
    {
        var options = new HttpClientOptions();
        if (config.TryGetValue("ra.http.trustAllCerts", out var trustAll) && bool.TryParse(trustAll, out var t))
        {
            options.TrustAllCerts = t;
        }
        if (config.TryGetValue("ra.http.proxyHost", out var host)) options.ProxyHost = host;
        if (config.TryGetValue("ra.http.proxyPort", out var portStr) && int.TryParse(portStr, out var port))
        {
            options.ProxyPort = port;
        }
        if (config.TryGetValue("ra.http.timeoutSecs", out var timeoutStr) && double.TryParse(timeoutStr, out var secs))
        {
            options.Timeout = TimeSpan.FromSeconds(secs);
        }
        return options;
    }
}

/// <summary>
/// Direct (non-anonymized) HTTP/HTTPS client. A C# port of <c>http-client-java</c>'s
/// <c>ra.http.HTTPService</c> - outbound (<c>SendOut</c>) only. See <c>DESIGN.md</c> for
/// the scope cut against the Java original's Jetty-based inbound server hosting, which
/// no other language port of this client needs.
/// </summary>
public sealed class HttpClientService(HttpClientOptions? options = null) : IDisposable
{
    public const string HeaderError = "error";

    private readonly HttpClientOptions _options = options ?? new HttpClientOptions();
    private volatile NetworkStatus _status = NetworkStatus.Closed;
    private System.Net.Http.HttpClient? _client;

    public NetworkStatus Status => _status;
    public bool IsConnected => _status == NetworkStatus.Connected;

    public static HttpClientService FromConfig(IReadOnlyDictionary<string, string> config) =>
        new(HttpClientOptions.FromConfig(config));

    /// <summary>Builds the underlying <see cref="System.Net.Http.HttpClient"/>. Never throws;
    /// returns <c>false</c> and sets <see cref="NetworkStatus.Error"/> on failure.</summary>
    public bool Connect()
    {
        _status = NetworkStatus.Connecting;
        try
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            if (_options.TrustAllCerts)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }
            if (_options.ProxyHost is not null && _options.ProxyPort is not null)
            {
                handler.Proxy = new WebProxy(_options.ProxyHost, _options.ProxyPort.Value);
                handler.UseProxy = true;
            }
            _client = new System.Net.Http.HttpClient(handler) { Timeout = _options.Timeout };
            _status = NetworkStatus.Connected;
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"HTTP client connect failed: {e.Message}");
            _status = NetworkStatus.Error;
            return false;
        }
    }

    public bool Disconnect()
    {
        _client?.Dispose();
        _client = null;
        _status = NetworkStatus.Disconnected;
        return true;
    }

    /// <summary>
    /// Sends <paramref name="envelope"/> out over HTTP/HTTPS. The URL comes from
    /// <see cref="Envelope.Url"/> or, if unset, a <see cref="SimpleExternalRoute"/>
    /// destination's <see cref="NetworkPeer.Id"/> treated as an <c>http://</c> host -
    /// matching <c>ra.http.HTTPService.sendOut</c>. On any HTTP response the body is
    /// written to <c>envelope</c>'s document content (<see cref="Envelope.AddContent"/>);
    /// on a 403/408/410/418/451/511 response or a transport failure,
    /// <c>envelope.Headers["error"]</c> is set and <see cref="NetworkStatus.Blocked"/> or
    /// <see cref="NetworkStatus.Error"/> is recorded on <see cref="Status"/>.
    /// </summary>
    public bool SendOut(Envelope envelope)
    {
        if (!IsConnected && !Connect())
        {
            envelope.SetHeader(HeaderError, "HTTP client not connected and unable to connect");
            return false;
        }

        var url = ResolveUrl(envelope);
        if (url is null)
        {
            envelope.SetHeader(HeaderError, "Must provide either Envelope.Url or an external route with a destination NetworkPeer");
            return false;
        }

        using var request = new HttpRequestMessage(ResolveMethod(envelope), url);
        ApplyPlainHeaders(envelope, request);
        ApplyBody(envelope, request);

        try
        {
            using var response = _client!.Send(request);
            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            envelope.AddContent(JsonValue.Create(bytes));
            if (!response.IsSuccessStatusCode)
            {
                HandleFailure(envelope, (int)response.StatusCode, url);
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"HTTP request to {url} failed: {e.Message}");
            envelope.SetHeader(HeaderError, e.Message);
            _status = NetworkStatus.Error;
            return false;
        }
    }

    private static string? ResolveUrl(Envelope envelope)
    {
        if (!string.IsNullOrEmpty(envelope.Url)) return envelope.Url;
        if (envelope.GetRoute() is SimpleExternalRoute { Destination.Id: { } id }) return $"http://{id}";
        return null;
    }

    private static HttpMethod ResolveMethod(Envelope envelope) => envelope.Action switch
    {
        EnvelopeAction.Post => HttpMethod.Post,
        EnvelopeAction.Put => HttpMethod.Put,
        EnvelopeAction.Delete => HttpMethod.Delete,
        _ => HttpMethod.Get,
    };

    private static void ApplyPlainHeaders(Envelope envelope, HttpRequestMessage request)
    {
        if (StringHeader(envelope, HeaderNames.Authorization) is { } auth)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, auth);
        }
        if (StringHeader(envelope, HeaderNames.UserAgent) is { } ua)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.UserAgent, ua);
        }
    }

    /// <summary>Builds the request body (multipart, raw content, or the whole envelope as
    /// JSON, matching <c>HTTPService.sendOut</c>'s three cases) for POST/PUT/DELETE and
    /// attaches the Content-Type/Content-Disposition/Content-Transfer-Encoding headers to
    /// it - those live on <see cref="HttpContent.Headers"/> in .NET, not the request.</summary>
    private static void ApplyBody(Envelope envelope, HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get) return;

        byte[] bodyBytes;
        string? contentType = StringHeader(envelope, HeaderNames.ContentType);

        if (envelope.Multipart is { } multipart)
        {
            bodyBytes = Encoding.UTF8.GetBytes(multipart.Finish());
            contentType = $"multipart/form-data; boundary={multipart.Boundary}";
        }
        else if (envelope.GetRoute() is SimpleExternalRoute { SendContentOnly: true })
        {
            bodyBytes = ContentBytes(envelope.Content()) ?? [];
        }
        else
        {
            bodyBytes = Encoding.UTF8.GetBytes(envelope.ToJsonString(indent: false));
        }

        var content = new ByteArrayContent(bodyBytes);
        if (contentType is not null && MediaTypeHeaderValue.TryParse(contentType, out var mt))
        {
            content.Headers.ContentType = mt;
        }
        if (StringHeader(envelope, HeaderNames.ContentDisposition) is { } disposition)
        {
            content.Headers.TryAddWithoutValidation(HeaderNames.ContentDisposition, disposition);
        }
        if (StringHeader(envelope, HeaderNames.ContentTransferEncoding) is { } encoding)
        {
            content.Headers.TryAddWithoutValidation(HeaderNames.ContentTransferEncoding, encoding);
        }
        request.Content = content;
    }

    private static byte[]? ContentBytes(JsonNode? content) => content switch
    {
        null => null,
        JsonValue v when v.TryGetValue<byte[]>(out var bytes) => bytes,
        JsonValue v when v.TryGetValue<string>(out var s) => Encoding.UTF8.GetBytes(s),
        _ => Encoding.UTF8.GetBytes(content.ToJsonString()),
    };

    private static string? StringHeader(Envelope envelope, string name) =>
        envelope.Header(name) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Maps a subset of response codes to a <c>BLOCKED-*</c> reason, matching
    /// <c>HTTPService.handleFailure</c>. Ported without a <c>NetworkConnectionReport</c>
    /// type - <c>ra-common-cs</c> doesn't have one yet (see DESIGN.md) - so the reason is
    /// recorded on the envelope only; it does not affect <see cref="Status"/>, which
    /// tracks the client's own connection, not any one request's outcome.</summary>
    private static void HandleFailure(Envelope envelope, int code, string url)
    {
        var reason = code switch
        {
            403 => "BLOCKED-FORBIDDEN",
            408 => "BLOCKED-TIMEOUT",
            410 => "BLOCKED-GONE",
            418 => "BLOCKED-TEAPOT",
            451 => "BLOCKED-LEGAL",
            511 => "BLOCKED-AUTHN",
            _ => null,
        };
        if (reason is not null)
        {
            Console.Error.WriteLine($"HTTP {code} from {url}: {reason}");
        }
        envelope.SetHeader(HeaderError, code.ToString());
    }

    public void Dispose() => Disconnect();
}
