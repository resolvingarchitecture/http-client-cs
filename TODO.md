# TODO

- [x] **Identity metadata leak check, done 2026-09-26**: confirmed
      `System.Net.Http.HttpClient` sets no default `User-Agent` (unlike the
      other language ports) - see DESIGN.md "Identity metadata leaks".
      Nothing to fix here.
- [ ] **Real SOCKS5 support, required before any Tor use** - `HttpClientHandler.Proxy`/
      `WebProxy` has no native SOCKS5 client; this can't reach a SOCKS5-only
      relay like `TorSocksRelay` today. See DESIGN.md "Identity metadata
      leaks" - `tor-client-cs`'s own `Socks5.cs` is the reference for what a
      real implementation looks like.
- [ ] Async `SendOutAsync` (currently synchronous `HttpClient.Send`).
- [ ] `NetworkConnectionReport` + status-observer wiring in `ra-common-cs`,
      then use it here instead of the envelope-header-only blocked-response
      reporting (see `DESIGN.md`).
- [ ] `TorClient` (in `tor-client-cs`) reusing this client's `Connect`/proxy
      support instead of its own hand-rolled SOCKS+raw-GET `Http.cs`,
      mirroring `TORClientService extends HTTPService` in Java.
- [ ] Once `ra-common-cs` grows a shared `NetworkService` base (see
      `DESIGN.md`), rebase `HttpClientService` on it.
- [ ] Wire into a future `1m5-core-cs` as `HttpProtocolService`, gated by a
      `1m5.http.enabled` config flag — mirrors `HttpProtocolService` /
      `1m5.http.enabled` in `1m5-core-java`.
