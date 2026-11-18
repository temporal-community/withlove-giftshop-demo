[windows]
set shell := ["pwsh.exe", "-NoLogo", "-Command"]
[unix]
set shell := ["bash", "-c"]

solution        := "WithLoveShop.slnx"
configuration   := "Debug"
apphost         := "src/WithLove.AppHost/WithLove.AppHost.csproj"
stripewebhooks  := "tools/WithLove.StripeWebhooks/WithLove.StripeWebhooks.csproj"

# List available recipes
default:
    @just --list

# Remove all build output
clean:
    dotnet clean {{solution}} --configuration {{configuration}} --nologo -v q
    @echo "Clean complete."

# Build the solution
build:
    dotnet build {{solution}} --configuration {{configuration}}

# Purge deleted Key Vaults in the configured resource group
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

# Record the secret for fresh deployments; use the environment to keep it out of argv.
# Updating this file alone does not update Key Vault.
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

    # Keep the temporary secret file outside the repository.
    rewritten_secrets_env="$(mktemp)"
    trap 'rm -f -- "${rewritten_secrets_env}"' EXIT

    # Aspire reads the parameter from the exported environment.
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

    # Preserve the existing file ownership and permissions.
    cat -- "${rewritten_secrets_env}" > .secrets.env

# Deploy to Azure and reconcile the Stripe destination
deploy environment="azureprod" reset_state="false":
    @bash scripts/deploy.sh "{{environment}}" "{{reset_state}}" "{{apphost}}"

# List Azure deployment steps without provisioning
deploy-preview environment="azureprod":
    aspire deploy --apphost {{ apphost }} --environment "{{environment}}" --list-steps

# Reconcile the Stripe destination; recreate mode replaces it and briefly interrupts delivery
ensure-stripe-webhook environment="azureprod" mode="ensure":
    @bash scripts/ensure-stripe-webhook.sh "{{environment}}" "{{mode}}" "{{stripewebhooks}}"

# Recreate the Stripe destination and install a fresh secret
recreate-stripe-webhook environment="azureprod":
    just ensure-stripe-webhook "{{environment}}" recreate

# Compare secret fingerprints across the local file, Key Vault, and Stripe
verify-stripe-webhook-secret environment="azureprod":
    @bash scripts/verify-stripe-webhook-secret.sh "{{environment}}"

# Install the recorded secret in Key Vault and restart the app
install-stripe-webhook-secret environment="azureprod":
    @bash scripts/install-stripe-webhook-secret.sh "{{environment}}"

# Delete managed Stripe destinations and clear the recorded secret on success
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

    # Stripe cleanup must work even after Azure resources are gone.
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

# Destroy Azure resources, wait for deletion, and remove the Stripe destination
destroy environment="azureprod" timeout="3600":
    @bash scripts/destroy.sh "{{environment}}" "{{timeout}}" "{{apphost}}"

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
