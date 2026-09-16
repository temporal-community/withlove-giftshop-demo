set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
set shell := ["bash", "-c"]

solution        := "WithLoveShop.slnx"
configuration   := "Debug"
apphost         := "src/WithLove.AppHost/WithLove.AppHost.csproj"
stripewebhooks  := "tools/WithLove.StripeWebhooks/WithLove.StripeWebhooks.csproj"

# List available recipes
default:
    @just --list

# Show project info
info:
    @echo "Solution  : {{solution}}"
    @echo "Config    : {{configuration}}"

# Remove all build output
clean:
    dotnet clean {{solution}} --configuration {{configuration}} --nologo -v q
    @echo "Clean complete."

# Restore NuGet packages
restore:
    dotnet restore {{solution}}

# Build the solution
build: restore
    dotnet build {{solution}} --configuration {{configuration}} --no-restore

# Alias: build
compile: build

# `run` declares --capture itself and forwards it, so `just run --capture` behaves exactly like
# `just run-phoenix --capture`. Without the attribute, just reads --capture as a second recipe
# name and fails with "justfile does not contain recipe `--capture`" — the first thing anyone
# tries, since run is the documented default. Keep the line below single: just uses the last
# comment line as the `just --list` description.

# Start the full stack with Phoenix; pass --capture to export AI payload content or --all-traces to disable AI-only filtering
[arg("capture", long="capture", value="true")]
[arg("all_traces", long="all-traces", value="true")]
run capture="false" all_traces="false": (run-phoenix capture all_traces)

# Start with Phoenix; pass --capture to export AI payload content or --all-traces to disable AI-only filtering
[arg("Telemetry__CaptureAiContent", long="capture", value="true")]
[arg("all_traces", long="all-traces", value="true")]
[env("Trace__Destination", "Phoenix")]
run-phoenix $Telemetry__CaptureAiContent="false" all_traces="false":
    if [[ "{{all_traces}}" == "true" ]]; then export Trace__AiOnly=false; fi
    aspire start --apphost {{ apphost }}

# Start with AX; pass --capture to export AI payload content or --all-traces to disable AI-only filtering
[arg("Telemetry__CaptureAiContent", long="capture", value="true")]
[arg("all_traces", long="all-traces", value="true")]
[env("Trace__Destination", "Ax")]
run-ax $Telemetry__CaptureAiContent="false" all_traces="false":
    if [[ "{{all_traces}}" == "true" ]]; then export Trace__AiOnly=false; fi
    aspire start --apphost {{ apphost }}

# Start with the Aspire dashboard as the trace destination; pass --capture to export AI payload content
[arg("Telemetry__CaptureAiContent", long="capture", value="true")]
[env("Trace__Destination", "Aspire")]
run-aspire $Telemetry__CaptureAiContent="false":
    aspire start --apphost {{ apphost }}

# Purge soft-deleted Key Vaults that were created by this AppHost environment.
# Key Vault names remain reserved after a normal delete, so this is required before
# recreating the same generated vault name in a disposable environment.
purge-deleted-keyvaults:
    #!/usr/bin/env bash
    set -euo pipefail
    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

    : "${Azure__SubscriptionId:?Azure__SubscriptionId is required}"
    : "${Azure__ResourceGroup:?Azure__ResourceGroup is required}"

    active_subscription="$(az account show --query id --output tsv)"
    if [[ "${active_subscription}" != "${Azure__SubscriptionId}" ]]; then
        echo "Azure CLI subscription '${active_subscription}' does not match configured subscription '${Azure__SubscriptionId}'." >&2
        exit 1
    fi

    vault_prefix="$(printf '%s' "/subscriptions/${Azure__SubscriptionId}/resourceGroups/${Azure__ResourceGroup}/providers/Microsoft.KeyVault/vaults/" | tr '[:upper:]' '[:lower:]')"
    deleted_vaults="$(az keyvault list-deleted \
        --subscription "${Azure__SubscriptionId}" \
        --resource-type vault \
        --query '[].name' \
        --output tsv)"

    if [[ -z "${deleted_vaults}" ]]; then
        echo "No soft-deleted Key Vaults found."
        exit 0
    fi

    purged_any=false
    while IFS= read -r vault_name; do
        [[ -z "${vault_name}" ]] && continue

        metadata="$(az keyvault show-deleted \
            --subscription "${Azure__SubscriptionId}" \
            --name "${vault_name}" \
            --output json 2>/dev/null || true)"
        [[ -z "${metadata}" ]] && continue

        vault_id="$(jq -r '.properties.vaultId // empty' <<< "${metadata}" | tr '[:upper:]' '[:lower:]')"
        deleted_location="$(jq -r '.properties.location // .location // empty' <<< "${metadata}")"
        purge_protection="$(jq -r '.properties.purgeProtectionEnabled // false' <<< "${metadata}")"
        [[ "${vault_id}" == "${vault_prefix}"* ]] || continue

        if [[ "${purge_protection}" == "true" ]]; then
            echo "Cannot purge ${vault_name}: purge protection is enabled." >&2
            exit 1
        fi

        echo "Purging soft-deleted AppHost Key Vault '${vault_name}'..."
        az keyvault purge \
            --subscription "${Azure__SubscriptionId}" \
            --name "${vault_name}" \
            --location "${deleted_location}" \
            --only-show-errors
        purged_any=true

        for purge_attempt in {1..12}; do
            if ! az keyvault show-deleted \
                --subscription "${Azure__SubscriptionId}" \
                --name "${vault_name}" \
                --output none 2>/dev/null; then
                break
            fi
            if (( purge_attempt == 12 )); then
                echo "Timed out waiting for Key Vault '${vault_name}' to leave the deleted state." >&2
                exit 1
            fi
            sleep 5
        done
    done <<< "${deleted_vaults}"

    if [[ "${purged_any}" == false ]]; then
        echo "No soft-deleted AppHost Key Vaults matched '${Azure__ResourceGroup}'."
    fi

# Rewrite Parameters__stripe_webhook_secret in .secrets.env, in place.
#
# The value arrives in WITHLOVE_STRIPE_WEBHOOK_VALUE rather than as a recipe argument so that a
# real `whsec_` signing secret never reaches argv, `ps` output, or shell history. Set the variable
# to the empty string to clear the parameter (see destroy / remove-stripe-webhook).
#
# Why this exists at all: .secrets.env is the input `aspire deploy` resolves the
# stripe-webhook-secret parameter from, and ConfigureAzureDependencies emits
# kv-stripe-webhook-secret from that parameter into the Key Vault Bicep.
#
# It does NOT follow that a deploy rewrites the stored Key Vault value. The value is a deployment
# *parameter*, not template content, so an unchanged template is a no-op: Aspire short-circuits
# with "Using existing deployment for keyvault (0.0s)" and whatever version the vault already
# holds survives untouched. This was observed in a real deploy on 2026-09-12, and confirmed with
# `az keyvault secret list-versions` — one version, created hours before the deploy that was
# supposed to have replaced it.
#
# So both directions of the old, simpler story are wrong. Writing here does not make a secret
# live (ensure-stripe-webhook's direct `az keyvault secret set` is what does, and
# verify-stripe-webhook-secret is what proves it landed), and a Key Vault value is not reliably
# erased by the next deploy either. What this file *is* is the durable record: the only store
# that survives a teardown, and the parameter a genuinely fresh provision resolves from. Keep it
# accurate for that reason, not because a deploy will replay it. See the design's §1.3 / §3.3.
[private]
write-stripe-webhook-secret:
    #!/usr/bin/env bash
    set -euo pipefail
    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    if [[ -z "${WITHLOVE_STRIPE_WEBHOOK_VALUE+x}" ]]; then
        echo "Error: WITHLOVE_STRIPE_WEBHOOK_VALUE must be set (it may be the empty string)." >&2
        exit 1
    fi

    # Build the new file beside the system temp dir, never in the repo: .gitignore covers the
    # literal name '.secrets.env' only, so a stray '.secrets.env.XXXX' would show up in git status.
    rewritten_secrets_env="$(mktemp)"
    trap 'rm -f -- "${rewritten_secrets_env}"' EXIT

    # Always emit the `export ` prefix: .secrets.env is sourced and every other assignment in it
    # is exported, and `aspire deploy` reads the parameter from the environment.
    awk '
        BEGIN { replaced = 0; value = ENVIRON["WITHLOVE_STRIPE_WEBHOOK_VALUE"] }
        /^[[:space:]]*(export[[:space:]]+)?Parameters__stripe_webhook_secret[[:space:]]*=/ {
            if (!replaced) {
                print "export Parameters__stripe_webhook_secret=\"" value "\""
                replaced = 1
            }
            next
        }
        { print }
        END { if (!replaced) print "export Parameters__stripe_webhook_secret=\"" value "\"" }
    ' .secrets.env > "${rewritten_secrets_env}"

    if [[ ! -s "${rewritten_secrets_env}" ]]; then
        echo "Error: rewriting .secrets.env produced an empty file; leaving the original untouched." >&2
        exit 1
    fi

    # Overwrite through the existing inode rather than mv, so the file keeps its current
    # ownership and permissions and no unignored temp file is ever created in the repo.
    cat -- "${rewritten_secrets_env}" > .secrets.env

# Deploy the application to Azure using the shared production defaults.
# Requires .secrets.env in the repo root — copy .secrets.env.example and fill in your values.
#   just deploy                      # deploy to azureprod (uses cached state)
#   just deploy --capture            # deploy and capture AI payload content in telemetry
#   just deploy --all-traces         # default AX: export all trace spans rather than AI-only trajectory
#   just deploy --trace-destination Aspire   # deploy with Aspire as the trace destination
#   just deploy --trace-destination Phoenix  # deploy internal, ephemeral Phoenix for this sample
#   just deploy staging              # deploy to a different environment
#   just deploy-clean                # deploy with fresh Aspire state
[arg("capture", long="capture", value="true")]
[arg("all_traces", long="all-traces", value="true")]
[arg("trace_destination", long="trace-destination")]
deploy environment="azureprod" reset_state="false" capture="false" all_traces="false" trace_destination="":
    #!/usr/bin/env bash
    set -euo pipefail
    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

    # Apply the command-line privacy opt-in after loading local deployment settings, so
    # `just deploy --capture` cannot be accidentally overridden by .secrets.env.
    if [[ "{{capture}}" == "true" ]]; then
        export Telemetry__CaptureAiContent=true
    fi
    if [[ -n "{{trace_destination}}" ]]; then
        export Trace__Destination="{{trace_destination}}"
    fi
    if [[ "{{all_traces}}" == "true" ]]; then
        export Trace__AiOnly=false
    fi

    : "${Azure__SubscriptionId:?Azure__SubscriptionId is required}"
    : "${Azure__ResourceGroup:?Azure__ResourceGroup is required}"
    : "${Azure__Location:?Azure__Location is required}"

    active_account="$(az account show --query '{subscription:id,tenant:tenantId}' --output tsv)"
    IFS=$'\t' read -r active_subscription active_tenant <<< "${active_account}"
    if [[ "${active_subscription}" != "${Azure__SubscriptionId}" ]]; then
        echo "Azure CLI subscription '${active_subscription}' does not match configured subscription '${Azure__SubscriptionId}'." >&2
        exit 1
    fi
    if [[ -n "${Azure__TenantId:-}" && "${active_tenant}" != "${Azure__TenantId}" ]]; then
        echo "Azure CLI tenant '${active_tenant}' does not match configured tenant '${Azure__TenantId}'." >&2
        exit 1
    fi

    # Placed after the subscription/tenant guards so a misconfigured run never writes .secrets.env.
    #
    # The stripe-webhook-secret parameter has to resolve *before* aspire deploy runs, because
    # ConfigureAzureDependencies emits kv-stripe-webhook-secret from it as part of the Bicep. But
    # the destination URL needs the shopSite FQDN, which is an *output* of that same deployment —
    # a genuine cycle automation can relocate but not remove. Seed a placeholder so the operator
    # never has to type one; ensure-stripe-webhook replaces it below, once the FQDN exists.
    #
    # This is why deploy writes .secrets.env twice: a placeholder here, the real signing secret
    # after the deployment completes. A value that is already set is never overwritten.
    if [[ -z "${Parameters__stripe_webhook_secret:-}" ]]; then
        echo "Seeding placeholder Parameters__stripe_webhook_secret; ensure-stripe-webhook will replace it."
        WITHLOVE_STRIPE_WEBHOOK_VALUE="whsec_placeholder" just write-stripe-webhook-secret
        Parameters__stripe_webhook_secret="whsec_placeholder"
    fi
    export Parameters__stripe_webhook_secret

    apphost_path="$(cd "$(dirname "{{apphost}}")" && pwd)/$(basename "{{apphost}}")"
    normalized_apphost_path="$(printf '%s' "${apphost_path}" | tr '[:upper:]' '[:lower:]')"
    if command -v shasum >/dev/null 2>&1; then
        apphost_sha="$(printf '%s' "${normalized_apphost_path}" | shasum -a 256 | awk '{print toupper($1)}')"
    elif command -v sha256sum >/dev/null 2>&1; then
        apphost_sha="$(printf '%s' "${normalized_apphost_path}" | sha256sum | awk '{print toupper($1)}')"
    else
        apphost_sha="$(printf '%s' "${normalized_apphost_path}" | openssl dgst -sha256 | awk '{print toupper($NF)}')"
    fi
    environment_name="$(printf '%s' "{{environment}}" | tr '[:upper:]' '[:lower:]')"
    deployment_state_file="${HOME}/.aspire/deployments/${apphost_sha}/${environment_name}.json"

    if [[ -f "${deployment_state_file}" ]]; then
        cached_subscription="$(jq -r '.["Azure:SubscriptionId"] // empty' "${deployment_state_file}")"
        cached_resource_group="$(jq -r '.["Azure:ResourceGroup"] // empty' "${deployment_state_file}")"
        cached_location="$(jq -r '.["Azure:Location"] // empty' "${deployment_state_file}")"

        if [[ "{{reset_state}}" == "true" ||
              "${cached_subscription}" != "${Azure__SubscriptionId}" ||
              "${cached_resource_group}" != "${Azure__ResourceGroup}" ||
              "${cached_location}" != "${Azure__Location}" ]]; then
            echo "Removing stale Aspire state for '{{environment}}' at ${deployment_state_file}." >&2
            rm -f -- "${deployment_state_file}"
        fi
    elif [[ "{{reset_state}}" == "true" ]]; then
        echo "No cached Aspire state exists for '{{environment}}'."
    fi

    wait_for_resource_group_ready() {
        local wait_attempt resource_group_state
        for ((wait_attempt = 1; wait_attempt <= 40; wait_attempt++)); do
            resource_group_state="$(az group show \
                --subscription "${Azure__SubscriptionId}" \
                --name "${Azure__ResourceGroup}" \
                --query properties.provisioningState \
                --output tsv 2>/dev/null || true)"

            if [[ -z "${resource_group_state}" || "${resource_group_state}" == "Succeeded" ]]; then
                return 0
            fi

            case "${resource_group_state}" in
                Deleting|Creating|Updating)
                    echo "Resource group '${Azure__ResourceGroup}' is ${resource_group_state}; waiting 15s..." >&2
                    sleep 15
                    ;;
                *)
                    echo "Resource group '${Azure__ResourceGroup}' is in unexpected state '${resource_group_state}'." >&2
                    return 1
                    ;;
            esac
        done

        echo "Timed out waiting for resource group '${Azure__ResourceGroup}' to become ready." >&2
        return 1
    }

    wait_for_resource_group_ready
    just purge-deleted-keyvaults

    deploy_args=(
        deploy
        --apphost "${apphost_path}"
        --environment "{{environment}}"
        --non-interactive
    )

    max_attempts=3
    retry_delay_seconds=60
    for ((attempt = 1; attempt <= max_attempts; attempt++)); do
        wait_for_resource_group_ready
        echo "Aspire deploy attempt ${attempt}/${max_attempts}..."
        set +e
        aspire "${deploy_args[@]}"
        deploy_status=$?
        set -e

        if (( deploy_status == 0 )); then
            redis_revision="$(az containerapp show \
                --subscription "${Azure__SubscriptionId}" \
                --resource-group "${Azure__ResourceGroup}" \
                --name rediscache \
                --query properties.latestRevisionName \
                --output tsv 2>/dev/null || true)"
            if [[ -n "${redis_revision}" ]]; then
                echo "Restarting Redis revision '${redis_revision}' to apply the configured password."
                az containerapp revision restart \
                    --subscription "${Azure__SubscriptionId}" \
                    --resource-group "${Azure__ResourceGroup}" \
                    --name rediscache \
                    --revision "${redis_revision}" \
                    --output none

                for redis_wait_attempt in {1..24}; do
                    redis_state="$(az containerapp revision show \
                        --subscription "${Azure__SubscriptionId}" \
                        --resource-group "${Azure__ResourceGroup}" \
                        --name rediscache \
                        --revision "${redis_revision}" \
                        --query '{health:properties.healthState,running:properties.runningState}' \
                        --output tsv 2>/dev/null || true)"
                    if [[ "${redis_state}" == $'Healthy\tRunning' ]]; then
                        echo "Redis revision '${redis_revision}' is healthy."
                        break
                    fi
                    if (( redis_wait_attempt == 24 )); then
                        echo "Timed out waiting for Redis revision '${redis_revision}' to become healthy." >&2
                        exit 1
                    fi
                    sleep 5
                done
            fi

            # Runs after the Redis gate, not before: that gate already exits non-zero on timeout,
            # and creating a Stripe destination pointed at a deployment that then fails its own
            # health check would leave a live destination aimed at an app that never came up.
            set +e
            just ensure-stripe-webhook "{{environment}}"
            stripe_webhook_status=$?
            set -e
            if (( stripe_webhook_status != 0 )); then
                echo "Deploy failed at the Stripe event destination step (exit ${stripe_webhook_status})." >&2
                echo "The Azure resources deployed successfully, but the webhook is not fully configured," >&2
                echo "so Stripe signature verification will fail. This is deliberately a red deploy:" >&2
                echo "'deployed but webhooks unconfigured' is the silent failure this step exists to" >&2
                echo "eliminate, and it would otherwise surface days later as loyalty workflows that" >&2
                echo "never start. Address the problem reported above; the repair it names is the one" >&2
                echo "to run — re-running 'just deploy {{environment}}' by itself may not be it." >&2
                exit "${stripe_webhook_status}"
            fi

            # One call site, deliberately placed after the step rather than inside it, so it covers
            # every way that step can report success — including the exit-11 path where Stripe
            # reconciled an existing destination and issued no secret at all. That path is how a
            # deploy went green over a Key Vault holding a secret from an earlier deploy: nothing
            # failed, and until now nothing checked. Keeping the check out here also means a future
            # early `exit 0` added inside ensure-stripe-webhook cannot quietly bypass it.
            set +e
            just verify-stripe-webhook-secret "{{environment}}"
            stripe_verify_status=$?
            set -e
            if (( stripe_verify_status != 0 )); then
                echo "Deploy failed webhook secret verification (exit ${stripe_verify_status})." >&2
                echo "The Azure resources are deployed and healthy, but the signing secret the running" >&2
                echo "app reads is not the one on record, so Stripe signature verification will reject" >&2
                echo "every delivery. Follow the repair printed above — this is exactly the state that" >&2
                echo "used to exit 0." >&2
                exit "${stripe_verify_status}"
            fi
            exit 0
        fi

        latest_aspire_log="$(ls -t "${HOME}/.aspire/logs"/cli_*.log 2>/dev/null | head -n 1 || true)"
        if [[ -z "${latest_aspire_log}" ]] || ! rg -qi 'pending delete operation|ResourceGroupBeingDeleted|resource group.*deprovisioning' "${latest_aspire_log}"; then
            exit "${deploy_status}"
        fi

        if (( attempt == max_attempts )); then
            echo "Aspire deploy still hit an Azure pending-delete conflict after ${max_attempts} attempts." >&2
            exit "${deploy_status}"
        fi

        echo "Azure is still releasing a generated deployment-script resource; retrying in ${retry_delay_seconds}s..." >&2
        sleep "${retry_delay_seconds}"
        retry_delay_seconds=$((retry_delay_seconds * 2))
    done

# Like deploy, but drops the cached deployment state first.
# Use this after changing Azure__Location, Azure__ResourceGroup, or similar infra-level settings.
#   just deploy-clean           # deploy to azureprod with fresh state
#   just deploy-clean --capture # deploy with fresh state and capture AI payload content
#   just deploy-clean --all-traces # fresh state; default AX exports all trace spans
#   just deploy-clean staging   # deploy to staging with fresh state
[arg("capture", long="capture", value="true")]
[arg("all_traces", long="all-traces", value="true")]
[arg("trace_destination", long="trace-destination")]
deploy-clean environment="azureprod" capture="false" all_traces="false" trace_destination="":
    just deploy "{{environment}}" true "{{capture}}" "{{all_traces}}" "{{trace_destination}}"

# Read-only: `aspire deploy --list-steps` enumerates the pipeline and exits. It runs none of
# `deploy`'s safeguards — no subscription guard, no Key Vault purge, no resource-group wait —
# because it changes nothing, and it deliberately does not source .secrets.env for the same
# reason. Use it to see what `just deploy` will do, or to check that a new pipeline step (for
# example validate-stripe-webhook-secret) is registered and ordered where you expect.
#   just deploy-preview             # preview azureprod
#   just deploy-preview staging     # preview a named environment
# Keep the line below single: just uses the last comment line as the `just --list` description.

# Print the Azure deploy pipeline's steps without provisioning anything
deploy-preview environment="azureprod":
    aspire deploy --apphost {{ apphost }} --environment "{{environment}}" --list-steps

# Reconcile the Stripe event destination for a deployed environment, and install the signing
# secret if a new destination had to be created.
#
# This runs automatically at the end of `just deploy`. Run it by hand when you suspect drift —
# it is safe to re-run, and in steady state it makes no changes at all.
#
# Two modes:
#   ensure    (default) create the destination only if it is missing; otherwise reconcile the URL
#   recreate            delete and recreate it, minting a fresh signing secret
#
# Use `recreate` only for recovery. Stripe never returns a signing secret after the create call,
# so if Key Vault and Stripe have drifted apart there is no API that can read the current value
# back — recreating is the only operation that yields a secret we can both see and install. The
# cost is a real gap: between the delete and the restarted revision, Stripe signs deliveries with
# a secret the running app does not hold. Stripe retries, so this is recoverable rather than
# lossy, but it is why recreate is not the steady-state path.
#   just ensure-stripe-webhook                  # reconcile azureprod
#   just ensure-stripe-webhook staging          # reconcile a named environment
#   just ensure-stripe-webhook azureprod recreate
# Keep the line below single: just uses the last comment line as the `just --list` description.

# Create or reconcile the Stripe event destination for a deployed environment
ensure-stripe-webhook environment="azureprod" mode="ensure":
    #!/usr/bin/env bash
    set -euo pipefail

    case "{{mode}}" in
        ensure|recreate) ;;
        *)
            echo "Error: mode must be 'ensure' or 'recreate', got '{{mode}}'." >&2
            exit 1
            ;;
    esac

    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

    : "${Azure__SubscriptionId:?Azure__SubscriptionId is required}"
    : "${Azure__ResourceGroup:?Azure__ResourceGroup is required}"
    : "${Parameters__stripe_api_key:?Parameters__stripe_api_key is required}"

    # The tool reads the Stripe key from the environment. It is never passed as an argument.
    export Parameters__stripe_api_key

    # Same subscription/tenant guards as deploy, so this is safe to run standalone.
    active_account="$(az account show --query '{subscription:id,tenant:tenantId}' --output tsv)"
    IFS=$'\t' read -r active_subscription active_tenant <<< "${active_account}"
    if [[ "${active_subscription}" != "${Azure__SubscriptionId}" ]]; then
        echo "Azure CLI subscription '${active_subscription}' does not match configured subscription '${Azure__SubscriptionId}'." >&2
        exit 1
    fi
    if [[ -n "${Azure__TenantId:-}" && "${active_tenant}" != "${Azure__TenantId}" ]]; then
        echo "Azure CLI tenant '${active_tenant}' does not match configured tenant '${Azure__TenantId}'." >&2
        exit 1
    fi

    if [[ ! -f "{{stripewebhooks}}" ]]; then
        echo "Error: {{stripewebhooks}} not found." >&2
        exit 1
    fi

    # ---- preflight: the destination URL ------------------------------------------------------
    # /stripe/webhook is MapStripeWebhookHandler's default pattern, and Web maps it with no
    # arguments, so the route and the Stripe__Default__WebhookSecret binding agree by default.
    shopsite_fqdn="$(az containerapp show \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --name shopsite \
        --query properties.configuration.ingress.fqdn \
        --output tsv 2>/dev/null || true)"
    if [[ -z "${shopsite_fqdn}" ]]; then
        echo "Error: could not read the shopsite ingress FQDN from resource group '${Azure__ResourceGroup}'." >&2
        echo "  Deploy the environment first: just deploy {{environment}}" >&2
        exit 1
    fi
    webhook_url="https://${shopsite_fqdn}/stripe/webhook"

    # ---- preflight: can this principal write the Key Vault secret? ----------------------------
    # ConfigureKeyVaultAccess grants the *app* identity read access; nothing in the repo grants
    # the *operator* write access. Probe it here, before any Stripe call, so the answer is known
    # while the account is still unmutated — discovering it after a destination has been created
    # is the difference between "re-run it" and "go delete a destination by hand".
    key_vault_name=""
    candidate_vaults="$(az keyvault list \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --query '[].name' \
        --output tsv 2>/dev/null || true)"
    while IFS= read -r candidate_vault; do
        [[ -z "${candidate_vault}" ]] && continue
        if az keyvault secret show \
            --subscription "${Azure__SubscriptionId}" \
            --vault-name "${candidate_vault}" \
            --name kv-stripe-webhook-secret \
            --output none 2>/dev/null; then
            key_vault_name="${candidate_vault}"
            break
        fi
    done <<< "${candidate_vaults}"

    # The probe writes a secret and deliberately *leaves it in place*. An earlier version deleted
    # it again, which was wrong: this vault has soft-delete on (90-day retention), so the deleted
    # name cannot be reused, and every run after the first got a Conflict that the old
    # `2>/dev/null` flattened into "cannot write". Correct once per vault, a false negative
    # forever after — and a false negative here routes a run that mints a new signing secret into
    # the degraded path for a reason that is not true. `set` is idempotent, so one permanently
    # present, obviously-named probe secret is the whole cost — each run adds a version to that
    # one secret instead of consuming a name, and versions are not what soft-delete blocks. Do not
    # switch to a unique-per-run name: that only moves the leak into the deleted-secrets list.
    kv_probe_name=withlove-stripe-webhook-preflight

    # stdout dropped, stderr captured: *why* a write failed is the entire point of this probe.
    # 'preflight' is not a secret, so passing it on argv is fine.
    kv_probe_write() {
        { az keyvault secret set \
            --subscription "${Azure__SubscriptionId}" \
            --vault-name "${key_vault_name}" \
            --name "${kv_probe_name}" \
            --value preflight \
            --output none >/dev/null; } 2>&1
    }

    key_vault_writable=false
    key_vault_probe_error=""
    if [[ -n "${key_vault_name}" ]]; then
        set +e
        key_vault_probe_error="$(kv_probe_write)"
        kv_probe_status=$?
        set -e

        # A probe name left soft-deleted by the older version of this check. That is a state
        # problem, not a permission answer: recover the name and retry. Recovery is asynchronous,
        # so the retry polls instead of assuming.
        if (( kv_probe_status != 0 )) && \
           grep -qiE 'ObjectIsDeletedButRecoverable|deleted but recoverable' <<< "${key_vault_probe_error}"; then
            echo "Preflight: probe secret '${kv_probe_name}' is soft-deleted in '${key_vault_name}'; recovering it."
            set +e
            kv_recover_error="$({ az keyvault secret recover \
                --subscription "${Azure__SubscriptionId}" \
                --vault-name "${key_vault_name}" \
                --name "${kv_probe_name}" \
                --output none >/dev/null; } 2>&1)"
            kv_recover_status=$?
            set -e
            if (( kv_recover_status == 0 )); then
                for _ in 1 2 3 4 5 6 7 8 9 10; do
                    set +e
                    key_vault_probe_error="$(kv_probe_write)"
                    kv_probe_status=$?
                    set -e
                    if (( kv_probe_status == 0 )); then
                        break
                    fi
                    grep -qiE 'deleted but recoverable|currently being|Conflict' <<< "${key_vault_probe_error}" || break
                    sleep 3
                done
            else
                key_vault_probe_error="${kv_recover_error}"
                kv_probe_status="${kv_recover_status}"
            fi
        fi

        if (( kv_probe_status == 0 )); then
            key_vault_writable=true
            key_vault_probe_error=""
        else
            # One bounded line: this lands in a deploy log. The probed value is the literal string
            # 'preflight', so no secret can be quoted back here.
            key_vault_probe_error="$(tr '\n' ' ' <<< "${key_vault_probe_error}" \
                | tr -s ' ' | sed 's/^ *//; s/ *$//' | cut -c1-300)"
        fi
    fi

    # "Denied" and "failed" are different answers and must never be printed as the same one. Only
    # an authorization error says anything about this principal's permissions; a Conflict, an
    # unknown vault or a network failure leaves the question open.
    key_vault_write_denied=false
    if [[ -n "${key_vault_probe_error}" ]] && \
       grep -qiE 'forbidden|accessdenied|authorizationfailed|unauthorized|does not have secrets [a-z]+ permission' \
           <<< "${key_vault_probe_error}"; then
        key_vault_write_denied=true
    fi

    # Skip the later Key Vault write only when it is known to be pointless: no vault, or a denial.
    # An inconclusive probe is not a denial — by the time that write is attempted the secret is
    # already recorded in .secrets.env, so trying and failing costs nothing and succeeding saves
    # the operator a manual repair.
    key_vault_write_blocked=false
    if [[ -z "${key_vault_name}" || "${key_vault_write_denied}" == "true" ]]; then
        key_vault_write_blocked=true
    fi

    if [[ "${key_vault_writable}" != "true" ]]; then
        if [[ -z "${key_vault_name}" ]]; then
            echo "Preflight: no Key Vault in '${Azure__ResourceGroup}' exposes kv-stripe-webhook-secret to this principal." >&2
        elif [[ "${key_vault_write_denied}" == "true" ]]; then
            echo "Preflight: this principal cannot write secrets to Key Vault '${key_vault_name}' (access denied)." >&2
            echo "  Azure: ${key_vault_probe_error}" >&2
        else
            echo "Preflight: could not confirm write access to Key Vault '${key_vault_name}'. The probe" >&2
            echo "  failed for a reason that is not an authorization error, so this is unknown, not" >&2
            echo "  denied; the Key Vault write will still be attempted if a secret is minted." >&2
            echo "  Azure: ${key_vault_probe_error}" >&2
        fi
        if [[ "${key_vault_write_blocked}" == "true" ]]; then
            echo "  If a new signing secret is minted this run records it in .secrets.env and stops" >&2
            echo "  before the Key Vault write. Re-running 'just deploy' does NOT install it: the" >&2
            echo "  secret is a deployment parameter, not template content, so an unchanged template" >&2
            echo "  short-circuits and the stored value is left alone. The repair is" >&2
            echo "    just install-stripe-webhook-secret {{environment}}" >&2
            echo "  (direct 'az keyvault secret set' plus a revision restart), or a full" >&2
            echo "    just destroy {{environment}} && just deploy {{environment}}" >&2
        fi
    else
        echo "Preflight: this principal can write secrets to Key Vault '${key_vault_name}'."
    fi

    # ---- reconcile against Stripe ------------------------------------------------------------
    # The secret is never printed and never crosses stdout. The tool writes it to --secret-out
    # with mode 0600; the enclosing directory is 0700 so nothing else can read or pre-create it.
    stripe_secret_dir="$(mktemp -d)"
    chmod 700 "${stripe_secret_dir}"
    trap 'rm -rf -- "${stripe_secret_dir}"' EXIT
    stripe_secret_out="${stripe_secret_dir}/signing-secret"

    echo "Reconciling the Stripe event destination for ${webhook_url} (mode: {{mode}})."
    set +e
    dotnet run --project "{{stripewebhooks}}" -- \
        "{{mode}}" \
        --url "${webhook_url}" \
        --tag "{{environment}}" \
        --secret-out "${stripe_secret_out}"
    stripe_tool_status=$?
    set -e

    case "${stripe_tool_status}" in
        0)
            echo "Stripe event destination already matches ${webhook_url}; nothing to install."
            exit 0
            ;;
        10)
            echo "Created a Stripe event destination for ${webhook_url}; installing the signing secret."
            ;;
        11)
            echo "Stripe event destination already existed and was reconciled to ${webhook_url}."
            echo "No new signing secret was issued, so the stored one remains correct."
            exit 0
            ;;
        *)
            echo "Error: stripe-webhook {{mode}} failed with exit code ${stripe_tool_status}." >&2
            exit "${stripe_tool_status}"
            ;;
    esac

    if [[ ! -s "${stripe_secret_out}" ]]; then
        echo "Error: the tool reported a new destination (exit ${stripe_tool_status}) but wrote no secret." >&2
        echo "  A Stripe destination may now exist whose signing secret is recorded nowhere, and no" >&2
        echo "  Stripe API can read it back. Recover with:" >&2
        echo "    just ensure-stripe-webhook {{environment}} recreate" >&2
        exit 1
    fi

    stripe_webhook_secret="$(tr -d '\r\n' < "${stripe_secret_out}")"
    rm -f -- "${stripe_secret_out}"

    # Shape gate, deliberately duplicating validate-stripe-webhook-secret. The two are
    # complementary, not redundant: that one is a pipeline step on the *publish* graph and gates
    # the value going in, this one gates the value this recipe is about to write out. Neither is a
    # semantic check — whsec_placeholder passes both, by design.
    stripe_secret_shape='^whsec_[^[:space:]"'"'"']+$'
    stripe_secret_malformed=false
    if ! [[ "${stripe_webhook_secret}" =~ ${stripe_secret_shape} ]]; then
        stripe_secret_malformed=true
    fi
    # The curly quotes below are intentional, not a typo to be autocorrected: a human hand-editing
    # .secrets.env during recovery is exactly how a smart-quoted value gets in, and that is the
    # case validate-stripe-webhook-secret was written for.
    # shellcheck disable=SC1112
    case "${stripe_webhook_secret}" in
        *'“'*|*'”'*|*'‘'*|*'’'*) stripe_secret_malformed=true ;;
    esac
    if [[ "${stripe_secret_malformed}" == "true" ]]; then
        echo "Error: the value written to --secret-out is not a well-formed Stripe signing secret." >&2
        echo "  Expected a 'whsec_' prefix with no whitespace and no quote characters." >&2
        echo "  The value itself is not echoed. Recover with:" >&2
        echo "    just ensure-stripe-webhook {{environment}} recreate" >&2
        exit 1
    fi

    # ---- persist: .secrets.env first, Key Vault second ---------------------------------------
    # The order is not negotiable. .secrets.env is a local file write and near-certain to succeed;
    # the Key Vault write is a network call across an RBAC boundary that may not be granted. This
    # way the likely failure still leaves the secret where the repair path — and a genuinely fresh
    # provision — can read it; a redeploy over an existing vault will not replay it. Reversed, the
    # likely failure loses a secret that no API can return, forcing a recreate.
    WITHLOVE_STRIPE_WEBHOOK_VALUE="${stripe_webhook_secret}" just write-stripe-webhook-secret
    echo "Recorded the signing secret in .secrets.env (Parameters__stripe_webhook_secret)."

    if [[ "${key_vault_write_blocked}" == "true" ]]; then
        echo "" >&2
        echo "The Azure deployment itself succeeded and the new signing secret is safely recorded in" >&2
        echo ".secrets.env — nothing has been lost. It is NOT yet live: Key Vault still holds the" >&2
        echo "previous value, so Stripe signature verification will fail until it is installed." >&2
        echo "" >&2
        echo "Re-running 'just deploy {{environment}}' will NOT install it. Aspire only re-runs the" >&2
        echo "Key Vault deployment when the Bicep changes; the secret is a deployment parameter, not" >&2
        echo "template content, so an unchanged template short-circuits and the stored value is left" >&2
        echo "alone. That run would reach this step, find the destination already present, mint no" >&2
        echo "secret, and exit 0 over a Key Vault that is still stale." >&2
        echo "" >&2
        echo "Nor will re-running this recipe: the destination now exists, so Stripe reconciles it" >&2
        echo "and issues no secret, and there is nothing new to install." >&2
        echo "" >&2
        echo "Repair — get 'Key Vault Secrets Officer' on the vault, then push the recorded value:" >&2
        echo "    just install-stripe-webhook-secret {{environment}}" >&2
        echo "Confirm with:" >&2
        echo "    just verify-stripe-webhook-secret {{environment}}" >&2
        exit 2
    fi

    stripe_kv_value_file="${stripe_secret_dir}/kv-value"
    # printf, not echo: a trailing newline would become part of the stored secret.
    ( umask 077; printf '%s' "${stripe_webhook_secret}" > "${stripe_kv_value_file}" )
    set +e
    az keyvault secret set \
        --subscription "${Azure__SubscriptionId}" \
        --vault-name "${key_vault_name}" \
        --name kv-stripe-webhook-secret \
        --file "${stripe_kv_value_file}" \
        --encoding utf-8 \
        --output none
    stripe_kv_status=$?
    set -e
    rm -f -- "${stripe_kv_value_file}"
    if (( stripe_kv_status != 0 )); then
        echo "Error: writing kv-stripe-webhook-secret to '${key_vault_name}' failed (exit ${stripe_kv_status})." >&2
        echo "  The signing secret IS recorded in .secrets.env, so nothing is lost." >&2
        echo "  Do not reach for 'just deploy' here: Aspire re-runs the Key Vault deployment only" >&2
        echo "  when the Bicep changes, and the secret is a parameter rather than template content," >&2
        echo "  so an unchanged template leaves the stored value exactly as it is." >&2
        echo "  Retry the Key Vault write from the value already on record:" >&2
        echo "    just install-stripe-webhook-secret {{environment}}" >&2
        exit 2
    fi
    echo "Updated Key Vault secret 'kv-stripe-webhook-secret' in '${key_vault_name}'."

    # ---- activate ----------------------------------------------------------------------------
    # The Key Vault reference Aspire emits is versionless, so a new version is addressable by the
    # already-deployed template with no Bicep change. But a running container's environment block
    # is fixed at start, so the replica must be restarted to see it — the same reason the Redis
    # revision is restarted after its password is configured.
    shopsite_revision="$(az containerapp show \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --name shopsite \
        --query properties.latestRevisionName \
        --output tsv 2>/dev/null || true)"
    if [[ -z "${shopsite_revision}" ]]; then
        echo "Error: could not read the shopsite revision name; the secret is stored but not yet live." >&2
        echo "  Restart it manually, or re-run 'just deploy {{environment}}'." >&2
        exit 2
    fi

    echo "Restarting shopsite revision '${shopsite_revision}' to pick up the new signing secret."
    az containerapp revision restart \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --name shopsite \
        --revision "${shopsite_revision}" \
        --output none

    for shopsite_wait_attempt in {1..24}; do
        shopsite_state="$(az containerapp revision show \
            --subscription "${Azure__SubscriptionId}" \
            --resource-group "${Azure__ResourceGroup}" \
            --name shopsite \
            --revision "${shopsite_revision}" \
            --query '{health:properties.healthState,running:properties.runningState}' \
            --output tsv 2>/dev/null || true)"
        if [[ "${shopsite_state}" == $'Healthy\tRunning' ]]; then
            echo "shopsite revision '${shopsite_revision}' is healthy."
            break
        fi
        if (( shopsite_wait_attempt == 24 )); then
            echo "Timed out waiting for shopsite revision '${shopsite_revision}' to become healthy." >&2
            echo "  The signing secret is stored in both .secrets.env and Key Vault; only the restart" >&2
            echo "  is unconfirmed. Check the revision, then re-run 'just ensure-stripe-webhook {{environment}}'." >&2
            exit 1
        fi
        sleep 5
    done

    echo "Stripe event destination configured for ${webhook_url}."

# Delete and recreate the Stripe event destination, minting a fresh signing secret.
# Recovery only — see the note on ensure-stripe-webhook's recreate mode for the delivery gap this
# opens. Reach for it when Key Vault and Stripe have drifted and no record of the current secret
# exists, because no Stripe API can read a signing secret back after it is created.
#   just recreate-stripe-webhook             # recover azureprod
#   just recreate-stripe-webhook staging     # recover a named environment
# Keep the line below single: just uses the last comment line as the `just --list` description.

# Recreate the Stripe event destination and install a fresh signing secret
recreate-stripe-webhook environment="azureprod":
    just ensure-stripe-webhook "{{environment}}" recreate

# Prove the Stripe signing secret is the same in every store that holds one.
#
# Runs automatically at the end of `just deploy`, immediately after the Stripe step, on every path
# that step can succeed by. Run it by hand whenever you want to answer "would a webhook verify
# right now?" — it changes nothing.
#
# It compares *fingerprints*, never values: the first 12 lowercase hex characters of
# SHA-256(secret), which is precisely the function SecretFingerprint.Compute uses to stamp the
# Stripe destination's `withlove_secret_fingerprint` metadata. 48 bits of a digest over a
# high-entropy value is not a secret and is safe in a deploy log; the secrets themselves are held
# only in shell variables, never printed, never placed on a command line, never written to a file
# that outlives the recipe.
#
# Three stores hold a copy, and only two can be read back:
#   .secrets.env   the parameter a fresh provision resolves kv-stripe-webhook-secret from
#   Key Vault      what the running container app actually reads, and therefore signs against
#   Stripe         unreadable by construction — a signing secret is returned exactly once, in the
#                  create response, and neither v1 nor v2 has a retrieve or rotate API. The
#                  fingerprint metadata is the only witness Stripe can ever offer, and until this
#                  recipe existed nothing read it back.
#
# The three failure classes are deliberately not treated alike:
#   Key Vault and .secrets.env disagree  -> fail. This is the bug: every webhook rejected for a
#                                          bad signature, behind a deploy that exited 0.
#   Key Vault cannot be read             -> fail. An unverifiable state is not a verified one, and
#                                          a "pass" here would be a pass nobody checked.
#   Stripe's fingerprint contradicts     -> fail. Readable and disagreeing is real drift.
#   Stripe cannot be consulted           -> warn. A missing witness is not evidence of a problem,
#                                          and the gate above does not depend on it.
#   just verify-stripe-webhook-secret            # check azureprod
#   just verify-stripe-webhook-secret staging    # check a named environment
# Keep the line below single: just uses the last comment line as the `just --list` description.

# Check the Stripe signing secret agrees across .secrets.env, Key Vault and Stripe
verify-stripe-webhook-secret environment="azureprod":
    #!/usr/bin/env bash
    set -euo pipefail

    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

    : "${Azure__SubscriptionId:?Azure__SubscriptionId is required}"
    : "${Azure__ResourceGroup:?Azure__ResourceGroup is required}"

    # Byte-for-byte the same function as SecretFingerprint.Compute: lowercase hex SHA-256,
    # truncated to 12 characters, over the exact UTF-8 bytes of the secret. printf rather than
    # echo is load-bearing — one trailing newline changes the digest and would make every store
    # look like it disagreed with every other one.
    fingerprint_of() {
        local fingerprint_input="$1" fingerprint_digest
        if command -v shasum >/dev/null 2>&1; then
            fingerprint_digest="$(printf '%s' "${fingerprint_input}" | shasum -a 256 | awk '{print $1}')"
        elif command -v sha256sum >/dev/null 2>&1; then
            fingerprint_digest="$(printf '%s' "${fingerprint_input}" | sha256sum | awk '{print $1}')"
        else
            fingerprint_digest="$(printf '%s' "${fingerprint_input}" | openssl dgst -sha256 | awk '{print $NF}')"
        fi
        printf '%s' "${fingerprint_digest}" | tr '[:upper:]' '[:lower:]' | cut -c1-12
    }

    # ---- store 1: .secrets.env ----------------------------------------------------------------
    local_secret="${Parameters__stripe_webhook_secret:-}"
    if [[ -z "${local_secret}" ]]; then
        echo "UNVERIFIED — .secrets.env carries no Parameters__stripe_webhook_secret value." >&2
        echo "  There is nothing to compare Key Vault against, so the running app's signing secret" >&2
        echo "  is unaccounted for. Reconcile the destination first:" >&2
        echo "    just ensure-stripe-webhook {{environment}}" >&2
        exit 1
    fi
    if [[ "${local_secret}" == "whsec_placeholder" ]]; then
        echo "UNVERIFIED — .secrets.env still holds the seed placeholder 'whsec_placeholder'." >&2
        echo "  deploy seeds that value so the parameter resolves before the FQDN exists, and" >&2
        echo "  ensure-stripe-webhook replaces it only when it *creates* a destination. A" >&2
        echo "  destination that already existed issues no secret, so the real one is now recorded" >&2
        echo "  nowhere and no Stripe API can return it." >&2
        echo "  The only repair is to mint a fresh one:" >&2
        echo "    just recreate-stripe-webhook {{environment}}" >&2
        exit 1
    fi

    # ---- store 2: Key Vault -------------------------------------------------------------------
    # Read access and the secret's absence are different answers and must not be collapsed into
    # one `|| true`: that is how a permissions problem turns into a silent pass. Capture stderr and
    # classify it instead.
    stderr_capture="$(mktemp)"
    trap 'rm -f -- "${stderr_capture}"' EXIT

    candidate_vaults="$(az keyvault list \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --query '[].name' \
        --output tsv 2>/dev/null || true)"

    key_vault_name=""
    key_vault_secret=""
    key_vault_denied=""
    while IFS= read -r candidate_vault; do
        [[ -z "${candidate_vault}" ]] && continue
        set +e
        candidate_secret="$(az keyvault secret show \
            --subscription "${Azure__SubscriptionId}" \
            --vault-name "${candidate_vault}" \
            --name kv-stripe-webhook-secret \
            --query value \
            --output tsv 2>"${stderr_capture}")"
        candidate_status=$?
        set -e
        if (( candidate_status == 0 )); then
            candidate_secret="$(printf '%s' "${candidate_secret}" | tr -d '\r\n')"
            if [[ -n "${candidate_secret}" ]]; then
                key_vault_name="${candidate_vault}"
                key_vault_secret="${candidate_secret}"
                break
            fi
            continue
        fi
        if grep -qiE 'forbidden|unauthorized|not authorized|does not have secrets get permission|authorizationfailed' "${stderr_capture}"; then
            key_vault_denied="${candidate_vault}"
        fi
    done <<< "${candidate_vaults}"

    if [[ -z "${key_vault_name}" ]]; then
        echo "UNVERIFIED — the Key Vault copy of the signing secret could not be read." >&2
        if [[ -n "${key_vault_denied}" ]]; then
            echo "  Key Vault '${key_vault_denied}' refused the read for this principal." >&2
            echo "  Grant yourself 'Key Vault Secrets User' on that vault to check, or 'Key Vault" >&2
            echo "  Secrets Officer' if you also expect to repair a mismatch, then re-run:" >&2
            echo "    just verify-stripe-webhook-secret {{environment}}" >&2
        else
            echo "  No Key Vault in resource group '${Azure__ResourceGroup}' exposes a readable" >&2
            echo "  kv-stripe-webhook-secret to this principal. If the environment is deployed this" >&2
            echo "  is an access problem; if it is not deployed yet, deploy it first." >&2
        fi
        echo "" >&2
        echo "  This is reported as a failure on purpose. Passing here would be claiming a" >&2
        echo "  verification that did not happen, and the state it would hide — every Stripe" >&2
        echo "  delivery rejected for a bad signature — stays invisible until loyalty workflows" >&2
        echo "  quietly stop starting days later." >&2
        exit 1
    fi

    local_fingerprint="$(fingerprint_of "${local_secret}")"
    key_vault_fingerprint="$(fingerprint_of "${key_vault_secret}")"

    # ---- store 3: Stripe's own record ---------------------------------------------------------
    # Identity is the rule WebhookIdentity.Matches documents — withlove_managed == "true" AND
    # withlove_environment == the tag — and deliberately not the URL, which is neither unique nor
    # stable across a teardown. Read-only: a GET against the v1 list, the same API surface the
    # provisioning tool uses.
    stripe_fingerprint=""
    stripe_note=""
    if [[ -z "${Parameters__stripe_api_key:-}" ]]; then
        stripe_note="Parameters__stripe_api_key is not set, so Stripe was not consulted"
    else
        # The key reaches curl through a config file on stdin, never through argv, for the same
        # reason the provisioning tool refuses a --key flag: argv is world-readable via ps.
        set +e
        stripe_endpoints="$(printf 'user = "%s:"\n' "${Parameters__stripe_api_key}" \
            | curl --silent --show-error --fail --config - \
                   --get --data-urlencode 'limit=100' \
                   https://api.stripe.com/v1/webhook_endpoints 2>"${stderr_capture}")"
        stripe_status=$?
        set -e
        if (( stripe_status != 0 )); then
            stripe_note="the Stripe API could not be read (curl exit ${stripe_status}: $(tr '\n' ' ' < "${stderr_capture}"))"
        else
            stripe_fingerprints="$(jq -r --arg tag "{{environment}}" '
                [ .data[]?
                  | select((.metadata.withlove_managed? // "") == "true")
                  | select((.metadata.withlove_environment? // "") == $tag)
                  | (.metadata.withlove_secret_fingerprint? // "") ]
                | map(select(. != "")) | unique | .[]' <<< "${stripe_endpoints}")"
            if [[ -z "${stripe_fingerprints//[[:space:]]/}" ]]; then
                stripe_note="no destination tagged '{{environment}}' carries a withlove_secret_fingerprint"
            elif (( $(wc -l <<< "${stripe_fingerprints}") > 1 )); then
                stripe_note="several destinations tagged '{{environment}}' carry different fingerprints; resolve the duplicates before trusting this leg"
            else
                stripe_fingerprint="${stripe_fingerprints}"
            fi
        fi
    fi

    # ---- verdict ------------------------------------------------------------------------------
    echo "Stripe signing secret fingerprints (truncated SHA-256 digests, not secret values):"
    echo "  .secrets.env : ${local_fingerprint}"
    echo "  Key Vault    : ${key_vault_fingerprint}  (${key_vault_name})"
    if [[ -n "${stripe_fingerprint}" ]]; then
        echo "  Stripe       : ${stripe_fingerprint}"
    else
        echo "  Stripe       : not read — ${stripe_note}"
    fi

    if [[ "${local_fingerprint}" != "${key_vault_fingerprint}" ]]; then
        echo "" >&2
        echo "FAILED — Key Vault holds a different signing secret than .secrets.env." >&2
        echo "  The running app verifies signatures against the Key Vault value, so every Stripe" >&2
        echo "  delivery to this environment is being rejected right now." >&2
        echo "" >&2
        echo "  'just deploy {{environment}}' does NOT repair this. Aspire re-runs the Key Vault" >&2
        echo "  deployment only when the Bicep changes, and the secret is a deployment parameter" >&2
        echo "  rather than template content — an unchanged template short-circuits with \"Using" >&2
        echo "  existing deployment for keyvault\" and the stored value survives untouched." >&2
        echo "" >&2
        echo "  Repair, in order of preference:" >&2
        echo "    1. Install the recorded value and restart the app:" >&2
        echo "         just install-stripe-webhook-secret {{environment}}" >&2
        echo "       Requires 'Key Vault Secrets Officer' on '${key_vault_name}'." >&2
        echo "    2. By hand, keeping the value off argv:" >&2
        # Single quotes below are the point, not an oversight: these lines are a script for the
        # operator to run later, so the shell expansions in them must survive unexpanded. The
        # disable covers the whole group — SC2016 is right about what it sees and wrong about
        # what is wanted.
        # shellcheck disable=SC2016
        {
            echo '         set -a; . ./.secrets.env; set +a'
            echo '         whsec_file="$(mktemp)"; chmod 600 "${whsec_file}"'
            echo '         printf %s "${Parameters__stripe_webhook_secret}" > "${whsec_file}"'
            echo "         az keyvault secret set --subscription ${Azure__SubscriptionId} \\"
            echo "             --vault-name ${key_vault_name} --name kv-stripe-webhook-secret \\"
            echo '             --file "${whsec_file}" --encoding utf-8 --output none'
            echo '         rm -f -- "${whsec_file}"'
            echo "         az containerapp revision restart --subscription ${Azure__SubscriptionId} \\"
            echo "             --resource-group ${Azure__ResourceGroup} --name shopsite --revision <latest>"
        } >&2
        echo "    3. Start clean, accepting the teardown:" >&2
        echo "         just destroy && just deploy {{environment}}" >&2
        echo "" >&2
        echo "  Then re-check:  just verify-stripe-webhook-secret {{environment}}" >&2
        exit 1
    fi

    if [[ -n "${stripe_fingerprint}" && "${stripe_fingerprint}" != "${local_fingerprint}" ]]; then
        echo "" >&2
        echo "FAILED — Stripe signs with a secret neither .secrets.env nor Key Vault holds." >&2
        echo "  Those two agree with each other, so this is not a failed install; the destination" >&2
        echo "  was recreated somewhere this automation did not see it, most likely by hand in the" >&2
        echo "  Stripe Dashboard." >&2
        echo "  Nothing can read the current secret back — Stripe returns one exactly once — so the" >&2
        echo "  only repair is to mint a fresh one and install it:" >&2
        echo "    just recreate-stripe-webhook {{environment}}" >&2
        echo "  That opens a short delivery gap while the revision restarts; Stripe retries, so it" >&2
        echo "  is recoverable rather than lossy." >&2
        exit 1
    fi

    if [[ -z "${stripe_fingerprint}" ]]; then
        echo "Warning: two of the three stores were checked and agree — ${stripe_note}." >&2
        echo "  Stripe's own copy is unconfirmed. Compare the destination's" >&2
        echo "  withlove_secret_fingerprint metadata against ${local_fingerprint} by hand if you" >&2
        echo "  want the third leg." >&2
    fi

    echo "Verified: .secrets.env and Key Vault '${key_vault_name}' hold the same signing secret."

# Install the signing secret already recorded in .secrets.env into Key Vault, then restart the app.
#
# This is the repair for the one state `just deploy` cannot fix on its own: .secrets.env holds the
# right secret and Key Vault holds an older one. It happens when the Key Vault write inside
# ensure-stripe-webhook is denied or fails — that recipe records the secret locally first, by
# design, precisely so the value survives to be installed later.
#
# Re-running `just deploy` does not do this, and neither does re-running ensure-stripe-webhook:
# Aspire skips a Key Vault deployment whose Bicep has not changed, and Stripe issues no secret for
# a destination that already exists. This recipe writes the value directly.
#
# It mints nothing and touches Stripe not at all, so it is safe to re-run. It refuses to install a
# placeholder or a malformed value, and it verifies the result rather than assuming it.
#   just install-stripe-webhook-secret            # repair azureprod
#   just install-stripe-webhook-secret staging    # repair a named environment
# Keep the line below single: just uses the last comment line as the `just --list` description.

# Push the .secrets.env signing secret into Key Vault and restart shopsite
install-stripe-webhook-secret environment="azureprod":
    #!/usr/bin/env bash
    set -euo pipefail

    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

    : "${Azure__SubscriptionId:?Azure__SubscriptionId is required}"
    : "${Azure__ResourceGroup:?Azure__ResourceGroup is required}"

    # Same subscription/tenant guards as deploy, so this is safe to run standalone.
    active_account="$(az account show --query '{subscription:id,tenant:tenantId}' --output tsv)"
    IFS=$'\t' read -r active_subscription active_tenant <<< "${active_account}"
    if [[ "${active_subscription}" != "${Azure__SubscriptionId}" ]]; then
        echo "Azure CLI subscription '${active_subscription}' does not match configured subscription '${Azure__SubscriptionId}'." >&2
        exit 1
    fi
    if [[ -n "${Azure__TenantId:-}" && "${active_tenant}" != "${Azure__TenantId}" ]]; then
        echo "Azure CLI tenant '${active_tenant}' does not match configured tenant '${Azure__TenantId}'." >&2
        exit 1
    fi

    install_secret="${Parameters__stripe_webhook_secret:-}"
    if [[ -z "${install_secret}" ]]; then
        echo "Error: .secrets.env carries no Parameters__stripe_webhook_secret to install." >&2
        echo "  There is no secret on record. Reconcile the destination first:" >&2
        echo "    just ensure-stripe-webhook {{environment}}" >&2
        exit 1
    fi
    if [[ "${install_secret}" == "whsec_placeholder" ]]; then
        echo "Error: refusing to install the seed placeholder 'whsec_placeholder' into Key Vault." >&2
        echo "  It passes every shape check and verifies no signature — 'looks right, is wrong' is" >&2
        echo "  the exact state this automation exists to keep out of Key Vault." >&2
        echo "  Mint a real one:  just recreate-stripe-webhook {{environment}}" >&2
        exit 1
    fi

    # Same shape gate ensure-stripe-webhook applies before its own write; see the note there on why
    # the duplication is deliberate. The curly quotes are intentional — a hand-edited .secrets.env
    # is exactly how one gets in.
    install_secret_shape='^whsec_[^[:space:]"'"'"']+$'
    install_secret_malformed=false
    if ! [[ "${install_secret}" =~ ${install_secret_shape} ]]; then
        install_secret_malformed=true
    fi
    # shellcheck disable=SC1112
    case "${install_secret}" in
        *'“'*|*'”'*|*'‘'*|*'’'*) install_secret_malformed=true ;;
    esac
    if [[ "${install_secret_malformed}" == "true" ]]; then
        echo "Error: Parameters__stripe_webhook_secret in .secrets.env is not a well-formed signing secret." >&2
        echo "  Expected a 'whsec_' prefix with no whitespace and no quote characters, straight or" >&2
        echo "  smart. The value itself is not echoed." >&2
        exit 1
    fi

    # Locate the vault the same way ensure-stripe-webhook does: the one already exposing
    # kv-stripe-webhook-secret. Writing the secret into a vault nothing reads would be worse than
    # failing, so a vault that cannot be identified is a hard stop rather than a guess.
    key_vault_name=""
    candidate_vaults="$(az keyvault list \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --query '[].name' \
        --output tsv 2>/dev/null || true)"
    while IFS= read -r candidate_vault; do
        [[ -z "${candidate_vault}" ]] && continue
        if az keyvault secret show \
            --subscription "${Azure__SubscriptionId}" \
            --vault-name "${candidate_vault}" \
            --name kv-stripe-webhook-secret \
            --output none 2>/dev/null; then
            key_vault_name="${candidate_vault}"
            break
        fi
    done <<< "${candidate_vaults}"

    if [[ -z "${key_vault_name}" ]]; then
        echo "Error: no Key Vault in '${Azure__ResourceGroup}' exposes kv-stripe-webhook-secret to this principal." >&2
        echo "  Either the environment is not deployed, or this principal cannot read the vault." >&2
        echo "  'Key Vault Secrets Officer' on the vault covers both the read and the write below." >&2
        exit 1
    fi

    # --file, not --value: the secret never reaches argv. printf, not echo, because a trailing
    # newline would become part of the stored secret and break every signature check.
    install_value_dir="$(mktemp -d)"
    chmod 700 "${install_value_dir}"
    trap 'rm -rf -- "${install_value_dir}"' EXIT
    install_value_file="${install_value_dir}/kv-value"
    ( umask 077; printf '%s' "${install_secret}" > "${install_value_file}" )

    echo "Installing the recorded signing secret into Key Vault '${key_vault_name}'."
    set +e
    az keyvault secret set \
        --subscription "${Azure__SubscriptionId}" \
        --vault-name "${key_vault_name}" \
        --name kv-stripe-webhook-secret \
        --file "${install_value_file}" \
        --encoding utf-8 \
        --output none
    install_kv_status=$?
    set -e
    rm -f -- "${install_value_file}"
    if (( install_kv_status != 0 )); then
        echo "Error: writing kv-stripe-webhook-secret to '${key_vault_name}' failed (exit ${install_kv_status})." >&2
        echo "  Nothing was lost — the value is still recorded in .secrets.env. Grant this principal" >&2
        echo "  'Key Vault Secrets Officer' on the vault and re-run this recipe." >&2
        exit 1
    fi

    # The Key Vault reference Aspire emits is versionless, so the new version is addressable with no
    # Bicep change — but a running container's environment block is fixed at start, so the replica
    # has to restart to see it.
    shopsite_revision="$(az containerapp show \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --name shopsite \
        --query properties.latestRevisionName \
        --output tsv 2>/dev/null || true)"
    if [[ -z "${shopsite_revision}" ]]; then
        echo "Error: could not read the shopsite revision name; the secret is stored but not yet live." >&2
        echo "  Restart the shopsite revision by hand, then re-run:" >&2
        echo "    just verify-stripe-webhook-secret {{environment}}" >&2
        exit 1
    fi

    echo "Restarting shopsite revision '${shopsite_revision}' to pick up the installed secret."
    az containerapp revision restart \
        --subscription "${Azure__SubscriptionId}" \
        --resource-group "${Azure__ResourceGroup}" \
        --name shopsite \
        --revision "${shopsite_revision}" \
        --output none

    for install_wait_attempt in {1..24}; do
        shopsite_state="$(az containerapp revision show \
            --subscription "${Azure__SubscriptionId}" \
            --resource-group "${Azure__ResourceGroup}" \
            --name shopsite \
            --revision "${shopsite_revision}" \
            --query '{health:properties.healthState,running:properties.runningState}' \
            --output tsv 2>/dev/null || true)"
        if [[ "${shopsite_state}" == $'Healthy\tRunning' ]]; then
            echo "shopsite revision '${shopsite_revision}' is healthy."
            break
        fi
        if (( install_wait_attempt == 24 )); then
            echo "Timed out waiting for shopsite revision '${shopsite_revision}' to become healthy." >&2
            echo "  The secret is stored in both .secrets.env and Key Vault; only the restart is" >&2
            echo "  unconfirmed. Check the revision, then re-run:" >&2
            echo "    just verify-stripe-webhook-secret {{environment}}" >&2
            exit 1
        fi
        sleep 5
    done

    # Prove it rather than assume it: this recipe exists because a write that was believed to have
    # happened had not.
    just verify-stripe-webhook-secret "{{environment}}"

# Delete the Stripe event destinations this repo manages for an environment.
# Runs automatically as part of `just destroy`. Safe to re-run: a destination that is already
# gone is success, not an error.
# Also clears Parameters__stripe_webhook_secret in .secrets.env, because once the destination is
# deleted the stored value names a destination that no longer exists — and a dead secret still
# passes every shape check there is, which is exactly the "looks right, is wrong" state the
# automation exists to prevent. The parameter is only cleared when the delete actually succeeded.
#   just remove-stripe-webhook               # tear down azureprod's destinations
#   just remove-stripe-webhook staging       # tear down a named environment
# Keep the line below single: just uses the last comment line as the `just --list` description.

# Delete the Stripe event destinations managed for an environment
remove-stripe-webhook environment="azureprod":
    #!/usr/bin/env bash
    set -euo pipefail
    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

    : "${Parameters__stripe_api_key:?Parameters__stripe_api_key is required}"
    export Parameters__stripe_api_key

    # No Azure guard here on purpose: this touches Stripe only, and teardown must still work when
    # the resource group — and the reason to be logged into az at all — is already gone.
    if [[ ! -f "{{stripewebhooks}}" ]]; then
        echo "Error: {{stripewebhooks}} not found; cannot remove Stripe event destinations." >&2
        exit 1
    fi

    set +e
    dotnet run --project "{{stripewebhooks}}" -- remove --tag "{{environment}}"
    stripe_remove_status=$?
    set -e

    if (( stripe_remove_status != 0 )); then
        echo "Error: stripe-webhook remove failed with exit code ${stripe_remove_status}." >&2
        echo "  Parameters__stripe_webhook_secret was left as-is: if the destination still exists," >&2
        echo "  its signing secret is still the correct one and clearing it would strand the app." >&2
        exit "${stripe_remove_status}"
    fi

    WITHLOVE_STRIPE_WEBHOOK_VALUE="" just write-stripe-webhook-secret
    echo "Cleared Parameters__stripe_webhook_secret in .secrets.env; the next deploy will seed a placeholder."

# Destroy the Azure deployment and wait until the resource group is fully gone.
# aspire destroy returns as soon as ARM accepts the request; the actual teardown is
# async and the Container Apps environment can take more than 10 minutes to release.
# This recipe blocks until a direct resource-group existence check returns false so
# it's safe to redeploy immediately after.
# Also attempts to terminate the database setup workflow in Temporal Cloud. This only
# affects a workflow that is currently RUNNING — if it has already completed, terminate
# is a no-op (and AllowDuplicate in the code handles the re-run on next deploy anyway).
# The value here is stopping a mid-run workflow from burning 90 min of retries against
# a SQL Server that no longer exists.
# Also deletes the Stripe event destinations tagged for this environment, so teardown does not
# orphan them against Stripe's 16-destination cap. Best-effort: a Stripe failure warns loudly
# but does not block the Azure teardown.
#   just destroy                    # destroy azureprod, wait up to 3600s
#   just destroy staging            # destroy a named environment
#   just destroy azureprod 1800     # custom timeout in seconds
destroy environment="azureprod" timeout="3600":
    #!/usr/bin/env bash
    set -euo pipefail
    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

    : "${Azure__SubscriptionId:?Azure__SubscriptionId is required}"
    : "${Azure__ResourceGroup:?Azure__ResourceGroup is required}"

    active_account="$(az account show --query '{subscription:id,tenant:tenantId}' --output tsv)"
    IFS=$'\t' read -r active_subscription active_tenant <<< "${active_account}"
    if [[ "${active_subscription}" != "${Azure__SubscriptionId}" ]]; then
        echo "Azure CLI subscription '${active_subscription}' does not match configured subscription '${Azure__SubscriptionId}'." >&2
        exit 1
    fi
    if [[ -n "${Azure__TenantId:-}" && "${active_tenant}" != "${Azure__TenantId}" ]]; then
        echo "Azure CLI tenant '${active_tenant}' does not match configured tenant '${Azure__TenantId}'." >&2
        exit 1
    fi

    apphost_path="$(cd "$(dirname "{{apphost}}")" && pwd)/$(basename "{{apphost}}")"
    normalized_apphost_path="$(printf '%s' "${apphost_path}" | tr '[:upper:]' '[:lower:]')"
    if command -v shasum >/dev/null 2>&1; then
        apphost_sha="$(printf '%s' "${normalized_apphost_path}" | shasum -a 256 | awk '{print toupper($1)}')"
    elif command -v sha256sum >/dev/null 2>&1; then
        apphost_sha="$(printf '%s' "${normalized_apphost_path}" | sha256sum | awk '{print toupper($1)}')"
    else
        apphost_sha="$(printf '%s' "${normalized_apphost_path}" | openssl dgst -sha256 | awk '{print toupper($NF)}')"
    fi
    environment_name="$(printf '%s' "{{environment}}" | tr '[:upper:]' '[:lower:]')"
    deployment_state_file="${HOME}/.aspire/deployments/${apphost_sha}/${environment_name}.json"

    use_aspire_destroy=false
    if [[ -f "${deployment_state_file}" ]]; then
        cached_subscription="$(jq -r '.["Azure:SubscriptionId"] // empty' "${deployment_state_file}")"
        cached_resource_group="$(jq -r '.["Azure:ResourceGroup"] // empty' "${deployment_state_file}")"
        if [[ "${cached_subscription}" == "${Azure__SubscriptionId}" &&
              "${cached_resource_group}" == "${Azure__ResourceGroup}" ]]; then
            use_aspire_destroy=true
        else
            echo "Cached Aspire state targets '${cached_resource_group}' in '${cached_subscription}', not the configured resource group; using exact resource-group cleanup." >&2
        fi
    else
        echo "No Aspire state exists for '{{environment}}'; using exact resource-group cleanup if needed." >&2
    fi

    # Best-effort: stop a running db-setup workflow so it doesn't spin against a
    # deleted SQL Server. Silent no-op if the workflow is already completed or absent.
    temporal workflow terminate \
        --workflow-id withlove-db-setup \
        --namespace "${Parameters__temporal_namespace}" \
        --address "${Parameters__temporal_address}" \
        --api-key "${Parameters__temporal_api_key}" \
        --reason "Azure resources being destroyed" \
        2>/dev/null && echo "Terminated running db-setup workflow." || true

    # Delete the Stripe event destinations for this environment before anything Azure-side starts.
    # Early on purpose: the operator may ^C during the resource-group wait below, which can run for
    # an hour, so anything placed after it is unreliable.
    #
    # Best-effort, unlike deploy — a Stripe API hiccup must not block a teardown the operator is
    # probably running *because* something is already broken. But the warning is loud and names the
    # consequence: orphaned destinations accumulate against Stripe's 16-per-account cap, and after
    # a destroy/redeploy cycle the new FQDN differs from the old one, so the orphan is not reused.
    set +e
    just remove-stripe-webhook "{{environment}}"
    stripe_remove_status=$?
    set -e
    if (( stripe_remove_status != 0 )); then
        echo "WARNING: Stripe event destination teardown failed (exit ${stripe_remove_status})." >&2
        echo "  Azure teardown continues, but destinations tagged '{{environment}}' may still exist." >&2
        echo "  Finish later with: just remove-stripe-webhook {{environment}}" >&2
        echo "  Or delete them from the Stripe Dashboard under Developers > Event destinations." >&2
    fi

    if [[ "${use_aspire_destroy}" == "true" ]]; then
        set +e
        aspire destroy \
            --apphost "${apphost_path}" \
            --environment "{{environment}}" \
            --non-interactive \
            --yes
        destroy_status=$?
        set -e
        if (( destroy_status != 0 )); then
            echo "Aspire destroy failed with exit code ${destroy_status}; falling back to exact resource-group deletion." >&2
            use_aspire_destroy=false
        fi
    fi

    if [[ "${use_aspire_destroy}" != "true" ]]; then
        existing_resource_group_state="$(az group show \
            --subscription "${Azure__SubscriptionId}" \
            --name "${Azure__ResourceGroup}" \
            --query properties.provisioningState \
            --output tsv 2>/dev/null || true)"
        if [[ -n "${existing_resource_group_state}" && "${existing_resource_group_state}" != "Deleting" ]]; then
            az group delete \
                --subscription "${Azure__SubscriptionId}" \
                --name "${Azure__ResourceGroup}" \
                --yes \
                --no-wait
        elif [[ "${existing_resource_group_state}" == "Deleting" ]]; then
            echo "Resource group '${Azure__ResourceGroup}' is already deleting; continuing to wait."
        fi
    fi

    resource_group_exists="$(az group exists \
        --subscription "${Azure__SubscriptionId}" \
        --name "${Azure__ResourceGroup}" \
        --output tsv)"
    if [[ "${resource_group_exists}" == "true" ]]; then
        echo "Waiting for resource group '${Azure__ResourceGroup}' to finish deleting (timeout: {{timeout}}s)..."
        destroy_deadline=$((SECONDS + {{timeout}}))
        while true; do
            # Refresh Azure state before evaluating the deadline so a resource group that
            # disappeared during the previous sleep cannot be reported as a timeout.
            resource_group_exists="$(az group exists \
                --subscription "${Azure__SubscriptionId}" \
                --name "${Azure__ResourceGroup}" \
                --output tsv)"
            if [[ "${resource_group_exists}" != "true" ]]; then
                break
            fi

            if (( SECONDS >= destroy_deadline )); then
                resource_group_state="$(az group show \
                    --subscription "${Azure__SubscriptionId}" \
                    --name "${Azure__ResourceGroup}" \
                    --query properties.provisioningState \
                    --output tsv 2>/dev/null || true)"
                remaining_resource_count="$(az resource list \
                    --subscription "${Azure__SubscriptionId}" \
                    --resource-group "${Azure__ResourceGroup}" \
                    --query 'length(@)' \
                    --output tsv 2>/dev/null || printf 'unknown')"
                resource_lock_count="$(az lock list \
                    --subscription "${Azure__SubscriptionId}" \
                    --resource-group "${Azure__ResourceGroup}" \
                    --query 'length(@)' \
                    --output tsv 2>/dev/null || printf 'unknown')"

                echo "Timed out after {{timeout}}s waiting for resource group '${Azure__ResourceGroup}' to disappear." >&2
                echo "  Current state: ${resource_group_state:-unknown}" >&2
                echo "  Remaining resources: ${remaining_resource_count:-unknown}" >&2
                echo "  Resource locks: ${resource_lock_count:-unknown}" >&2
                echo "Azure deletion continues asynchronously; it is not safe to deploy yet." >&2
                echo "Re-run 'just destroy {{environment}} {{timeout}}' to resume waiting and complete Key Vault cleanup." >&2
                exit 1
            fi
            sleep 15
        done
        echo "Resource group fully deleted."
    else
        echo "Resource group '${Azure__ResourceGroup}' already gone."
    fi
    just purge-deleted-keyvaults
    rm -f -- "${deployment_state_file}"

# Run all tests
test:
    dotnet test {{solution}} --logger "console;verbosity=normal"

# Run unit tests only (fast, no Docker required)
test-unit:
    dotnet test {{solution}} \
        --filter "Category=Unit" \
        --logger "console;verbosity=normal"

# Run integration tests only (requires Docker for SQL Server + Redis)
test-integration:
    dotnet test {{solution}} \
        --filter "Category=Integration" \
        --logger "console;verbosity=normal"

# Run the package-backed chat integration lane (local Temporal only; no Docker/OpenAI)
test-chat-integration:
    dotnet test tests/WithLove.Workflows.Tests/WithLove.Workflows.Tests.csproj \
        --filter "Category=Integration&Feature=Chat" \
        --logger "console;verbosity=normal"
