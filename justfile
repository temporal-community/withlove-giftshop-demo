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

# Start the full stack with Phoenix; pass --capture to export AI payload content
[arg("capture", long="capture", value="true")]
run capture="false": (run-phoenix capture)

# Start with Phoenix; pass --capture to export AI payload content
[arg("Telemetry__CaptureAiContent", long="capture", value="true")]
[env("Arize__TraceDestination", "Phoenix")]
run-phoenix $Telemetry__CaptureAiContent="false":
    aspire start --apphost {{ apphost }}

# Start with AX; pass --capture to export AI payload content
[arg("Telemetry__CaptureAiContent", long="capture", value="true")]
[env("Arize__TraceDestination", "Ax")]
run-ax $Telemetry__CaptureAiContent="false":
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
# stripe-webhook-secret parameter from, and ConfigureAzureDependencies rewrites the Key Vault
# secret from that parameter on *every* deploy. A secret installed only into Key Vault is
# therefore erased by the next deploy. See the design's §1.3 / §3.3.
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
#   just deploy staging              # deploy to a different environment
#   just deploy-clean                # deploy with fresh Aspire state
deploy environment="azureprod" reset_state="false":
    #!/usr/bin/env bash
    set -euo pipefail
    if [[ ! -f .secrets.env ]]; then
        echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
        exit 1
    fi
    source .secrets.env

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
                echo "never start. Address the problem reported above and re-run 'just deploy {{environment}}'." >&2
                exit "${stripe_webhook_status}"
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
#   just deploy-clean staging   # deploy to staging with fresh state
deploy-clean environment="azureprod":
    just deploy "{{environment}}" true

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

    key_vault_writable=false
    if [[ -n "${key_vault_name}" ]]; then
        # 'preflight' is not a secret, so passing it on argv here is fine.
        if az keyvault secret set \
            --subscription "${Azure__SubscriptionId}" \
            --vault-name "${key_vault_name}" \
            --name withlove-stripe-webhook-preflight \
            --value preflight \
            --output none 2>/dev/null; then
            key_vault_writable=true
            az keyvault secret delete \
                --subscription "${Azure__SubscriptionId}" \
                --vault-name "${key_vault_name}" \
                --name withlove-stripe-webhook-preflight \
                --output none 2>/dev/null || true
        fi
    fi

    if [[ "${key_vault_writable}" != "true" ]]; then
        if [[ -z "${key_vault_name}" ]]; then
            echo "Preflight: no Key Vault in '${Azure__ResourceGroup}' exposes kv-stripe-webhook-secret to this principal." >&2
        else
            echo "Preflight: this principal cannot write secrets to Key Vault '${key_vault_name}'." >&2
        fi
        echo "  If a new signing secret is minted it will be recorded in .secrets.env and installed" >&2
        echo "  by the next 'just deploy' rather than written to Key Vault directly." >&2
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
    # way the likely failure still leaves the secret where the next deploy will read it. Reversed,
    # the likely failure loses a secret that no API can return, forcing a recreate.
    WITHLOVE_STRIPE_WEBHOOK_VALUE="${stripe_webhook_secret}" just write-stripe-webhook-secret
    echo "Recorded the signing secret in .secrets.env (Parameters__stripe_webhook_secret)."

    if [[ "${key_vault_writable}" != "true" ]]; then
        echo "" >&2
        echo "The Azure deployment itself succeeded and the new signing secret is safely recorded in" >&2
        echo ".secrets.env — nothing has been lost. It is NOT yet live: Key Vault still holds the" >&2
        echo "previous value, so Stripe signature verification will fail until it is installed." >&2
        echo "" >&2
        echo "Finish with:  just deploy {{environment}}" >&2
        echo "  Aspire rewrites kv-stripe-webhook-secret from .secrets.env on every deploy and the" >&2
        echo "  new revision starts with it. That run reaches this step again, finds the destination" >&2
        echo "  already present, and makes no further Stripe changes." >&2
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
        echo "  Re-run 'just deploy {{environment}}' to let Aspire install it from there." >&2
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
