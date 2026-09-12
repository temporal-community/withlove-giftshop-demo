# Azure Deployment Guide — WithLove Gift Shop

This guide walks through deploying the WithLove Gift Shop to Azure Container Apps. It assumes you know .NET but have not deployed an Aspire app before.

For the normal local workflow, use the repository's `just` recipes. They wrap the Aspire CLI with the cleanup and retry behavior needed by this application's Azure resources. Direct Aspire commands are documented below for CI, previews, and advanced troubleshooting.

## Prerequisites

- .NET 10 SDK
- Azure CLI (`az`) — authenticate with `az login`
- Aspire CLI — install from [aspire.dev](https://aspire.dev/get-started/install-cli/)
- `just` and `jq` for the repository deployment recipes
- Temporal Cloud account with a namespace provisioned and an API key
- Stripe account with API keys
- OpenAI account with an API key (used for embeddings and chat)

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

The chat path uses `TemporalCommunity.Extensions.AI` 0.14.2. Web starts
`WithLove.GiftShopChatWorkflow`; workflowServer executes each model step as
`TemporalCommunity.Extensions.AI.GetChatStep` and each requested GiftShop tool as its own
`TemporalCommunity.Extensions.AI.InvokeFunction` activity. The extension requires Temporal Server
1.31.0 or newer. Confirm the target Temporal Cloud namespace supports that server level before
deployment.

The AppHost represents the existing namespace with `TemporalCommunity.Aspire.Hosting` and
injects its connection settings into the Web and Workflow Server container apps. The Temporal
Cloud resource is excluded from deployment manifests, so deployment does not create the
namespace or register its search attributes.

shopSite uses sticky sessions — required for Blazor InteractiveServer (SignalR).

workflowServer uses TCP-only health probes because the HTTP `/health` endpoint is only exposed in development.

## Step 1 — Gather parameter values

Copy the environment template and fill in the Azure and application values:

```bash
cp .secrets.env.example .secrets.env
```

The `just` deployment recipes source `.secrets.env` and run Aspire non-interactively. All required values must be present in that file or exported by the calling shell. Aspire manages deployment parameter state separately from `aspire secret set` and caches resolved deployment values at:

```
~/.aspire/deployments/<apphost-sha256>/azureprod.json
```

Subsequent runs read from that cache. `just deploy` checks only this AppHost's environment file and removes it when the configured subscription, location, or resource group changes. It then performs a normal deployment so Aspire can save replacement state. `just deploy-clean` forces the same app-scoped reset. The recipes intentionally do not use `--clear-cache`, because that option ignores the cache and also prevents Aspire from saving replacement state.

Collect the following values before running Step 3:

| Parameter | Where to find it |
|---|---|
| `openai-api-key` | OpenAI dashboard → API keys |
| `ARIZE_OTLP_ENDPOINT` | The regional OTLP endpoint shown by the Arize AX connect page; no region is assumed by the application |
| `ARIZE_API_KEY` | Arize AX → Settings → API Keys; use a scoped service key |
| `ARIZE_SPACE_ID` | The base64 space ID used for OTLP ingestion, not the human-readable space name |
| `redis-password` | Generate once with `openssl rand -hex 24`; keep it stable across deploys |
| `stripe-api-key` | Stripe Dashboard → Developers → API keys → Secret key |
| `stripe-public-key` | Stripe Dashboard → Developers → API keys → Publishable key |
| `temporal-address` | Temporal Cloud → Namespace → gRPC endpoint (e.g. `your-ns.tmprl.cloud:7233`) |
| `temporal-namespace` | Temporal Cloud → Namespace name (e.g. `your-ns.acct`) |
| `temporal-api-key` | Temporal Cloud → API keys |
| `stripe-webhook-secret` | Enter `whsec_placeholder` for now — replaced in Step 5 |

> **Note:** `aspire secret set` stores values in the AppHost's local dev user secrets for `aspire run`. Those values are not read by `aspire deploy`. Use the interactive prompts (Step 3) or environment variables (see CI deploy section) to supply values to the deploy pipeline.

Published applications default to Arize AX for traces. The AX resource is external and excluded
from the deployment manifest; its endpoint, API key, and space ID remain deferred deployment
parameters. Logs and metrics continue to use Aspire's OTLP configuration, and AX headers are
attached only to the AX trace exporter.

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

# Optional: preview the underlying Aspire steps without deploying
aspire deploy \
  --apphost src/WithLove.AppHost/WithLove.AppHost.csproj \
  --environment azureprod \
  --list-steps

# Recommended developer workflow
just deploy
```

Before deploying, the recipe verifies that the active Azure CLI subscription and tenant match `.secrets.env`. It fails before provisioning if they do not match. The required Azure targeting values are:

- Azure subscription
- Azure region (for example, `eastus`)
- Resource group name (for example, `withlove-rg`)

After the deploy completes, the shopSite external URL is printed in the output, for example:

```
https://shopsite.victoriousbeach-abc123.eastus.azurecontainerapps.io
```

Keep this URL — you need it in the next step.

If you change the Azure location or resource group, use:

```bash
just deploy-clean
```

`just deploy` also purges matching soft-deleted Key Vaults from the configured disposable resource group, waits for resource-group transitions to finish, and retries only confirmed Azure pending-deletion conditions. Deterministic deployment failures are returned immediately. After a successful deployment, it restarts the Redis revision so a rotated `redis-password` is loaded by the running Redis process before clients reconnect.

### Azure SQL identity provisioning

Aspire 13.5 fixes the earlier Azure SQL role script that could fail in `Invoke-Sqlcmd` with a `Microsoft.Extensions.Caching.Memory` `MissingMethodException`. The application still uses one shared managed identity for three Container Apps, and that shared-identity role-module case has not been verified as safe with the default Aspire model.

The AppHost therefore continues to disable the default SQL role assignments and deploy one repository-owned `sql-identity-access` Bicep resource instead. It acquires an Azure SQL token, uses the in-box `System.Data.SqlClient`, reconciles the shared identity's database user by SID, and grants `db_owner` idempotently. Remove this workaround only after verifying through published artifacts and a disposable Azure deployment that the default model emits one safe role-provisioning path for the shared identity.

## Step 4 — Create the Stripe Event Destination

The Stripe CLI container used in development is replaced by a Stripe Event Destination in production. This destination sends webhook events to shopSite.

1. Go to **Stripe Dashboard -> Workbench -> Webhooks -> Create an event destination**
2. Event source: **Your account**
3. Payload format: **Snapshot** (preserves the v1 object format — no code changes needed)
4. Subscribe to events: `checkout.session.completed`, `checkout.session.expired`
5. Destination type: **Webhook endpoint**
6. URL: `https://{your-shopsite-domain}/stripe/webhook`
7. Save. On the destination detail page, click **"Click to reveal"** next to the signing secret and copy the `whsec_...` value.

## Step 5 — Update the webhook secret and redeploy

`aspire deploy` manages its own parameter cache (see Step 1) independently of `aspire secret set`. Running `aspire secret set` here would update the local dev user secrets — not the deploy cache — so the placeholder would remain and Stripe webhook verification would fail.

Replace `Parameters__stripe_webhook_secret` in `.secrets.env`, then deploy:

```bash
just deploy
```

The recipe sources `.secrets.env`, and the explicit value takes precedence over Aspire's cached parameter value. The deploy writes the updated secret to Key Vault and Container Apps picks it up.

## Verification checklist

After deploy completes:

1. Navigate to the shopSite external URL — the app loads and products display
2. Complete a test purchase using a [Stripe test card](https://docs.stripe.com/testing) — the order confirmation page appears and the webhook arrives
3. Navigate to `/account/loyalty` — points are earned and the Temporal workflow is running
4. In the Temporal Cloud UI, confirm a `loyalty-{userId}` workflow shows status Running
5. In the Azure portal, open the workflowServer Container App logs and confirm: `Connected to Temporal Cloud, processing tasks from with-love-tasks`
6. Confirm workflowServer has at least one replica running:

```bash
az containerapp replica list \
  --resource-group withlove-rg \
  --name workflowserver
```

7. Start a new assistant session and ask it to add a product, then view the cart
8. In Temporal Cloud, confirm the workflow type is `WithLove.GiftShopChatWorkflow` and its history
   contains separate `GetChatStep` and `InvokeFunction` activities
9. In the Aspire/Azure trace backend, confirm package spans named `chat ...` and `execute_tool ...`
   are exported by workflowServer
10. Refresh/reconnect the shop UI and confirm only user and final assistant text is rendered; tool
    protocol remains internal to Temporal/model context

## Non-interactive / CI deploy

For CI using the repository recipe, have the CI secret store materialize `.secrets.env` using `.secrets.env.example` as the schema. Include every required value, including `Parameters__redis_password`, then run:

```bash
test -s .secrets.env
just deploy
```

If CI supplies environment variables directly instead of creating `.secrets.env`, use the direct `aspire deploy --non-interactive` command shown in the advanced section below.

## Secret rotation

**Stripe webhook secret:** Rotate in Stripe Dashboard -> Webhooks -> select destination -> **Rotate secret**. Stripe accepts signatures from both the old and new secret for 24 hours. Replace `Parameters__stripe_webhook_secret` in `.secrets.env`, then deploy (`aspire secret set` updates local development secrets, not Azure deployment inputs):

```bash
just deploy
```

**Arize AX credentials:** Replace `ARIZE_API_KEY`, `ARIZE_SPACE_ID`, or `ARIZE_OTLP_ENDPOINT` in
`.secrets.env`, then deploy. These are Aspire parameter-backed application settings rather than Key
Vault references, so an AX credential or destination change requires a new application revision.

**Other Key Vault-backed secrets:** Replace the corresponding value in `.secrets.env`, then deploy.
Key Vault secrets are picked up by Container Apps within approximately 30 minutes automatically —
a forced restart is not required for non-critical rotations.

```bash
just deploy
```

## Teardown

```bash
just destroy
```

This first uses Aspire's destroy pipeline when its app-scoped state matches `.secrets.env`. If a failed deployment never saved state, or stale state points elsewhere, it falls back to deleting only the explicitly configured resource group. It waits up to 3,600 seconds by default for Azure to confirm that the resource group no longer exists, then purges matching soft-deleted Key Vaults and removes the exact local environment state file so the next `just deploy` starts cleanly.

Azure resource-group deletion is asynchronous and can spend an extended period in `Deleting` even after all contained resources are gone. If the timeout is reached, the recipe reports the current state plus remaining resource and lock counts, exits without claiming success, and leaves Azure's deletion running. Re-run `just destroy` to resume waiting and finish Key Vault cleanup. Override the wait ceiling when needed with `just destroy azureprod <seconds>`.

## Generated files (do not commit)

Aspire generates the following files during deploy. They are listed in `.gitignore` and must not be committed:

```
/infra/              # Bicep templates (regenerated from AppHost on each deploy)
azure.yaml           # azd manifest
aspire-manifest.json
.azure/              # azd environment state
```

If you prefer the `azd` CLI over `aspire deploy`:

```bash
azd auth login
azd init          # regenerates azure.yaml and infra/ from the AppHost
azd up --environment azureprod
```

`azd up` and `aspire deploy` produce the same deployment — they use the same underlying Bicep generation from the AppHost.

## Useful commands

| Command | Purpose |
|---|---|
| `just deploy` | Deploy or incrementally update Azure resources using the project safeguards |
| `just deploy-clean` | Deploy with fresh Aspire state after infrastructure-level changes |
| `just destroy` | Tear down Azure resources, wait for deletion, and clean up Key Vaults |
| `aspire secret list` | Show all stored secrets (values masked) |
| `aspire secret get <key>` | Read a single secret value |
| `aspire secret delete <key>` | Remove a secret |
| `aspire secret path` | Show path to the secrets JSON file |
| `aspire deploy --apphost src/WithLove.AppHost/WithLove.AppHost.csproj --list-steps --environment azureprod` | Preview deploy steps without executing |
| `az containerapp logs show --name shopsite --resource-group withlove-rg` | Stream shopSite logs |
| `az containerapp replica list --name workflowserver --resource-group withlove-rg` | Check workflowServer replicas |

## Direct Aspire CLI

Use the direct commands when running CI without `just`, previewing pipeline steps, or troubleshooting Aspire itself. These commands do not include the repository's account guard, Key Vault cleanup, resource-group wait, app-scoped cache repair, or transient Azure retry behavior:

```bash
source .secrets.env
aspire deploy \
  --apphost src/WithLove.AppHost/WithLove.AppHost.csproj \
  --environment azureprod \
  --non-interactive
aspire destroy \
  --apphost src/WithLove.AppHost/WithLove.AppHost.csproj \
  --environment azureprod \
  --non-interactive \
  --yes
```
