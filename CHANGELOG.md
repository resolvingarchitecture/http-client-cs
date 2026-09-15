# Changelog

## 0.1.0

- Initial C# port of `http-client-java`'s `ra.http.HTTPService`, outbound
  (`SendOut`) only: `HttpClientService` — GET/POST/PUT/DELETE, HTTP and
  HTTPS via `System.Net.Http.HttpClient`, `Multipart` bodies, the standard
  headers, an optional HTTP proxy, and a test-only trust-all-certs mode.
- `HttpClientOptions.FromConfig` for `ra.http.trustAllCerts` /
  `ra.http.proxyHost` / `ra.http.proxyPort` / `ra.http.timeoutSecs`.
