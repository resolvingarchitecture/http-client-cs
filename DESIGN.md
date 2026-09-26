# http-client (C#) — Design

A direct HTTP/HTTPS client, for use as the HTTP **protocol service** by a
future `1m5-core-cs`, the same role `http-client-java`'s `HTTPService` plays
in `1m5-core-java` (see `network.onemfive.core.protocol.HttpProtocolService`
there). A C# port of that design, scoped to outbound sending only.

## Where it sits

    (future) 1m5-core-cs  ──wraps──►  Ra.HttpClient.HttpClientService
                                              │
                                   System.Net.Http.HttpClient
                                              │
                                        the live network

## No `NetworkService` base to extend

Unlike Java, where `ra.http.HTTPService extends ra.common.network.NetworkService`
and `ra.tor.TORClientService extends HTTPService`, **`ra-common-cs` has no
`NetworkService` layer yet** — `Network.cs` says outright "the full network
service layer is deferred to a later phase," and both existing protocol
clients here (`tor-client-cs`'s `TorClient`, `i2p-cs`'s `I2pClient`) are
standalone classes with their own local `Status`/`I2pStatus` enum, not a
shared base. `HttpClientService` follows that same established shape:
standalone, no inheritance, its own `Connect()`/`Disconnect()`/`SendOut()`
surface — there is nothing to extend yet, and inventing a speculative base
class here (rather than in `ra-common-cs`, where every port would need to
agree on it) would be exactly the premature abstraction this codebase
avoids elsewhere.

One divergence from `TorClient`/`I2pClient`'s pattern: this client's
`Status` property is `Ra.Common.NetworkStatus` (from `Network.cs`), not a
new local enum. That type already has the `Blocked`/`PortConflict`/`Error`
vocabulary this client's blocked-response handling needs (see below);
`TorClient`/`I2pClient`'s simpler `Connecting`/`Connected`/`Disconnected`/`Error`
states didn't need that vocabulary, so they didn't reach for it. Both are
reasonable; this one had a closer-fitting type already sitting there.

## Components

    HttpClientOptions   TrustAllCerts, ProxyHost/Port, Timeout; FromConfig(...)
    HttpClientService   Connect()/Disconnect()/IsConnected/SendOut(Envelope)

`Ra.HttpClient` is both the namespace and (with `Service` appended, to avoid
literally shadowing `System.Net.Http.HttpClient`) the class name pattern
`service-bus-cs`/`tor-client-cs` use elsewhere in this project.

## Message flow

**Outbound** — `SendOut(envelope)` connects lazily (matching
`HTTPService.sendOut`'s `if(!isConnected() && !connect())`), resolves a URL
from `Envelope.Url` or (if unset) a `SimpleExternalRoute`'s
`Destination.Id` as an `http://` host, builds a `System.Net.Http.HttpRequestMessage`
for the `Envelope.Action` (`Get`/`Post`/`Put`/`Delete`), copies
`Authorization`/`User-Agent` onto the request and
`Content-Type`/`Content-Disposition`/`Content-Transfer-Encoding` onto the
request *content* (`HttpContent.Headers`, not `HttpRequestMessage.Headers` —
a .NET API split with no equivalent in OkHttp's flatter header model), and
sends synchronously (`HttpClient.Send`, not `SendAsync` — this port, like
`TorClient`/`I2pClient`, is a synchronous surface; adding an async overload
is TODO). The response body always lands on `envelope.AddContent(...)` as a
`JsonValue` wrapping the raw `byte[]` (base64 on the wire — `System.Text.Json`'s
own convention, same precedent `tor-client-cs`'s `DESIGN.md` already
documents for the same reason: no distinct binary `JsonNode` kind).

Body construction mirrors `HTTPService.sendOut`'s three cases: a
`Multipart` on the envelope wins (body = `Finish()`, `Content-Type` forced
to `multipart/form-data; boundary=...`); else a `SimpleExternalRoute` with
`SendContentOnly` sends `Envelope.Content()` raw (`byte[]` unwrapped
directly, string UTF‑8 encoded, anything else JSON-serialized as a
fallback); else the whole envelope is serialized with `ToJsonString()` and
sent as the body — matching `e.toJSON().getBytes()` in Java.

**Inbound** — not implemented, matching every other port; there is no
listener side to this client at all (see "Not here").

## Blocked-response handling, without `NetworkConnectionReport`

`HTTPService.handleFailure` maps a closed set of response codes
(403/408/410/418/451/511) to `BLOCKED-*` reasons and reports them through
`ra.common.network.NetworkConnectionReport` to the service's status
observers. **`ra-common-cs` has no `NetworkConnectionReport` type** (nor an
observer mechanism for one) — same gap as the missing `NetworkService`
base. `HandleFailure` here reproduces the code→reason mapping and logs it,
but only records the reason on `envelope.Headers["error"]`; it does *not*
change `HttpClientService.Status`, since a single request's block shouldn't
silently make a healthy client report itself disconnected for every
subsequent, unrelated request. Wiring a real `NetworkConnectionReport` +
observer into `ra-common-cs` is TODO, shared with `tor-client-cs`/`i2p-cs`.

## C# adaptations vs. the Java original

- **Synchronous `HttpClient.Send`**, not `SendAsync` — matches this port's
  synchronous `SendOut` surface (see above); an async variant is TODO.
- **`HttpContent.Headers` vs `HttpRequestMessage.Headers` split** — .NET
  requires content-describing headers to live on the `HttpContent`, unlike
  OkHttp's single flat `Headers` object in `HTTPService.sendOut`; handled by
  building the request content first, then attaching those three headers to
  it (see "Message flow").
- **Trust-all-certs** via `HttpClientHandler.ServerCertificateCustomValidationCallback`,
  .NET's equivalent of OkHttp's `X509TrustManager` override — test-only,
  same as Java's `RA_HTTP_CLIENT_TRUST_ALL`.
- **Proxy** via `HttpClientHandler.Proxy` (a `WebProxy`), OkHttp's
  `Proxy`-on-the-builder equivalent.

## Identity metadata leaks

Required standard for any HTTP client this project relies on for anonymized
traffic (Tor/I2P), enforced here and checked against every sibling
`http-client-*` port: no default header, response header, or connection
behavior may reveal more about the requester than it has to.

- **Confirmed safe, 2026-09-26**: unlike `http-client-java` (OkHttp's own
  `User-Agent: okhttp/<version>` default, confirmed via bytecode),
  `http-client-cpp`/`http-client-python` (both previously defaulted to the
  project-identifying literal `"ra-http-client"`, since fixed), and
  `http-client-go`/`-rust`/`-ts` (flagged, not yet fixed, for their own
  stdlib/library defaults), `System.Net.Http.HttpClient` injects no default
  `User-Agent` of its own - a well-documented .NET behavior. Since this
  client only sets `User-Agent` when the caller's `Envelope` supplies one
  (see "Message flow" above), a request with none set genuinely sends none
  - nothing to fix here.
- **Not yet verified**: `HttpClientHandler.Proxy`/`WebProxy` is an HTTP
  CONNECT-style proxy abstraction with no native SOCKS5 client support in
  the BCL. This client likely cannot reach a SOCKS5-only relay like
  `tor-client-java`'s `TorSocksRelay` at all today - a functional gap, not
  a metadata leak, but one that would need closing (e.g. wrapping
  `tor-client-cs`'s own already-hand-rolled `Socks5.cs`, mirroring how
  `http-client-cpp`'s `socks5.hpp` is a standalone SOCKS5 client) before
  this client could route anything through Tor. Not attempted here.
- **No server/inbound half** (see "Not here" below), so the third known
  leak shape - a server-identifying response header, found and fixed in
  `http-client-java`'s Jetty listener (`Server: Jetty(<version>)`) - doesn't
  apply yet. Check for it if inbound hosting is ever built.

## Not here

- Inbound HTTP server hosting: the Java original's Jetty-based
  `EnvelopeHandler`/`SPAHandler`/`EnvelopeWebSocket`/`EnvelopeJSONDataHandler`/
  `EnvelopeProxyDataHandler` (SPA hosting, WebSocket upgrade, session
  handling). No other language port of this client needs it; it exists in
  Java only because `TORClientService` there also hosts the onion service's
  local listener side. `TorClient.SendOut`-over-this-client (mirroring the
  Java `TORClientService extends HTTPService` relationship, over this
  client's `Connect`/proxy support instead of hand-rolled SOCKS+raw-GET) is
  a natural follow-up but out of scope here.
- Async `SendOutAsync`.
- `NetworkConnectionReport` / status-observer wiring (see above) — depends
  on `ra-common-cs` gaining the type first.
- Redirected-request body re-attachment beyond what
  `HttpClientHandler.AllowAutoRedirect` does automatically.
