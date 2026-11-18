# WithLove Gift Shop

.NET 11, Aspire 17, Temporal, and a Blazor Web App using Interactive Server by default. Login and registration opt out of interactive routing and use static SSR. `WithLoveShop.slnx` is the solution; `Directory.Build.props` sets the target framework and C# language version.

## Working conventions

- Never commit or push unless explicitly asked. After implementation, stage the intended files and show `git status`; wait for a separate commit request.
- Use `git` for local version control and `gh` for GitHub operations.
- Use `Category` for the technical model and code; use “collection” in user-facing text and routes.
- Keep interactive cart and checkout components on Interactive Server, where scoped services share state within a SignalR circuit. Avoid introducing WebAssembly without an explicit decision.
- Use the existing Tailwind configuration in `App.razor` and established typography: Cinzel for headings, Quicksand for body text, and Dancing Script for personal notes.

## Build and test

```bash
dotnet build WithLoveShop.slnx
aspire run
```

Tests use xUnit, FakeItEasy, and FluentAssertions. Prefer a real in-memory `ProductsDbContext` or `FusionCache` where practical. Run test projects individually when running integration suites: parallel solution-wide runs can make the AppHost, Temporal, and SQL Server compete for resources.

```bash
dotnet test tests/WithLove.Web.Tests/WithLove.Web.Tests.csproj
dotnet test tests/WithLove.Workflows.Tests/WithLove.Workflows.Tests.csproj
dotnet test tests/WithLove.ProductsAPI.Tests/WithLove.ProductsAPI.Tests.csproj
dotnet test tests/WithLove.StripeWebhooks.Tests/WithLove.StripeWebhooks.Tests.csproj
```

CI excludes `RequiresSecrets=true`: some Products API integration tests start the AppHost and require a real OpenAI key. Keep the trait and CI filter in sync; report excluded tests when describing validation. A full local Products API suite requires the key in Aspire secrets. `SearchCacheInvalidationTests` uses a fake embedding generator and does not require it.

## Configuration and deployment

API keys are Aspire AppHost parameters, supplied to services through environment variables. Keep secrets out of source control and logs. Use Aspire secrets for local development; Azure deployment reads `.secrets.env` and uses `just deploy`.

Do not set `Parameters:stripe-webhook-secret` for local development: the Stripe CLI supplies a fresh secret for each session. For publish, `Parameters__stripe_webhook_secret` is validated before infrastructure is generated. The deploy automation creates or reconciles the Stripe destination and installs a newly issued signing secret; preserve that flow and its recovery checks.

## Architecture and application invariants

- `WithLove.AppHost` orchestrates services; `WithLove.ServiceDefaults` owns shared telemetry, health checks, resilience, and discovery.
- `WithLove.Data` contains the shared EF Core models and `ProductsDbContext`. Products API exposes read-only product and category endpoints; writes are performed by Temporal workflows. Preserve soft deletion, SQL Server row-version concurrency, and UTC timestamps.
- Products API uses `TypedResults` and the existing `ProblemDetailsResults` and error-handling middleware. Preserve its conditional requests and `X-WITHLOVE-API-VERSION` validation.
- `WithLove.Web` contains web models and services; shared workflow contracts live in `WithLove.Workflows`. There is no separate shared or WebAssembly client project.
- `ICartService` is scoped per circuit. Cart changes notify subscribers through `OnChange`; `FusionCacheCartService` uses Redis-backed persistence.
- Search combines SQL full-text and vector results with reciprocal rank fusion; preserve the no-match behavior and vector distance cutoff.

## Durable chat

- `WithLove.GiftShopChatWorkflow` runs model steps and tool calls as separate activities. `GiftShopChatToolCatalog.CreateDeclarations()` is the source of truth for model-visible tools.
- `GetHistory()` returns raw durable session entries, including system instructions and tool-call protocol. The Web display projection is not a redaction or data-removal boundary.
- Turn state accumulates cart and navigation commands; Web applies them only after a final response. Incomplete turns must not apply those commands.
- `AddDurableAI` and `AddGiftShopChatWorkflowClient()` configure the Temporal data converter. Manually created Temporal clients must use `DurableAIDataConverter.Instance`; a custom converter or payload codec can cause automatic configuration to be skipped. See `docs/temporal-ai-chat.md`.
- Chat identity is tied to the authenticated user or persistent anonymous cookie and rotates at login/logout. Preserve session ownership and shutdown behavior.
