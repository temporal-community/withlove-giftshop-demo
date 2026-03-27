## Project Overview

WithLove Gift Shop is a .NET 10 distributed application built with .NET Aspire and Temporal for workflow orchestration. The frontend is a Blazor Web App with WebAssembly (WASM) interactive rendering.

## Configuration Management

API keys are centralized in the Aspire AppHost using **parameters** — a single source of truth that injects values into all consuming projects via environment variables.

**Setting up secrets** (stored in AppHost user secrets, never committed; managed via Aspire CLI):

Run from repo root — Aspire auto-discovers the AppHost (new in Aspire 13.2):

```bash
# Define all parameters once
aspire secret set "Parameters:openai-api-key" "<your-key>"
aspire secret set "Parameters:stripe-api-key" "<your-key>"
aspire secret set "Parameters:stripe-webhook-secret" "<your-key>"
aspire secret set "Parameters:stripe-public-key" "<your-key>"
```

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
dotnet run --project src/WithLove.AppHost

# Run individual projects
dotnet run --project src/WithLove.Web/WithLove.Web
dotnet run --project src/WithLove.WorkflowServer

# Build a single project
dotnet build src/WithLove.WorkflowServer
```

## Testing

Unit and integration tests (109+ tests) cover:
- **ETag Generation & Validation** — ETag generation from row versions, verification with If-None-Match conditional requests
- **Problem Details Responses** — RFC 9457 compliant error responses with proper status codes and type URIs
- **API Version Validation** — X-WITHLOVE-API-VERSION header format validation (YYYY-MM-DD)
- **Error Handling Middleware** — Exception handling with development vs. production error detail control
- **Response Headers Middleware** — Cache-Control and security header injection
- **Product Caching Service** — Product retrieval, search with tag-based cache invalidation, category filtering, pagination
- **Database Operations** — Product soft delete pattern, optimistic concurrency control with row versions
- **Search & Filtering** — Case-insensitive product search, category-based filtering with enabled product filtering

Test organization:
- `Unit/` — Middleware, filters, utilities, services
- `Integration/` — Cache invalidation, database operations
- `Features/` — Health checks, pagination, search, response headers
- `Database/` — Database verification and migrations
- `Cache/` — Cache behavior and tag-based invalidation

## Architecture

This is a .NET Aspire distributed application using the XML-based `.slnx` solution format. `Directory.Build.props` sets `net10.0` target framework and `latest` C# language version for all projects.

### Projects

- **WithLove.AppHost** — Aspire orchestrator (Aspire.AppHost.Sdk 13.1.1). Entry point for running the full distributed application locally. Launches and manages all other services.
- **WithLove.ServiceDefaults** — Shared Aspire service defaults library. Configures OpenTelemetry (tracing, metrics, logging), health checks (`/health`, `/alive`), HTTP resilience, and service discovery. Referenced by service projects.
- **WithLove.Data** — Shared data access layer (class library). Contains EF Core `DbContext` and domain models (`Product`, `Category`) used across multiple services. Enables code reuse and consistent data access patterns across the application.
- **WithLove.Web** — Blazor Server host. Serves the Blazor app with both Interactive Server and Interactive WebAssembly render modes. References the `.Client` project for WASM components.
- **WithLove.Web.Client** — Blazor WebAssembly client project (`Microsoft.NET.Sdk.BlazorWebAssembly`). Contains components that run in the browser via WASM.
- **WithLove.WorkflowServer** — Temporal worker host. Connects to Temporal (default `localhost:7233`) using `ClientEnvConfig.LoadClientConnectOptions()` for configuration. Registers a hosted worker on the `with-love-tasks` task queue. Also exposes an OpenAPI endpoint in development.
- **WithLove.ProductsAPI** — ASP.NET Core Web API service. Implements REST endpoints for product and category management. References `WithLove.Data` for EF Core integration and `WithLove.ServiceDefaults` for Aspire telemetry and health checks.

### Key Dependencies

- **Aspire 13.1.1** — Distributed application orchestration
- **Temporal SDK (Temporalio 1.11.1)** — Workflow orchestration via `Temporalio.Extensions.Hosting`; the WorkflowServer reads Temporal connection config from environment variables (`ClientEnvConfig`)
- **Blazor** — UI with combined Server + WebAssembly interactive rendering
- **OpenTelemetry 1.15.0** — Observability (configured in ServiceDefaults)

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

Components are organized by type in `src/WithLove.Web/WithLove.Web/Components/`:
- **`Layout/`** — App shell: MainLayout, SiteHeader, SiteFooter
- **`Shared/`** — Reusable UI components: ProductCard*, CategoryCircle, TrustBadge, QuantitySelector, Breadcrumb, Pagination, ChatFab, QuizOverlay, etc.
- **`Pages/`** — Routable pages: Home, CollectionPage, ProductDetail, Cart, Checkout (and their supporting sub-components)

**Shared models and services** live in `src/WithLove.Shared/`:
- `Models/` — Product, Category, CartItem, GiftEnhancement, CheckoutModel, OrderSummary, BreadcrumbItem, etc.
- `Services/` — IProductService, ICartService, InMemoryCartService

## Blazor Render Modes

The application uses **InteractiveServer** for interactive components (cart, checkout, product interactions) and **Static SSR** for static content (home, collection pages, layouts). This strategy prioritizes SEO while enabling real-time interactivity where needed.

**Render mode decisions:**
- **Static SSR + StreamRendering:** Home, CollectionPage, ProductDetail (shell) — SEO-critical, no client interactivity needed
- **InteractiveServer:** ProductInteractions, Cart, Checkout, CartBadge, QuantitySelector — share cart state via scoped DI services within the same SignalR circuit
- **Static SSR (CSS toggle):** ChatFab, QuizOverlay — pure CSS checkbox hacks, no server state needed

Avoid WebAssembly for now; InteractiveServer components share scoped DI services efficiently. WASM migration can happen later when Temporal backend exposes APIs.

## State Management

**Cart state** is managed by `ICartService` registered as **scoped** (one instance per SignalR circuit):
- `InMemoryCartService` uses a `List<CartItem>` + `Action? OnChange` event
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

**RRF Formula**: `score = 1/(k+rank_fts) + 1/(k+rank_vector)` where k=60.0. Products appearing in both rankings get boosted scores.

Products matching neither strategy return empty results (not "10 closest neighbors regardless of relevance").

## Chat Assistant (LA)

**Temporal Workflow** (`ChatAgentWorkflow`):
- Long-lived session per user (24h idle timeout, resumable via `IdConflictPolicy.UseExisting`)
- Update: `SendMessageAsync(ChatRequest)` — processes single user message, returns `ChatResponse`
- Query: `GetHistory()` — retrieves conversation history for UI hydration on reconnect
- Signal: `EndSessionAsync()` — graceful shutdown and cache cleanup

**AI Activities** (`ChatAgentActivities`):
- System prompt defines LA personality: warm, playful, conversational
- Tools: `search_products`, `get_product_details`, `get_categories`, `browse_category`, `add_to_cart`, `remove_from_cart`, **`view_cart`**, **`clear_cart`**
- Tool results are concise summaries, not raw JSON, to reduce token usage and improve accuracy
- Cart operations: view_cart reads from snapshot (zero HTTP calls), clear_cart and remove_from_cart emit actions locally

**Blazor Integration** (`ChatService`):
- Scoped service bridges Blazor UI ↔ Temporal workflow
- Auth-based session IDs: `chat-{userId}` (resumable) or `chat-anon-{guid}` (ephemeral)
- Builds cart snapshot on each message → workflow → activity for accurate `view_cart` results
- Applies cart actions locally (Add/Remove/Clear) after inference completes

**UI** (`ChatFab.razor` + `ChatMessageContent.razor`):
- FAB pill button (unchanged text "Chat with Love") toggles chat panel
- Instant message display: user message + thinking indicator shown immediately before Temporal round-trip
- Rich message rendering: markdown images `![alt](url)`, bold/italic, newlines, inline code
- Quick action buttons for common queries

## Terminology: Category vs. Collection

**Standardized usage:**
- **`Category`** = Internal/technical term used throughout C# code
  - Model: `WithLove.Shared.Models.Category`
  - Service interface methods: `GetCategoryAsync()`, `GetCategoriesAsync()`, `GetProductsByCategoryAsync()`
  - Component names: `CategoryCircle.razor`, `CategorySidebar.razor`
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
  - EF Core 10.0.3 with SQL Server provider
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
gh release create v1.0.0 --title "Version 1.0.0"
gh release list

```

