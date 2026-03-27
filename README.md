# WithLove Gift Shop

<img src="docs/image.png" alt="WithLove Gift Shop" width="70%" />

WithLove is a sample e-commerce applicaiton that show how AI can be integrated into a web application. It includes a curated gift shop with hybrid search (full-text + vector), and an AI-powered chat shopping assistant. 
The sample also uses OpenAI models for inference and embedding generation.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Redis, SQL Server, and Temporal containers)
- [Stripe CLI](https://github.com/stripe/stripe-cli) — for local webhook forwarding (`brew install stripe/stripe-cli/stripe` on macOS)
- **OpenAI API key** — used by the chat assistant and embedding generation
- **Stripe API keys** (test mode) — used for checkout

## Configuration

API keys are defined once in the AppHost using [Aspire parameters](https://learn.microsoft.com/dotnet/aspire/fundamentals/external-parameters) and automatically injected into each project as environment variables. No need to duplicate keys across project appsettings files.

Set up secrets using the Aspire CLI (Aspire 13.2+). Run from the repo root — Aspire auto-discovers the AppHost:

```bash
aspire secret set Parameters:openai-api-key "<your-openai-key>"
aspire secret set Parameters:stripe-api-key "<your-stripe-secret-key>"
aspire secret set Parameters:stripe-webhook-secret "<your-stripe-webhook-secret>"
aspire secret set Parameters:stripe-public-key "<your-stripe-public-key>"
```

Verify your secrets are stored:

```bash
aspire secret list
```

To retrieve a single secret:

```bash
aspire secret get Parameters:openai-api-key
```

The AppHost injects these into the appropriate projects:

## Running the Application

### Prerequisites Check

Before running, ensure:

1. **Docker Desktop is running** — Required for Redis, SQL Server, and Temporal containers
2. **Stripe CLI is running and forwarding webhooks** — For local Stripe webhook testing:

```bash
stripe listen --forward-to https://localhost:7260/stripe/webhook
```

This listens for Stripe webhook events and forwards them to your local web app. Keep this terminal running while developing.

### Build & Run

Build the solution:

```bash
dotnet build WithLoveShop.slnx
```

Run the Aspire AppHost

```bash
aspire run
```

The Aspire dashboard opens automatically and shows:

- **Aspire Dashboard** — resource health, logs, traces, and metrics
- **Shop Frontend** — the Blazor Web app storefront
- **Products API** — REST endpoints with Scalar docs at `/scalar`
- **Redis Insight** — cache inspection dashboard
- **DbGate** — SQL Server browser
- **Temporal UI** — workflow visibility

To stop, press `Ctrl+C` in the terminal.

## Key Features

- **Hybrid Search** — Full-text search (SQL Server FTS) combined with vector similarity (OpenAI embeddings), merged via Reciprocal Rank Fusion
- **Chat Assistant (LA)** — Temporal-backed conversational shopping assistant using `Microsoft.Extensions.AI` with tool calling for product search, cart management, and recommendations
- **Stripe Web elements** — Server-side Checkout Sessions with the Payment and Address elements integrated
- **FusionCache + Redis** — Multi-layer caching with tag-based invalidation and Redis backplane for cross-instance sync
- **Temporal Workflows** — Durable database setup, Stripe order processing, customer onboarding, and long-lived chat sessions

## Temporal Workflows

The application uses Temporal for durable, long-lived operations:

| Workflow | Purpose | Key Features |
|----------|---------|--------------|
| **ChatAgentWorkflow** | Long-lived chat session per user | 24h idle timeout; resumable via `IdConflictPolicy.UseExisting`; Update/Query/Signal pattern for async message handling |
| **DatabaseSetupWorkflow** | Schema initialization on app startup | Runs full-text search index creation, vector column setup, and initial data seeding; executes once per deployment |
| **StripeCheckoutOrderWorkflow** | Order processing pipeline | Coordinates Stripe Checkout Session creation, webhook verification, and order fulfillment with retry logic |
| **CustomerOnboardingWorkflow** | New customer registration flow | Creates Stripe customer record and links to user account; ensures customer data is synced with payment processor |

**Access Temporal UI**: The Aspire dashboard provides a **Temporal UI** link showing all workflows, executions, task queues, and event histories.

## License

This project is licensed under the MIT License. See `LICENSE` for details.
