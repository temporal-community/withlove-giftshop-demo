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

Subsequent runs read from that cache. `just deploy` checks only this AppHost's environment file and removes it when the configured subscription, location, or resource group changes. It then performs a normal deployment so Aspire can save replacement state. `just deploy-clean` removes that file and also passes `--clear-cache` to Aspire; the latter can prevent replacement state from being saved during that run.

Collect the following values before running Step 3:

| Parameter | Where to find it |
|---|---|
| `openai-api-key` | OpenAI dashboard → API keys |
| `redis-password` | Generate once with `openssl rand -hex 24`; keep it stable across deploys |
| `stripe-api-key` | Stripe Dashboard → Developers → API keys → Secret key |
| `stripe-public-key` | Stripe Dashboard → Developers → API keys → Publishable key |
| `temporal-address` | Temporal Cloud → Namespace → gRPC endpoint (e.g. `your-ns.tmprl.cloud:7233`) |
| `temporal-namespace` | Temporal Cloud → Namespace name (e.g. `your-ns.acct`) |
| `temporal-api-key` | Temporal Cloud → API keys |
| `stripe-webhook-secret` | Managed by `just deploy`; do not set it manually |

> **Note:** `aspire secret set` stores values in the AppHost's local dev user secrets for `aspire run`. Those values are not read by `aspire deploy`. Use the interactive prompts (Step 3) or environment variables (see CI deploy section) to supply values to the deploy pipeline.

## Step 2 — Register Temporal Cloud search attributes (one-time)

WithLove uses two custom search attributes for workflow correlation. Register them in your Temporal Cloud namespace using `tcld`:

```bash
tcld namespace search-attribute add \
  --namespace your-ns.acct \
  --search-attribute-name StripeSessionId \
  --search-attribute-type Keyword

tcld namespace search-attribute add \
  --namespace your-ns.acct \
  --search-attribute-name CustomerId \
  --search-attribute-type Keyword
```

Alternatively, use the Temporal Cloud UI: **Namespace -> Search Attributes -> Add**.

These attributes must exist before the workflowServer starts or workflow searches will fail.

## Step 3 — Deploy

```bash
az login

# Optional: preview the underlying Aspire steps without deploying
just deploy-preview

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

## Step 4 — Stripe Event Destination automation

In production, the Stripe CLI container is replaced by a Stripe Event Destination. After Azure
provisioning and the Redis health check succeed, `just deploy` reads the shopSite ingress URL,
creates or reconciles the destination at `https://{shopsite-fqdn}/stripe/webhook`. When it creates
a destination, it records the signing secret in `.secrets.env`, installs it in Key Vault, and
restarts shopSite. It then verifies that the local record and Key Vault agree. Do not create the
destination manually; a failed run may already have created the endpoint.

The deploying identity needs Key Vault secret `get` and `set` permissions (for example, the **Key
Vault Secrets Officer** role). The preflight check leaves a non-sensitive
`withlove-stripe-webhook-preflight` secret in the vault by design. If the Key Vault write fails,
the secret remains recorded in `.secrets.env`; repair with:

```bash
just install-stripe-webhook-secret
just verify-stripe-webhook-secret
```

Use `just ensure-stripe-webhook` to reconcile the destination manually. `just recreate-stripe-webhook`
is recovery-only: it deletes and recreates the destination, causing a temporary webhook delivery
gap while the new signing secret is installed. `just destroy` removes destinations tagged for the
selected environment before tearing down Azure resources.

An existing Key Vault deployment can be reused without updating a changed parameter value. The
webhook automation writes a newly issued signing secret directly to Key Vault; a deploy alone is
not a reliable way to install a changed secret.

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

**Stripe webhook secret:** After rotating the secret in Stripe, record the new value as
`Parameters__stripe_webhook_secret` in `.secrets.env`. Install and verify it with:

```bash
just install-stripe-webhook-secret
just verify-stripe-webhook-secret
```

**Temporal Cloud:** Update `Parameters__temporal_api_key` in `.secrets.env`. If changing the
namespace, also update `Parameters__temporal_address` and `Parameters__temporal_namespace` and
register the search attributes in the new namespace. Existing workflows remain in the old
namespace. Deploy to apply configuration changes:

```bash
just deploy
```

Do not assume the deploy updated an existing Key Vault secret: Aspire can reuse the previous
Key Vault deployment even when a parameter changed. In the Azure portal, create a new version of
`kv-temporal-api-key` in the vault referenced by both `shopsite` and `workflowserver` if needed.
For versionless Key Vault references, Container Apps retrieves new versions within 30 minutes and
restarts active revisions that use them as environment variables. Verify both apps reconnect
before revoking the old key. See [Azure Container Apps secret management](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets).

**Other secrets:** Update `.secrets.env` and verify the corresponding live Key Vault value after
deployment. A changed deployment parameter alone does not guarantee that an existing vault value
was replaced. Verify each consuming app picks up the new value before revoking the old credential.

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
