## Project Overview

WithLove Gift Shop is a .NET 10 distributed application built with .NET Aspire and Temporal for workflow orchestration. The frontend is a Blazor Web App with Interactive Server rendering.

## Configuration Management

API keys are centralized in the Aspire AppHost using **parameters** — a single source of truth that injects values into all consuming projects via environment variables.

**Setting up secrets** (stored in AppHost user secrets, never committed; managed via Aspire CLI):

Run from the repository root; Aspire auto-discovers the AppHost:

```bash
# Define all parameters once
aspire secret set "Parameters:openai-api-key" "<your-key>"
aspire secret set "Parameters:stripe-api-key" "<your-key>"
aspire secret set "Parameters:stripe-public-key" "<your-key>"
```

**Do not set `Parameters:stripe-webhook-secret` for local development.** It is a publish/Azure-only
parameter: `AddParameters` declares it only when `builder.ExecutionContext.IsPublishMode` is true, so
a local `aspire run` never asks for it. Locally the Stripe CLI container (`AddStripeCliContainer` in
`ConfigureLocalDependencies`) runs `stripe listen`, which mints a fresh signing secret per session and
supplies it to Web and the WorkflowServer as `Stripe__Default__WebhookSecret` via `WithReference`.
A stored local copy would be a credential no local code path reads — and therefore one whose
corruption first shows up in Azure as a silent Stripe signature mismatch.

For publish/deploy, the value comes from `Parameters__stripe_webhook_secret` in `.secrets.env` (see
`.secrets.env.example` and `just deploy`). The `validate-stripe-webhook-secret` pipeline step runs
after `process-parameters` and before any publish work: a value that is empty, whitespace-padded,
quote-wrapped (straight *or* smart quotes) or missing the `whsec_` prefix fails `aspire publish` /
`aspire deploy` by name, before any Bicep is written or any Key Vault secret is created. The error
names the parameter and the expected shape but never echoes the value.

**Useful Aspire CLI commands:**

| Command | Purpose |
|---------|---------|
| `aspire secret set <key> <value>` | Add or update a secret |
| `aspire secret get <key>` | Read a single secret |
| `aspire secret list` | List all secrets (table view) |
| `aspire secret delete <key>` | Remove a secret |
| `aspire secret path` | Show path to the secrets JSON file |

**Consuming in AppHost.cs**:
```csharp
var openaiKey = builder.AddParameter("openai-api-key", secret: true);
var stripeApiKey = builder.AddParameter("stripe-api-key", secret: true);
// ...inject via .WithEnvironment() to projects
productsApi.WithEnvironment("OPENAI_API_KEY", openaiKey);
```

Projects read from environment variables — no appsettings duplication needed.

## Build and Run Commands

```bash
# Build the entire solution
dotnet build WithLoveShop.slnx

# Run the full application via the Aspire AppHost (orchestrates all services)
aspire start --apphost src/WithLove.AppHost/WithLove.AppHost.csproj

# Run individual projects
dotnet run --project src/WithLove.Web
dotnet run --project src/WithLove.WorkflowServer

# Build a single project
dotnet build src/WithLove.WorkflowServer
```

## Generated OpenInference Source

`src/WithLove.OpenInference/OpenInferenceAttributes.g.cs` is generated source, but it is an
intentional checked-in build input and must not be git-ignored. The normal application build does
not run the generator.

Regenerate the file whenever
`src/WithLove.OpenInference/Conventions/openinference-conventions.json` changes, or when the
generator's required attribute list or output formatting changes:

```bash
dotnet run --project tools/WithLove.OpenInference.Generator -- generate
```

Review and commit the generated diff with its manifest or generator change. Before completing the
change, run the non-writing stale-output check:

```bash
dotnet run --project tools/WithLove.OpenInference.Generator -- verify
```

`verify` must succeed; if it reports stale output, run `generate`, review the result, and rerun
`verify`.

## Testing

Test projects: `WithLove.Web.Tests`, `WithLove.Workflows.Tests`, `WithLove.ProductsAPI.Tests`,
`WithLove.Telemetry.Tests`, and `WithLove.StripeWebhooks.Tests`.

**Test counts are deliberately not recorded here.** They change with every commit that adds a test,
and a stale count in documentation is worse than no count — it gets cited, trusted, and repeated.
Ask the tooling instead:

```bash
# Total, and the CI/local split
dotnet test WithLoveShop.slnx --no-build --list-tests
dotnet test WithLoveShop.slnx --no-build --list-tests --filter "RequiresSecrets=true"   # local-only
dotnet test WithLoveShop.slnx --no-build --list-tests --filter "RequiresSecrets!=true"  # what CI runs
```

Stack: xUnit + FakeItEasy + FluentAssertions. Prefer a real in-memory `ProductsDbContext` or a real
`FusionCache` instance over a mock — the cache and EF query layers are self-contained, and faking
them tests the fake.

### The tests CI cannot run

Several ProductsAPI suites boot the Aspire AppHost. ProductsAPI's startup constructs
`new EmbeddingClient("text-embedding-3-small", openaiKey)` eagerly, and that throws on the
empty-string fallback — so without a real key the host never becomes ready and every test in those
classes fails at fixture initialization rather than on an assertion. They are marked
`[Trait(TestTraits.RequiresSecrets, TestTraits.True)]` at class level:

`DatabaseVerificationTests` · `HealthCheckTests` · `PaginationTests` ·
`ResponseHeaderTests` · `SearchTests`

To run them, set the key once (see the Configuration section) and run the ProductsAPI project on its
own:

```bash
aspire secret set "Parameters:openai-api-key" "<your-key>"
dotnet test tests/WithLove.ProductsAPI.Tests/WithLove.ProductsAPI.Tests.csproj
```

**A fully green run of the whole suite is achievable only on a developer machine with that key set.
CI cannot make that claim and should not be described as if it does** — `.github/workflows/build.yml` excludes the
trait with `--filter "...&RequiresSecrets!=true"` and reports the excluded count in the job summary.
The trait name is a literal contract between `TestTraits.cs` and that workflow; renaming either side
silently re-enables the secret-dependent tests, which will then fail the build.

Note that `SearchCacheInvalidationTests` is **not** in this set despite living under `Integration/`.
It fakes the embedding generator and never starts the AppHost, so it runs in CI in ~250 ms.

### Run tests project by project, not solution-wide

`dotnet test WithLoveShop.slnx` runs the test projects in parallel. The AppHost suite, the
Temporal dev server and the SQL Server container then compete for the same machine, and the AppHost
fixture times out — a red run that says nothing about the code. Run one project at a time:

```bash
dotnet test tests/WithLove.Web.Tests/WithLove.Web.Tests.csproj
dotnet test tests/WithLove.Workflows.Tests/WithLove.Workflows.Tests.csproj
dotnet test tests/WithLove.ProductsAPI.Tests/WithLove.ProductsAPI.Tests.csproj
dotnet test tests/WithLove.Telemetry.Tests/WithLove.Telemetry.Tests.csproj
dotnet test tests/WithLove.StripeWebhooks.Tests/WithLove.StripeWebhooks.Tests.csproj
```

CI is unaffected: it partitions by `Category=Unit` / `Category!=Unit`, so the AppHost suite is
already excluded from both steps.

### What is covered

- **Durable chat tools** — failure classification (404 answers, 5xx/408/429 retry, other 4xx fail
  fast), tool/declaration schema agreement, cart and navigation turn state, session ownership
- **Temporal wiring** — data converter reconciliation, mixed AI/non-AI workflow serialization,
  workflow replay against a recorded history
- **Prompt safety** — prompt-injection sanitization, and the fields `UserContext` is allowed to
  carry into append-only workflow history
- **Cart** — quantity bounds and saturating arithmetic in the production cart service,
  anonymous-cart merge, persistence
- **Products API** — ETags and conditional requests, RFC 9457 Problem Details, API version
  validation, error and response-header middleware, caching, hybrid-search RRF merge, pagination

Test organization (per project):
- `Unit/` — Middleware, filters, utilities, services
- `Integration/` — Anything requiring a real Temporal environment, AppHost or database
- `Features/` — Health checks, pagination, search, response headers
- `Replay/` — Recorded workflow histories replayed to catch non-deterministic changes
- `Traits/` — Shared xUnit trait constants, including the CI exclusion contract

## Architecture

This is a .NET Aspire distributed application using the XML-based `.slnx` solution format. `Directory.Build.props` sets `net10.0` target framework and `latest` C# language version for all projects.

### Projects

- **WithLove.AppHost** — Aspire orchestrator. Entry point for running the full distributed application locally. Launches and manages all other services.
- **Arize.Aspire.Hosting** — Local Aspire hosting integration for an Arize Phoenix resource, including endpoints, health checks, and reference wiring.
- **WithLove.ServiceDefaults** — Shared Aspire service defaults library. Configures OpenTelemetry (tracing, metrics, logging), health checks (`/health`, `/alive`), HTTP resilience, and service discovery. Referenced by service projects.
- **WithLove.Data** — Shared data access layer (class library). Contains EF Core `DbContext` and domain models (`Product`, `Category`) used across multiple services. Enables code reuse and consistent data access patterns across the application.
- **WithLove.OpenInference** — Local, non-packable OpenInference conventions library used to apply consistent semantic attributes to application-owned spans.
- **WithLove.Web** — Blazor Web App host. Serves the storefront with Static SSR plus Interactive Server render modes, hosts the Blazor components, shared web models/services, and the chat/Stripe/loyalty Temporal client code. There is **no** separate `.Client` WebAssembly project.
- **WithLove.Workflows** — Temporal workflow and activity class library. Contains the durable chat workflow (`GiftShopChatWorkflow`), the tool catalog, and the Stripe/loyalty/onboarding/database workflows. Referenced by `WithLove.WorkflowServer` (implementations) and `WithLove.Web` (declarations and client-side contracts).
- **WithLove.WorkflowServer** — Temporal worker host. Connects to Temporal (default `localhost:7233`) using `ClientEnvConfig.LoadClientConnectOptions()` for configuration. Registers a hosted worker on the `with-love-tasks` task queue. Also exposes an OpenAPI endpoint in development.
- **WithLove.ProductsAPI** — ASP.NET Core Web API service. Implements REST endpoints for product and category management. References `WithLove.Data` for EF Core integration and `WithLove.ServiceDefaults` for Aspire telemetry and health checks.
- **WithLove.OpenInference.Generator** — Development tool that regenerates the checked-in OpenInference attribute vocabulary.
- **WithLove.Telemetry.Verifier** — Runtime verification tool for unified chat trace shape and privacy invariants.

The projects under `tests/` cover Web, workflows, ProductsAPI, telemetry, and the Stripe webhook
provisioning tool.

### Key Dependencies

- **Aspire** — Distributed application orchestration
- **Temporal SDK** — Workflow orchestration via `Temporalio.Extensions.Hosting`; the WorkflowServer reads Temporal connection config from environment variables (`ClientEnvConfig`)
- **Blazor** — UI with Interactive Server rendering and static SSR for pages that opt out of interactive routing
- **OpenTelemetry** — Observability configured in ServiceDefaults

### Prerequisites

- .NET 10 SDK
- A Temporal server running locally (default `localhost:7233`) or configured via Temporal environment variables

## Design System & Styling

**CSS Framework:** Tailwind CSS via CDN with inline config in `App.razor`. The inline `tailwind.config` block defines all custom color tokens, font families, and border-radius values. This is a temporary rapid-prototyping approach; for production, migrate to the standalone Tailwind CLI with MSBuild integration.

**Color Palette:**
- `primary` (#DFA8A8) — Dusty rose for buttons, accents, active states
- `primary-dark` (#C58B8B) — Deeper rose for hover states, emphasis
- `stone-50` to `stone-900` — Neutral scale for backgrounds, text, borders
- `earth-brown`, `sage-green`, `clay` — Accent colors

**Typography:**
- **Cinzel** (serif) — Display/headings (h1-h6, product names, section titles)
- **Quicksand** (sans-serif) — Body text, labels, UI elements
- **Dancing Script** (cursive) — Personal notes, gift messages
- **Material Symbols Outlined** — UI icons throughout

## Component File Structure

Components are organized by type in `src/WithLove.Web/Components/`:
- **`Layout/`** — App shell: MainLayout, SiteHeader, SiteFooter
- **`Shared/`** — Reusable UI components: product cards, CategoryCircle, TrustBadge, QuantitySelector, Breadcrumb, ChatFab, and chat message rendering.
- **`Pages/`** — Routable pages: Home, CollectionPage, ProductDetail, Cart, Checkout (and their supporting sub-components)

**Web models and services** live inside the `WithLove.Web` project itself — there is no `WithLove.Shared` project:
- `src/WithLove.Web/Models/` (namespace `WithLove.Web.Models`) — Product, Category, CartItem, GiftEnhancement, CheckoutModel, BreadcrumbItem, AccountModels, etc.
- `src/WithLove.Web/Services/` (namespace `WithLove.Web.Services`) — IProductService, ICartService, FusionCacheCartService, ChatService, ILoyaltyService, etc.
- Cross-process contracts shared with the worker (chat request/turn state, workflow inputs) live in `src/WithLove.Workflows/`, not in a `Shared` project.

## Blazor Render Modes

The application uses **InteractiveServer** as its default render mode. Identity pages that must write response headers opt out through `ExcludeFromInteractiveRouting` and use static SSR.

**Render mode decisions:**
- **InteractiveServer:** Normal application pages and components, including product interactions, cart, checkout, and chat
- **Static SSR:** Login, registration, and other pages that explicitly exclude interactive routing

There is no WebAssembly client project. Interactive Server components share scoped services within a SignalR circuit.

## State Management

**Cart state** is managed by `ICartService` registered as **scoped** (one instance per SignalR circuit):
- `FusionCacheCartService` keeps the circuit-local snapshot and persists cart state through FusionCache
- CartBadge, Cart, and Checkout pages subscribe to `OnChange` for UI updates
- Gift enhancements are tracked as part of cart state
- Scoped services ensure isolation between user circuits while allowing shared access within a circuit

## Data Models & Services

**Core Models:**
- `Product` — ProductId, Name, Description, Price, ImageUrl, CategoryId, CategoryName, Materials[], Features[]
- `Category` — CategoryId, Name, Description
- `CartItem` — ProductId, Quantity, Product (navigation), SelectedEnhancements[]
- `GiftEnhancement` — EnhancementId, Name, Price, IconClass
- `CheckoutModel` — RecipientName, RecipientEmail, PersonalNote, GiftMessage, etc.

**Product Service** (`IProductService`):
- `GetProductAsync()`, `GetCategoriesAsync()`, `GetProductsByCategoryAsync()`
- `SearchProductsAsync(query, cancellationToken)` — Hybrid search via HTTP to ProductsAPI
- Currently uses `ProductApiService` (HTTP client with Aspire service discovery)

**Cart Service** (`ICartService`):
- Async mutations: `AddItemAsync()`, `RemoveItemAsync()`, `UpdateQuantityAsync()`, `ClearAsync()`
- Synchronous reads: `Items`, `ItemCount`, `Subtotal`, `Total`
- Backed by `FusionCacheCartService` — hybrid in-memory snapshot + Redis persistence (L1+L2 caching)
- Scoped per SignalR circuit; OnChange event fires for UI updates
- 30-day TTL for abandoned carts; cart data syncs across browser windows via Redis

## Hybrid Search (FTS + Vector)

Search merges two ranking strategies via **Reciprocal Rank Fusion (RRF)**:

1. **Full-Text Search (FTS)** — SQL Server `FREETEXT` on product Name/Description. Falls back to `LIKE` if FTS unavailable.
2. **Vector Search** — OpenAI embeddings with cosine distance similarity. Filtered to `maxCosineDistance = 0.8f` (0=identical, 2=opposite) to exclude irrelevant results.

**RRF Formula**: `score = 1/(k+rank_fts) + 1/(k+rank_vector)` where k=60. Products appearing in both rankings get boosted scores.

Products matching neither strategy return empty results (not "10 closest neighbors regardless of relevance").

## Chat Assistant (LA)

**Temporal Workflow** (`WithLove.GiftShopChatWorkflow`):
- Package-backed durable session per user with a 24-hour workflow-run lifetime
- Update: `SendMessageAsync(DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>)` — processes one durable model/tool turn. A `[WorkflowUpdateValidator]` rejects malformed identity, message, turn-state, or dispatch data before any activity is scheduled.
- Query: `GetHistory()` — returns the **raw** `IReadOnlyList<DurableSessionEntry>` durable session history, **including system instructions and the full tool-call protocol**. It is not filtered or redacted. Display projection happens client-side in Web via `GiftShopChatResponseProjector.ProjectHistory`, and that projection is a rendering convenience — **not** a data-removal boundary. See `docs/temporal-ai-chat.md` for what ends up in Temporal payloads.
- Signal: `RequestShutdownAsync()` — graceful session shutdown
- Every model step runs as `GetChatStep`; every tool invocation runs as a separate `InvokeFunction` activity
- Tool iteration is explicitly capped at 40; incomplete turns apply no cart/navigation commands

**Durable AI tools** (`GiftShopChatToolService` + `GiftShopChatToolCatalog`):
- System prompt (`GiftShopChatPrompt`) defines LA's voice: warm and conversational, but
  deliberately understated rather than exclamatory. Its ground rules — never state a product
  name, price or detail not read from a tool result, and never invent an ID — override the
  stylistic guidance. It also tells LA to match the occasion before the product (sympathy and
  apology get a plain, calm register), how to read each tool's failure strings, and what it
  cannot answer at all (shipping, returns, order status, stock, discount codes).
- 13 model-visible tools, frozen in `GiftShopChatToolCatalog.CreateDeclarations()` (that method is the single source of truth — update this list when it changes):
  - Catalog reads: `search_products`, `get_product_details`, `get_categories`, `browse_category`
  - Cart: `add_to_cart`, `remove_from_cart`, `view_cart`, `clear_cart`
  - Navigation: `navigate_to_product`, `navigate_to_collection`, `navigate_to_cart`, `navigate_to_checkout`
  - Loyalty: `view_loyalty_points`
- Loyalty tier thresholds and the redemption divisor are interpolated into the system prompt
  from `LoyaltyContracts` — do not restate those numbers in prose, here or in the prompt
- Tool results are concise summaries, not raw JSON, to reduce token usage and improve accuracy.
  The model-facing summary carries no image URL — product imagery is resolved client-side from
  the catalog. (An `ImageUrl` still travels on the typed `CartAction`, which the model never
  sees but which is serialized into workflow history.)
- `navigate_to_product` and `navigate_to_collection` resolve the ID server-side and return an
  error string for an unknown one, so the model cannot navigate the customer to a dead route
- Typed turn state accumulates cart/navigation commands sequentially and Web applies them only after a final response
- `AddDurableAI` configures the shared worker's data converter, and `AddGiftShopChatWorkflowClient()` configures Web's DI `ITemporalClient` as a side effect — the same client `StripeEventHandler` and `TemporalLoyaltyService` use. Manually created Temporal clients must use `DurableAIDataConverter.Instance`. The converter is applied only while `DataConverter` is still `DataConverter.Default`; a custom converter or `PayloadCodec` causes a **silent, log-only skip**. See `docs/temporal-ai-chat.md`.

**Blazor Integration** (`ChatService`):
- Scoped service bridges Blazor UI ↔ Temporal workflow
- Auth-based session IDs: `giftshop-chat-{userId}`, or `giftshop-chat-anon-{chatId}` where
  `chatId` comes from the `wl-chat-id` cookie — anonymous sessions are stable across refreshes
  and tabs, not minted per circuit. The workflow starts on the first message, not on panel open.
- Builds a cart snapshot for each turn so durable tools can evaluate current cart state
- Applies cart actions locally only when the workflow returns `FinalResponse`

**UI** (`ChatFab.razor` + `ChatMessageContent.razor`):
- FAB pill button (unchanged text "Chat with Love") toggles chat panel
- Instant message display: user message + thinking indicator shown immediately before Temporal round-trip
- Rich message rendering: catalog-backed product cards plus bold/italic, newlines, and inline code.
  Model-authored image URLs are discarded; reusable product templates show a neutral placeholder
  when a catalog image is missing or fails in the browser.
- Quick action buttons for common queries

## Terminology: Category vs. Collection

**Standardized usage:**
- **`Category`** = Internal/technical term used throughout C# code
  - Model: `WithLove.Web.Models.Category` (web/UI) and `WithLove.Data.Models.Category` (EF Core entity)
  - Service interface methods: `GetCategoryAsync()`, `GetCategoriesAsync()`, `GetProductsByCategoryAsync()`
  - Component names: `CategoryCircle.razor`
  - Variables: `category`, `categories`
  - Product properties: `CategoryId`, `CategoryName`, `SubCategory`

- **"collection"** = User-facing term used in UI, routes, and URLs
  - Routes: `/collections/{CategoryId}`
  - Breadcrumb labels: "Collection" fallback text
  - Page title: CollectionPage.razor
  - Comments: Refer to "collection filtering" in UI components

This separation keeps the codebase technically consistent while presenting a user-friendly interface.

## Data Layer Architecture

The **WithLove.Data** project is a shared class library that centralizes data access for the entire application:

- **Location:** `src/WithLove.Data/`
- **Purpose:** Provides Entity Framework Core DbContext and domain models for reuse across multiple services
- **Models:** `Product`, `Category` — shared domain entities with validation attributes and concurrency control (in `Models/` subdirectory)
- **DbContext:** `ProductsDbContext` — configures entities, indexes, and automatic timestamp management (in `Data/` subdirectory)
- **Migrations:** `Migrations/` directory with schema change history; run with startup project: `dotnet ef database update --project src/WithLove.Data --startup-project src/WithLove.ProductsAPI`
- **Key Features:**
  - Optimistic concurrency control via SQL Server `rowVersion` (timestamp)
  - Soft delete pattern via `IsEnabled` boolean flag
  - Automatic UTC timestamp management (`AddedDate`, `UpdatedDate`)
  - Performance indexes on frequently-queried columns (`IsEnabled`, `AddedDate`, `CategoryId`, `SKU`)
  - EF Core with SQL Server provider
  - **Namespace pattern:** DbContext and migrations use `WithLove.Data` namespace (not ProductsAPI)

**Why separate?** Moving the data layer to a shared project enables multiple services (WorkflowServer, future microservices, Worker processes) to use the same models and DbContext without code duplication. Currently only ProductsAPI consumes it, but this pattern scales as the application grows.

## Products API Architecture

### Endpoints

The Products API provides **read-only access** to products and categories:

**Product Endpoints:**
- `GET /api/products` — List all products with pagination, sorting, filtering
- `GET /api/products/{id}` — Get a single product by ID
- `GET /api/products/search` — Search products by name (case-insensitive substring matching)
- `GET /api/products/category/{categoryId}` — Get products in a specific category

**Category Endpoints:**
- `GET /api/categories` — List all categories with pagination
- `GET /api/categories/{id}` — Get a single category by ID

All endpoints support:
- **Conditional requests** via `If-None-Match` (ETag) and `If-Modified-Since` headers for caching optimization
- **API version validation** via `X-WITHLOVE-API-VERSION` header (format: YYYY-MM-DD)
- **RFC 9457 Problem Details** error responses for all error cases

**Note:** Product management (create, update, delete) is handled by backend Temporal workflows, not via the REST API.

### Error Handling Strategy

The Products API uses a **custom `ErrorHandlingMiddleware`** (not ASP.NET Core's built-in `UseExceptionHandler`) for the following reasons:

1. **RFC 9457 Problem Details Compliance** — Our custom `ProblemDetailsResponse` model enforces a specific Problem Details format across all error responses. Custom middleware gives direct control over serialization, ensuring consistency without additional endpoint routing.

2. **Direct Integration with ProblemDetailsResults** — The middleware calls our centralized `ProblemDetailsResults` factory class, ensuring all exceptions are converted to properly formatted Problem Details with type URIs, titles, and status codes.

3. **JSON Serialization Control** — We configure camelCase property names and null-value omission at the middleware level, ensuring consistent response formatting across all error paths.

4. **Development-Only Error Details** — The middleware checks `IHostEnvironment.IsDevelopment()` and conditionally includes detailed error messages and stack traces only in development, preventing information leakage in production.

5. **Structured Logging** — Direct access to `ILogger<ErrorHandlingMiddleware>` allows structured logging of exceptions with context (exception type, message) without the additional complexity of a separate error endpoint.

**Alternative:** `UseExceptionHandler` could be used with a dedicated error endpoint, but would require additional routing logic and less direct control over the response format. The custom middleware approach is simpler and more explicit for our use case.

**Middleware Registration:** The middleware is registered early in the pipeline in `Program.cs` (after service registration, before route mapping) to catch all unhandled exceptions across the entire application.

### Conditional Request Headers

The Products API implements HTTP conditional requests for caching optimization:

- **If-None-Match (ETag):** Returns 304 Not Modified if client's ETag matches current resource version (via `ETagGenerator.VerifyETag()`)
- **If-Modified-Since (Last-Modified):** Returns 304 Not Modified if resource hasn't changed since client's date
- Both headers set the `Last-Modified` response header using `ToString("R")` (RFC 1123 format)
- Both GET endpoints (`GetProductById`, `GetCategoryById`) support these headers

This reduces bandwidth and improves client-side caching behavior.

### Minimal API Endpoint Pattern: TypedResults

All Minimal API endpoints use **TypedResults methods** instead of Results methods for automatic OpenAPI documentation and type safety:

**Pattern:**
```csharp
// Imports at top of ProductEndpoints.cs and CategoryEndpoints.cs
using Microsoft.AspNetCore.Http.HttpResults;
using static Microsoft.AspNetCore.Http.TypedResults;

// Method signature - IResult return type for flexibility
private static async Task<IResult> GetProductById(...) { ... }

// Success responses use TypedResults methods (strongly typed)
return Ok(productResponse);              // TypedResults.Ok<T>()
return Created(uri, productResponse);    // TypedResults.Created<T>()
return NoContent();                      // TypedResults.NoContent()
return StatusCode(304);                  // TypedResults.StatusCode()

// Error responses use ProblemDetailsResults helpers (RFC 9457)
return ProblemDetailsResults.NotFound(...);
return ProblemDetailsResults.BadRequest(...);
return ProblemDetailsResults.Conflict(...);
return ProblemDetailsResults.PreconditionFailed(...);
```

**Benefits:**
- ✅ **Automatic OpenAPI Documentation** — Response types inferred from actual return statements; no `.Produces()` boilerplate needed
- ✅ **Type Safety** — Success responses strongly typed (Ok<T>, Created<T>, NoContent)
- ✅ **Clean Endpoint Metadata** — Minimal API configuration focuses on routing and naming only
- ✅ **Structured Error Responses** — All errors use RFC 9457 Problem Details format via `ProblemDetailsResults` helpers
- ✅ **Better IntelliSense** — IDE provides completion and validation for response types

**Endpoint Metadata Example:**
```csharp
// No .Produces() calls needed - OpenAPI schema auto-generated
group.MapGet("/{id}", GetProductById)
    .WithName("GetProductById")
    .WithSummary("Get a single product by ID")
    .WithTags("Products");
```

**Why `IResult` return type?** Pragmatic choice: TypedResults methods provide type safety for success paths, while `IResult` accommodates error responses from `ProblemDetailsResults` helpers without type mismatches. Alternative `Results<Ok<T>, NotFound<T>, BadRequest<T>, ...>` union types were too verbose and conflicted with existing error helper patterns.

## Git Workflow

**CRITICAL: Never commit or push code unless explicitly asked.**

**Key principles:**
- Use `gh` to create/update issues and PRs, inspect workflow runs, and manage releases
- Use `git` for all local version control operations


- Stage files and show `git status` when done with implementation — do NOT commit automatically
- Wait for explicit request: "commit these changes" or "commit and push"
- Even if implementation is complete and tested, wait for user consent
- This prevents locking in incomplete work and respects user control over git history

**Use CLI tools for Git and GitHub interaction:**

**`git` CLI — Local repository operations:**
```bash
# Stage and commit changes
git add src/file.cs
git commit -m "Fix bug in authentication"

# Push to remote
git push origin feature-branch

# Create and switch branches
git switch -c feature/new-feature
git switch main

# View history
git log --oneline -10
git diff main..feature-branch

# Undo changes
git revert <commit-hash>
git reset --soft HEAD~1  # undo last commit, keep changes staged
```

**`gh` CLI — GitHub-specific operations:**
```bash
# Create a pull request
gh pr create --title "Add new feature" --body "Description of changes"

# View and interact with PRs
gh pr view 42                    # view PR #42
gh pr list                       # list all PRs
gh pr review 42 --approve        # approve a PR
gh pr checks 42                  # check CI status

# Work with issues
gh issue create --title "Bug: login fails" --body "Steps to reproduce..."
gh issue list --state open
gh issue comment 15 --body "Fixed in PR #42"

# View and manage releases
gh release create <tag> --title "<release title>"
gh release list

```
