## Sky API

## Deploying
This project should be deployed within a container. 

### Configuration
See appsettings.json  
and [jaeger](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Jaeger/README.md#environment-variables)

### Rate Limiting

The API supports both IP-based rate limiting (for public/anonymous requests) and Client-based rate limiting (for authenticated clients with higher quotas).

#### IP Rate Limiting (Default)
By default, all requests are rate limited by IP address. The default limits are configured in `appsettings.json` under `IpRateLimiting`.

#### Client Rate Limiting (Premium Clients)
Premium clients can bypass IP rate limits by passing a client ID header (`X-ClientId`). 

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
1. If a request contains the `X-ClientId` header and the client ID is in `PREMIUM_CLIENT_IDS`, no rate limits apply
2. If the client ID has custom rules configured via `PREMIUM_CLIENT_RULES`, those limits apply
3. If no client ID is provided or it doesn't match any rules, IP-based rate limits apply

