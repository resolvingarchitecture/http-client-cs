# http-client (C#)

A direct (non-anonymized) HTTP/HTTPS client for **1M5**. A C# port of
[`http-client-java`](https://github.com/resolvingarchitecture/http-client-java)'s
`ra.http.HTTPService` — outbound (`SendOut`) only; see `DESIGN.md` for the
scope cut against the Java original's Jetty-based inbound server hosting.

## Use

```csharp
using Ra.Common;
using Ra.HttpClient;

using var client = new HttpClientService();     // or new HttpClientService(options)
var env = Envelope.Document();
env.Url = "https://resolvingarchitecture.io/";
env.Action = EnvelopeAction.Get;
if (client.SendOut(env))                        // connects lazily if not already connected
{
    var bytes = ((System.Text.Json.Nodes.JsonValue)env.Content()!).GetValue<byte[]>();
}
```

POST/PUT/DELETE, `Multipart`, and the standard headers (`Authorization`,
`Content-Type`, `Content-Disposition`, `Content-Transfer-Encoding`,
`User-Agent`) are all read straight off the `Envelope`, matching
`HTTPService.sendOut`.

### Config keys

| key | default | meaning |
|-----|---------|---------|
| `ra.http.trustAllCerts` | `false` | skip TLS certificate validation (test-only) |
| `ra.http.proxyHost` / `ra.http.proxyPort` | unset | route requests through an HTTP proxy |
| `ra.http.timeoutSecs` | `60` | per-request timeout |

```csharp
var client = HttpClientService.FromConfig(config); // IReadOnlyDictionary<string, string>
```

## Build

```
dotnet build
dotnet test
```

Depends on `ra-common-cs` via a relative `ProjectReference`
(`../../common/ra-common-cs`), matching `tor-client-cs`/`i2p-cs`'s
monorepo-dependency convention. Two of the nine tests make live HTTPS/HTTP
requests to `resolvingarchitecture.io` and skip cleanly (pass without
asserting) if there's no outbound network — see `HttpClientServiceTests.cs`.

## Identity metadata leaks

Checked 2026-09-26, after the same class of bug was found and fixed in
`http-client-java`/`-cpp`/`-python`/`1m5-remnant`'s Android `TorClient`, and
flagged (not yet fixed) in `-go`/`-rust`/`-ts`: unlike every one of those,
**this client has no default-`User-Agent` leak to fix** -
`System.Net.Http.HttpClient` does not inject any default `User-Agent` of its
own (a well-documented .NET behavior, the opposite gotcha from most other
stacks: some servers reject a request with *no* `User-Agent` at all, which
is why .NET developers often have to add one explicitly). Since this
client, like the others, only sets `User-Agent` when the caller's
`Envelope` supplies one, a request with none set genuinely sends none here
- no identifying default to leak. One thing still open: `HttpClientHandler.Proxy`/`WebProxy`
is an HTTP CONNECT-style proxy abstraction with no native SOCKS5 support, so
this client cannot yet correctly reach a SOCKS5-only relay like
`tor-client-java`'s `TorSocksRelay` at all - see `DESIGN.md` "Identity
metadata leaks".

## Status

Client only — GET/POST/PUT/DELETE, HTTP and HTTPS (via the BCL
`System.Net.Http.HttpClient`, no extra dependency), an optional HTTP proxy,
and a test-only trust-all-certs mode. No inbound server hosting (the Java
original's Jetty-based `EnvelopeHandler`/`SPAHandler`/`EnvelopeWebSocket`) —
no other language port of this client needs it. See `DESIGN.md` and
`TODO.md`.
