## Sky API

## Deploying
This project should be deployed within a container. 

### Configuration
See appsettings.json  
and [jaeger](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Jaeger/README.md#environment-variables)

Container-based deployments should keep configuration in environment variables or mounted secrets instead of editing files inside the image.

### AI chat and knowledge index

`POST /api/data/ai` accepts `{ "conversationId": "optional-server-id", "message": "...", "page": "/item/..." }`.
Omit `conversationId` to start a session, then send the returned id with follow-ups. The server keeps the five most recent conversational rounds and permits at most five rounds of DeepSeek tool calls. The legacy `GET /api/data/ai?prompt=...` route remains available for older clients.

Required production configuration:

| Variable | Purpose |
|----------|---------|
| `DEEPSEEK_API_KEY` | DeepSeek API credential (store in OpenBao) |
| `DEEPSEEK_BASE_URL` | OpenAI-compatible base URL; defaults to `https://api.deepseek.com` |
| `DEEPSEEK_MODEL` | Model name; defaults to `deepseek-v4-flash` |
| `DEEPSEEK_THINKING` / `DEEPSEEK_REASONING_EFFORT` | Thinking defaults to `true` with `high` effort; exact reasoning/tool transcripts are kept in Redis |
| `DEEPSEEK_MAX_TOKENS` | Per-completion output ceiling; defaults to `4096` |
| `OPENSEARCH_URL` | Cluster URL, e.g. `https://opensearch-cluster-nodes.opensearch:9200` |
| `OPENSEARCH_USERNAME` / `OPENSEARCH_PASSWORD` | The fleet `sky-api` identity and its out-of-band password |
| `OPENSEARCH_VERIFY_TLS` | Keep `true` when the cluster CA is trusted; explicitly set `false` only for an internal generated certificate that cannot be mounted |
| `OPENSEARCH_NUMBER_OF_REPLICAS` | Knowledge-index replicas; defaults to `1` for the three-node production cluster and development overrides it to `0` for a single local node |
| `EMBEDDING_API_URL` / `EMBEDDING_API_KEY` | Optional OpenAI-compatible embeddings endpoint. With no endpoint, a deterministic local lexical embedding is used. |
| `EMBEDDING_MODEL` / `EMBEDDING_DIMENSIONS` | Must match the configured embedding endpoint; defaults to `BAAI/bge-small-en-v1.5` and `384` |
| `EMBEDDING_BATCH_SIZE` | Embedding request batch size; defaults to the TEI-compatible maximum of `32` |
| `AI_CONVERSATION_TTL_HOURS` | Redis conversation retention; defaults to 24 hours |
| `KNOWLEDGE_SITEMAPS` / `KNOWLEDGE_URLS` | Comma-separated crawl entry points and extra pages |
| `KNOWLEDGE_ALLOWED_HOSTS` | Comma-separated crawl allowlist; defaults to `sky.coflnet.com` |
| `KNOWLEDGE_MAX_INDEX_BYTES` | Source ingestion budget; defaults to 2 GiB (`2147483648`) |
| `KNOWLEDGE_REFRESH_TOKEN` | Shared secret for the internal promotion-triggered refresh endpoint |
| `KNOWLEDGE_MIN_REFRESH_HOURS` | Stale-running-marker timeout; defaults to 12 hours |
| `KNOWLEDGE_QUESTION_MODEL` | DeepSeek model used to generate three query-style questions for new or changed wiki/guide chunks |

SkyApi replicas do not index at startup. After promoting hypixel-react, Argo calls the authenticated refresh endpoint once with the frontend commit SHA. Store the same generated token as `KNOWLEDGE_REFRESH_TOKEN` at `kv/sky/sky-api` and as `knowledge_refresh_token` at `kv/argo-workflows/promote`. An atomic OpenSearch marker prevents workflow retries from indexing the same revision twice, and an interrupted running marker becomes retryable after `KNOWLEDGE_MIN_REFRESH_HOURS`. The indexer compares each chunk's content hash and embedding profile with OpenSearch, so unchanged chunks are neither regenerated nor re-embedded. New or changed wiki and guide chunks are enriched with three questions they answer before embedding; OpenAPI descriptions skip this pass because their method, path, summary, and parameters are already query-oriented. Search combines BM25 and vector ranks. The 2 GiB setting is a ceiling, not reserved disk space: the index is not padded when the complete source corpus is smaller, and OpenSearch replicas/vector structures can make physical usage differ from source bytes.

Daily AI message limits are enforced in Redis: anonymous IPs 3, logged-in users 10, Starter Premium 20, Premium 50, and Premium+ 200. Tool calls do not consume additional user messages.

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
