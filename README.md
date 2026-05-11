## Sky API

## Deploying
This project should be deployed within a container. 

### Configuration
See appsettings.json  
and [jaeger](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Jaeger/README.md#environment-variables)

### Rate Limiting

The API supports both IP-based rate limiting (for public/anonymous requests) and Client-based rate limiting (for authenticated clients with higher quotas).

#### IP Rate Limiting (Default)
By default, all requests are rate limited by IP address. The shipped defaults in `appsettings.json` under `IpRateLimiting` are `30 requests / 10 seconds` and `100 requests / minute`, and both windows apply concurrently.

The middleware only emits `X-Rate-Limit-*` headers for the longest matching window. With the default configuration that usually means the `1m` rule, so callers can still hit the `10s` burst rule without seeing a separate burst header set ahead of time.

#### Client Rate Limiting (Custom / Premium Clients)
Custom clients can use a client ID header (`X-ClientId`) to receive their own quotas or bypass the default IP bucket entirely. This is configured separately from normal user authentication via `Authorization: Bearer` or `GoogleToken`.

**Environment Variables for Client Rate Limiting:**

| Variable | Description | Example |
|----------|-------------|---------|
| `PREMIUM_CLIENT_IDS` | Comma-separated list of client IDs to whitelist (no rate limits) | `client-1,client-2,client-3` |
| `PREMIUM_CLIENT_RULES` | Custom rate limit rules per client. Format: `clientId:period=limit,period=limit;clientId2:...` | `premium:1s=20,1m=500;vip:1s=50,1m=1000` |

**Example Usage:**

```bash
# Whitelist specific clients (no rate limits applied)
export PREMIUM_CLIENT_IDS="trusted-partner-1,trusted-partner-2"

# Configure custom limits for premium clients
export PREMIUM_CLIENT_RULES="basic-tier:10s=50,1m=200;premium-tier:10s=100,1m=500"
```

**Making requests with Client ID:**

```bash
# Request with client ID header (uses client-specific limits if configured)
curl -H "X-ClientId: your-client-id" https://api.example.com/api/endpoint
```

**How it works:**
1. All requests start with the default limits of `30 requests / 10 seconds` and `100 requests / minute`
2. If a request contains the `X-ClientId` header and the client ID is in `PREMIUM_CLIENT_IDS`, no rate limits apply
3. If the client ID has custom rules configured via `PREMIUM_CLIENT_RULES`, those limits apply
4. If no client ID is provided or it doesn't match any rules, IP-based rate limits apply

