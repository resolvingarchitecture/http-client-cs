# TODO

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
