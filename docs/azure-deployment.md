# Azure Deployment Guide — WithLove Gift Shop

This guide walks through deploying the WithLove Gift Shop to Azure Container Apps. It assumes you know .NET but have not deployed an Aspire app before.

Deploy with the repository's `just` recipes. They wrap the Aspire CLI with the account guard, Key Vault cleanup, resource-group waits, retry behavior, and Stripe Event Destination automation this application needs. Calling `aspire deploy` directly skips all of it — including the webhook setup — so the recipes are the only supported path.

## Prerequisites

See the README: [Prerequisites](../README.md#prerequisites) for the tooling and accounts the app
needs locally, and [Additional Prerequisites](../README.md#additional-prerequisites) for the extra
pieces deployment requires — Azure CLI, `just`, `jq`, `tcld`, and a Temporal Cloud account.

Run `az login` before Step 3.

## Architecture overview

```
Internet
   │
   ▼
Azure Container Apps Environment ("withlove-env")
   ├── shopSite      (external HTTPS — user traffic + Stripe webhooks)
   ├── productsApi   (internal)
   └── workflowServer (internal)
         │
         ├── Azure SQL Database     (replaces local SQL Server container)
         ├── Redis container (ACA)  (same image as dev; no managed Redis)
         └── Temporal Cloud         (replaces local Temporal dev container)
```

The AppHost's Azure publish configuration swaps local development services for Azure-managed equivalents — except Redis. Azure Managed Redis has no Balanced SKUs available in US regions on this subscription and Azure Cache for Redis is being retired, so a Redis container is used in all environments. Cart data is ephemeral per deployment (no persistent volume in ACA). All secrets flow through Azure Key Vault, which injects values into each Container App as environment variables.

**Scaling configuration (set in AppHost):**

| Service | Min replicas | Max replicas | Scale trigger |
|---|---|---|---|
| shopSite | 1 | 10 | 100 concurrent HTTP requests per replica |
| workflowServer | 1 | 5 | CPU utilization 70% |
| productsApi | default | default | default |

workflowServer never scales to zero because it must continuously poll Temporal Cloud for tasks.

## Step 1 — Gather parameter values

Copy the environment template and fill in the Azure and application values:

```bash
cp .secrets.env.example .secrets.env
```

The `just` deployment recipes source `.secrets.env` and run Aspire non-interactively. The table
below distinguishes required values from optional settings and automation-owned values. Values may
also be exported by the calling shell. See [Troubleshooting](#troubleshooting) for how Aspire
caches resolved deployment values.

Collect the following values before running Step 3. Configuration uses `__` in environment-variable
form (`Parameters__openai_api_key`), not the `:` form used in .NET configuration
(`Parameters:openai-api-key`).

| Environment variable | Required when | Where to find it or default |
|---|---|---|
| `Azure__SubscriptionId` | Always | Azure subscription ID; must match the active Azure CLI subscription. |
| `Azure__ResourceGroup` | Always | Target resource group name. |
| `Azure__Location` | Always | Azure region, for example `eastus`. |
| `Azure__TenantId` | Optional | When supplied, must match the active Azure CLI tenant. |
| `Parameters__openai_api_key` | Always | OpenAI dashboard → API keys. |
| `Parameters__redis_password` | Always | Generate once with `openssl rand -hex 24`; keep it stable across deploys. |
| `Parameters__stripe_api_key` | Always | Stripe Dashboard → Developers → API keys → Secret key. |
| `Parameters__stripe_public_key` | Always | Stripe Dashboard → Developers → API keys → Publishable key. |
| `Parameters__temporal_address` | Always | Temporal Cloud → Namespace → gRPC endpoint, for example `your-ns.tmprl.cloud:7233`. |
| `Parameters__temporal_namespace` | Always | Temporal Cloud → Namespace name, for example `your-ns.acct`. |
| `Parameters__temporal_api_key` | Always | Temporal Cloud → API keys. |
| `ARIZE_OTLP_ENDPOINT`, `ARIZE_API_KEY`, `ARIZE_SPACE_ID` | `Trace__Destination` is absent or `Ax` | The Arize AX connect page. Published applications default to `Ax`; set `Trace__Destination=Aspire` to use the managed Aspire dashboard instead. |
| `Parameters__stripe_webhook_secret` | Never manually | Automation seeds a bootstrap value, then creates and records the real Stripe Event Destination secret. |
| `Trace__Destination` | Optional | `Ax` in a published app; accepted values are `Aspire`, `Phoenix`, and `Ax`. `just deploy --trace-destination Phoenix` deploys an internal, ephemeral Phoenix Container App for this sample; its UI is not publicly exposed. `just deploy --trace-destination Aspire` uses the managed Aspire dashboard instead. |
| `Trace__AiOnly` | Optional | `true`; applies only to AX/Phoenix and exports the connected AI trajectory rather than all trace spans. `just deploy --all-traces` sets it to `false` for one deployment. |
| `Telemetry__CaptureAiContent` | Optional | `false`; `just deploy --capture` explicitly sets it to `true` for that invocation. |

> **Note:** Deployment values come from `.secrets.env` or the calling environment. AppHost user
> secrets are not a deployment input.

The Stripe webhook signing secret is intentionally not an input. `just deploy` seeds its temporary
bootstrap value, creates the Stripe Event Destination after Azure assigns the shopSite URL, and
records the generated signing secret automatically.

Published applications default to Arize AX for traces. The AX resource is external and excluded
from the deployment manifest; its endpoint, API key, and space ID are required only while AX is
the selected destination. Logs and metrics continue to use Aspire's OTLP configuration, and AX
headers are attached only to the AX trace exporter. `Trace__AiOnly=true` is the default: AX receives
the connected chat trajectory while the Aspire dashboard continues to receive logs and metrics.

The sample's committed telemetry identity key is used in Azure as well as local environments. It
keeps raw user and session identifiers out of traces and preserves demo grouping, but it is public
and is not a production privacy boundary.

## Step 2 — Register Temporal Cloud search attributes (one-time)

WithLove uses four custom search attributes for workflow correlation and chat-session lifecycle
queries. Register them in your Temporal Cloud namespace using `tcld`:

```bash
tcld namespace search-attribute add \
  --namespace your-ns.acct \
  --search-attribute-name StripeSessionId \
  --search-attribute-type Keyword

tcld namespace search-attribute add \
  --namespace your-ns.acct \
  --search-attribute-name CustomerId \
  --search-attribute-type Keyword

tcld namespace search-attribute add \
  --namespace your-ns.acct \
  --search-attribute-name TurnCount \
  --search-attribute-type Int

tcld namespace search-attribute add \
  --namespace your-ns.acct \
  --search-attribute-name SessionCreatedAt \
  --search-attribute-type Datetime
```

Alternatively, use the Temporal Cloud UI: **Namespace -> Search Attributes -> Add**.

These attributes must exist before the workflowServer starts or workflow searches will fail.

## Step 3 — Deploy

```bash
az login

# Optional: preview the pipeline without provisioning anything
just deploy-preview

# Recommended developer workflow
just deploy

# Explicit opt-in to include AI inputs, outputs, system instructions, and tool payloads in telemetry
just deploy --capture

# Override the published AX default and export traces to the managed Aspire dashboard
just deploy --trace-destination Aspire

# Deploy an internal, ephemeral Phoenix trace backend for this sample
just deploy --trace-destination Phoenix

# Default AX: send all trace spans rather than only the AI trajectory; this does not capture payload content
just deploy --all-traces
```

Before deploying, the recipe verifies that the active Azure CLI subscription and tenant match `.secrets.env`. It fails before provisioning if they do not match. The required Azure targeting values are:

- Azure subscription
- Azure region (for example, `eastus`)
- Resource group name (`Azure__ResourceGroup`; `.secrets.env.example` defaults to `rg-aspire-withlove`)

AI content capture is disabled by default. `just deploy --capture` sets
`Telemetry__CaptureAiContent=true` for that deployment after `.secrets.env` is loaded, so the
explicit command-line opt-in takes precedence over a value in the file. Capture can export chat
inputs and outputs, model messages and system instructions, and tool arguments and results; use it
only when that data is appropriate for the configured telemetry backend. It affects new telemetry
only and does not redact or delete existing traces.

After the deploy completes, the shopSite external URL is printed in the output, for example:

```
https://shopsite.victoriousbeach-abc123.eastus.azurecontainerapps.io
```

The deployment recipe reads this URL itself when it configures the Stripe Event Destination.

If you change the Azure location or resource group, use:

```bash
just deploy-clean
```

`just deploy` also purges matching soft-deleted Key Vaults from the configured disposable resource group, waits for resource-group transitions to finish, and retries only confirmed Azure pending-deletion conditions. Deterministic deployment failures are returned immediately. After a successful deployment, it restarts the Redis revision so a rotated `redis-password` is loaded by the running Redis process before clients reconnect.

## Step 4 — Stripe Event Destination automation

The Stripe CLI container used in development is replaced by a Stripe Event Destination in
production. After Azure provisioning and the Redis health check succeed, `just deploy`:

1. Reads the shopSite ingress URL from Azure.
2. Creates or reconciles the managed Stripe Event Destination for
   `https://{shopsite-fqdn}/stripe/webhook` and the required checkout events.
3. Records the signing secret in `.secrets.env` and installs it in Key Vault.
4. Restarts the shopSite revision so Stripe signature verification uses the new secret.

Do not create this Event Destination or copy its signing secret manually. A failed automation run
may already have created an endpoint, so manually creating another can leave duplicate Stripe
destinations with different signing secrets.

For one-pass completion, the deploying identity needs Key Vault data-plane `get` and `set`
(`Key Vault Secrets Officer`); `delete` is not used and the preflight probe secret stays by design.
Without them the recipe records the value in `.secrets.env` and reports a failed deployment.
**Re-running `just deploy` does not install it** — the secret is a deployment parameter, not
template content, so an unchanged template short-circuits and Key Vault keeps the stale value.
Repair with `just install-stripe-webhook-secret` (writes the vault, restarts shopsite) and confirm
with `just verify-stripe-webhook-secret`; `just destroy && just deploy` is the heavy alternative.

## Verification checklist

After deploy completes:

1. Navigate to the shopSite external URL — the app loads and products display
2. Complete a test purchase using a [Stripe test card](https://docs.stripe.com/testing) — the order confirmation page appears and the webhook arrives
3. Navigate to `/account/loyalty` — points are earned and the Temporal workflow is running
4. In the Temporal Cloud UI, confirm a `loyalty-{userId}` workflow shows status Running
5. In the Azure portal, open the workflowServer Container App logs and confirm: `Connected to Temporal Cloud, processing tasks from with-love-tasks`
6. Confirm workflowServer has at least one replica running:

```bash
source .secrets.env
az containerapp replica list \
  --resource-group "${Azure__ResourceGroup}" \
  --name workflowserver
```

7. Start a new assistant session and ask it to add a product, then view the cart
8. In Temporal Cloud, confirm the workflow type is `WithLove.GiftShopChatWorkflow` and its history
   contains separate `GetChatStep` and `InvokeFunction` activities
9. In the Aspire/Azure trace backend, confirm package spans named `chat ...` and `execute_tool ...`
   are exported by workflowServer
10. Refresh/reconnect the shop UI and confirm only user and final assistant text is rendered; tool
    protocol remains internal to Temporal/model context
11. Open the managed Aspire dashboard at the URL printed at the end of the deploy and confirm each
    Container App reports logs and metrics

**Managed Aspire dashboard.** `aspire deploy` provisions it automatically — the AppHost never opts
out and `EnableDashboard` defaults to `true` — and the deploy pipeline's
`print-dashboard-url-withlove-env` step prints its URL at the end of a run. It is served at
`https://aspire-dashboard.ext.<env-domain>/`. Custom domains are not supported for it, so that
default `.ext` hostname is the only way in. If it refuses to authenticate, see
[Aspire dashboard authentication failure](#aspire-dashboard-authentication-failure).

## Secret rotation

**Stripe webhook secret:** It is automation-owned. Do not rotate it in the Stripe Dashboard, because the application will not know the replacement value. If recovery requires a fresh Event Destination and signing secret, run:

```bash
just recreate-stripe-webhook
```

This is a recovery operation, not routine rotation: it deletes the managed Event Destination before
creating a replacement, which creates a temporary delivery gap. Follow any non-zero result exactly;
if the Key Vault write did not land, run `just install-stripe-webhook-secret` — not `just deploy`.

**Arize AX credentials:** Replace `ARIZE_API_KEY`, `ARIZE_SPACE_ID`, or `ARIZE_OTLP_ENDPOINT` in
`.secrets.env`, then deploy. These are Aspire parameter-backed application settings rather than Key
Vault references, so an AX credential or destination change requires a new application revision.

**Other Key Vault-backed secrets:** Replace the value in `.secrets.env`, then `just deploy-clean` —
a plain `just deploy` does not rewrite an existing Key Vault secret, for the same reason as above.
Secrets reach the app as environment variables fixed at container start, so restart the revision.

## Teardown

```bash
just destroy
```

This first uses Aspire's destroy pipeline when its app-scoped state matches `.secrets.env`. If a failed deployment never saved state, or stale state points elsewhere, it falls back to deleting only the explicitly configured resource group. It waits up to 3,600 seconds by default for Azure to confirm that the resource group no longer exists, then purges matching soft-deleted Key Vaults and removes the exact local environment state file so the next `just deploy` starts cleanly.

Azure resource-group deletion is asynchronous and can spend an extended period in `Deleting` even after all contained resources are gone. If the timeout is reached, the recipe reports the current state plus remaining resource and lock counts, exits without claiming success, and leaves Azure's deletion running. Re-run `just destroy` to resume waiting and finish Key Vault cleanup. Override the wait ceiling when needed with `just destroy azureprod <seconds>`.

## Useful commands

| Command | Purpose |
|---|---|
| `just deploy` | Deploy or incrementally update Azure resources using the project safeguards |
| `just deploy-clean` | Deploy with fresh Aspire state after infrastructure-level changes |
| `just destroy` | Tear down Azure resources, wait for deletion, and clean up Key Vaults |
| `just deploy-preview` | List the deploy pipeline's steps without provisioning anything |
| `just install-stripe-webhook-secret` | Push the `.secrets.env` signing secret into Key Vault and restart shopsite |
| `just verify-stripe-webhook-secret` | Check the Stripe signing secret agrees across `.secrets.env`, Key Vault and Stripe |
| `az containerapp logs show --name shopsite --resource-group "${Azure__ResourceGroup}"` | Stream shopSite logs |
| `az containerapp replica list --name workflowserver --resource-group "${Azure__ResourceGroup}"` | Check workflowServer replicas |

The `az` commands read the resource group from `.secrets.env` — `source .secrets.env` first, in the
repository root. The `just` recipes do this themselves; there is no hardcoded group name to keep in
sync.

## Troubleshooting

### Deployment parameter cache

Aspire manages deployment parameter state separately from `aspire secret set` and caches resolved
values at `~/.aspire/deployments/<apphost-sha256>/azureprod.json`. Subsequent runs read from that
cache. `just deploy` removes this AppHost's file when the configured subscription, location, or
resource group changes, then deploys normally so Aspire can save replacement state.
`just deploy-clean` forces the same reset. The recipes deliberately avoid `--clear-cache`, which
ignores the cache *and* prevents Aspire from saving replacement state.
