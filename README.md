# WithLove Gift Shop

<img src="docs/image.png" alt="WithLove Gift Shop" width="70%" />

WithLove is a sample e-commerce application that shows how AI can be integrated into a web application. It includes a curated gift shop with hybrid search (full-text + vector), and an AI-powered chat shopping assistant.
The sample also uses OpenAI models for inference and embedding generation.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Redis, SQL Server, and Temporal containers)
- [Stripe CLI](https://github.com/stripe/stripe-cli) — for local webhook forwarding (`brew install stripe/stripe-cli/stripe` on macOS)
- **OpenAI API key** — used by the chat assistant and embedding generation
- **Stripe API keys** (test mode) — used for checkout

Local development pins `temporalio/temporal:1.7.2` (Temporal Server 1.31.1), which satisfies the
durable AI extension's Temporal Server 1.31.0 minimum.

## Configuration

API keys are defined once in the AppHost using [Aspire parameters](https://learn.microsoft.com/dotnet/aspire/fundamentals/external-parameters) and automatically injected into each project as environment variables. No need to duplicate keys across project appsettings files.

Set up secrets using the Aspire CLI (Aspire 13.2+). Run from the repo root — Aspire auto-discovers the AppHost:

```bash
aspire secret set Parameters:openai-api-key "<your-openai-key>"
aspire secret set Parameters:stripe-api-key "<your-stripe-secret-key>"
aspire secret set Parameters:stripe-public-key "<your-stripe-public-key>"
aspire secret set Parameters:redis-password "<local-redis-password>"
```

### Telemetry destinations and content security

Only traces switch between Arize backends. Logs and metrics continue to use the Aspire dashboard
in every local mode. Local runs default to the AppHost-managed Phoenix container; publish mode
defaults to AX.

The root `justfile` exposes all four supported local combinations:

| Destination | AI payload content | Command |
|---|---|---|
| Aspire dashboard | Redacted/omitted | `just run-aspire` |
| Aspire dashboard | Captured | `just run-aspire --capture` |
| Phoenix | Redacted/omitted | `just run` or `just run-phoenix` |
| Phoenix | Captured | `just run-phoenix --capture` |
| Phoenix | All trace spans | `just run-phoenix --all-traces` |
| Arize AX | Redacted/omitted | `just run-ax` |
| Arize AX | Captured | `just run-ax --capture` |
| Arize AX | All trace spans | `just run-ax --all-traces` |

The `--capture` flag is an explicit opt-in. It permits the Web and WorkflowServer resources to
export chat inputs and outputs, model messages and system instructions, and tool arguments and
results. Product retrieval and embedding payloads remain hidden. Captured content can contain
customer or business-sensitive data, and changing the flag affects only new telemetry; it does not
redact or delete data already retained by Phoenix or AX.

`--all-traces` is a separate diagnostic opt-out from AI-only filtering. It sets
`Trace__AiOnly=false` for the run, so Phoenix or AX receives all trace spans. It does not capture
AI payload content; combine it with `--capture` only when both are deliberately needed.

Before using AX, store its connection values in the Aspire secret store. Use the endpoint and
base64 space ID shown on the AX connect page; the sample does not assume an AX region.

```bash
aspire secret set ARIZE_OTLP_ENDPOINT "<endpoint-from-the-AX-connect-page>"
aspire secret set ARIZE_API_KEY "<your-AX-api-key>"
aspire secret set ARIZE_SPACE_ID "<your-AX-space-id>"
just run-ax
```

The equivalent direct Aspire commands are:

```bash
# Phoenix, content hidden
Trace__Destination=Phoenix Telemetry__CaptureAiContent=false \
  aspire start --apphost src/WithLove.AppHost/WithLove.AppHost.csproj

# AX, content explicitly captured
Trace__Destination=Ax Telemetry__CaptureAiContent=true \
  aspire start --apphost src/WithLove.AppHost/WithLove.AppHost.csproj

# Aspire dashboard, content hidden
Trace__Destination=Aspire Telemetry__CaptureAiContent=false \
  aspire start --apphost src/WithLove.AppHost/WithLove.AppHost.csproj
```

Configuration keys use `:` in configuration and `__` in environment-variable form:

| Setting | Default | Effect |
|---|---|---|
| `Trace:Destination` / `Trace__Destination` | Phoenix locally; AX when published | Selects `Aspire`, `Phoenix`, or `Ax` as the sole trace destination. |
| `Trace:AiOnly` / `Trace__AiOnly` | `true` | Sends only the connected AI trajectory to Phoenix or AX; Aspire always receives full traces. Set `false` only to send all selected-backend traces. |
| `Telemetry:CaptureAiContent` / `Telemetry__CaptureAiContent` | `false` | Application-level authorization for sensitive AI payload export. This is what `--capture` sets. |

`Telemetry:CaptureAiContent` is the only setting that can authorize AI payload capture. It defaults
to `false`; set it to `true` only when the exported prompts, responses, and tool payloads are safe
for the selected tracing backend. AX credentials are attached only to the AX trace exporter and are
not sent to Aspire's log or metric exporters.

> `Parameters:stripe-webhook-secret` is **not** set locally. The Stripe CLI container runs
> `stripe listen` and supplies a fresh signing secret each session. It is a publish/Azure-only
> parameter, sourced from `.secrets.env` — see `docs/azure-deployment.md`.

### Azure deployment settings

`just deploy` reads deployment inputs from `.secrets.env`, not the local Aspire secret store. The
following values are required for the standard Azure deployment:

| Group | Required values | Default or exception |
|---|---|---|
| Azure target | `Azure__SubscriptionId`, `Azure__ResourceGroup`, `Azure__Location` | `Azure__TenantId` is optional but, when supplied, must match the signed-in Azure CLI tenant. |
| Application | `Parameters__openai_api_key`, `Parameters__redis_password`, `Parameters__stripe_api_key`, `Parameters__stripe_public_key`, `Parameters__temporal_address`, `Parameters__temporal_namespace`, `Parameters__temporal_api_key` | `Parameters__stripe_webhook_secret` is automation-owned; do not supply it. |
| Trace export | `ARIZE_OTLP_ENDPOINT`, `ARIZE_API_KEY`, `ARIZE_SPACE_ID` | Required by the published default, `Trace:Destination=Ax`. Choose `Trace__Destination=Aspire` to use the managed Aspire dashboard, or `Phoenix` to deploy an internal, ephemeral Phoenix instance for this demo; either selection omits AX credentials. |

`Trace:AiOnly` defaults to `true`; `Telemetry:CaptureAiContent` defaults to `false`. The optional
`just deploy --capture` switch sets capture to `true` for that invocation. The complete setup,
including Key Vault permissions for Stripe automation, is in [Azure deployment](docs/azure-deployment.md).

Verify your secrets are stored:

```bash
aspire secret list
```

To retrieve a single secret:

```bash
aspire secret get Parameters:openai-api-key
```

The AppHost injects these values into the appropriate projects.

The Web app uses one committed sample telemetry identity key in every environment, so no telemetry
secret setup is required. It prevents raw identifiers from appearing directly in traces and keeps
demo trace grouping stable, but it is public and is not production-grade pseudonymization. Do not
reuse this design for an application that handles real customers or production data.

## Running Locally

### Prerequisites Check

Before running, ensure:

1. **Docker Desktop is running** — Required for Redis, SQL Server, and Temporal containers

### Build & Run

Build the solution:

```bash
dotnet build
```

Run the Aspire AppHost with the default Phoenix backend and content capture disabled:

```bash
just run
```

Opening Aspire dashboard should show:

- **Aspire Dashboard** — resource health, logs, traces, and metrics
- **Shop Frontend** — the Blazor Web app storefront
- **Products API** — REST endpoints with Scalar docs at `/scalar`
- **Redis Insight** — cache inspection dashboard
- **DbGate** — SQL Server browser
- **Stripe CLI** — local webhook forwarding
- **Temporal Server** — local dev server
- **Arize Phoenix** — local OpenInference trace analysis; logs and metrics stay in the Aspire dashboard

Use `just run-phoenix`, `just run-ax`, and their `--capture` variants as described in
[Telemetry destinations and content security](#telemetry-destinations-and-content-security).

To stop, press `Ctrl+C` in the terminal.

## Key Features

- **Hybrid Search** — Full-text search (SQL Server FTS) combined with vector similarity (OpenAI embeddings), merged via Reciprocal Rank Fusion
- **Chat Assistant (LA)** — Package-backed durable agent using `TemporalCommunity.Extensions.AI` 0.14.2 and `Microsoft.Extensions.AI`, with every model step and tool invocation recorded as a separate Temporal activity; iteration-limited and provider-incomplete turns discard unapplied tool protocol before the next turn
- **Stripe Web elements** — Server-side Checkout Sessions with the Payment and Address elements integrated
- **FusionCache + Redis** — Multi-layer caching with tag-based invalidation and Redis backplane for cross-instance sync
- **Temporal Workflows** — Durable database setup, Stripe order processing, customer onboarding, and long-lived chat sessions

## Temporal Workflows

The application uses Temporal for durable, long-lived operations:

| Workflow | Purpose | Key Features |
|----------|---------|--------------|
| **WithLove.GiftShopChatWorkflow** | Durable chat session per user | Typed sequential tool state; separate model/tool activities; 24-hour workflow-run lifetime; Update/Query/Signal session API; continue-as-new history bounds |
| **DatabaseSetupWorkflow** | Schema initialization on app startup | Runs full-text search index creation, vector column setup, and initial data seeding; executes once per deployment |
| **StripeCheckoutOrderWorkflow** | Order processing pipeline | Coordinates Stripe Checkout Session creation, webhook verification, and order fulfillment with retry logic |
| **CustomerOnboardingWorkflow** | New customer registration flow | Creates Stripe customer record and links to user account; ensures customer data is synced with payment processor |

**Access Temporal UI**: The Aspire dashboard provides a **Temporal UI** link showing all workflows, executions, task queues, and event histories.

See [Durable AI chat architecture](docs/temporal-ai-chat.md) for registration, state, retries,
payload disclosure, observability, and troubleshooting details.

See [Telemetry architecture](docs/telemetry.md) for the MEAI/OpenInference span model, exporter
routing, trace-identity guarantee, and the local-versus-deployment behavior.

## Tests

```bash
just test-unit              # solution-wide unit tests, including replay regression
just test-chat-integration  # scripted model + fake HTTP + local Temporal; no OpenAI or Docker
just test-integration       # all integration tests; Products API tests require Docker
```

## Azure Deployment

The application deploys to Azure Container Apps via the Aspire CLI.

### Additional Prerequisites

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — authenticated with `az login`
- [Temporal CLI](https://docs.temporal.io/cli) — for workflow management during teardown
- [just](https://just.systems) — task runner (`brew install just` on macOS)
- [`jq`](https://jqlang.github.io/jq/) — the deploy and destroy recipes parse Azure JSON with it
- [`tcld`](https://docs.temporal.io/cloud/tcld) — Temporal **Cloud** CLI, used once to register search
  attributes. This is a different binary from the Temporal CLI above.
- A [Temporal Cloud](https://cloud.temporal.io) account — the app uses Temporal Cloud in production (not the local container)

### Setup

Copy the secrets template and fill in your values:

```bash
cp .secrets.env.example .secrets.env
# Edit .secrets.env — Azure subscription details, API keys, Temporal Cloud credentials
```

### Deploy

```bash
just deploy          # deploy to azureprod (incremental — reuses cached infra state)
just deploy-clean    # deploy with fresh state (use after changing location or resource group)
```

### Destroy

```bash
just destroy         # tear down all Azure resources and wait for full deletion
```

See [docs/azure-deployment.md](docs/azure-deployment.md) for detailed configuration, environment variables, and troubleshooting.

## License

This project is licensed under the MIT License. See `LICENSE` for details.
