#!/usr/bin/env bash
set -euo pipefail
readonly withlove_environment="$1"

if [[ ! -f .secrets.env ]]; then
    echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
    exit 1
fi
# shellcheck disable=SC1091
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
    echo "    just ensure-stripe-webhook ${withlove_environment}" >&2
    exit 1
fi
if [[ "${install_secret}" == "whsec_placeholder" ]]; then
    echo "Error: refusing to install the seed placeholder 'whsec_placeholder' into Key Vault." >&2
    echo "  It passes every shape check and verifies no signature — 'looks right, is wrong' is" >&2
    echo "  the exact state this automation exists to keep out of Key Vault." >&2
    echo "  Mint a real one:  just recreate-stripe-webhook ${withlove_environment}" >&2
    exit 1
fi

# Validate the secret, including smart quotes from manual edits.
# shellcheck disable=SC1112
install_secret_shape='^whsec_[^[:space:]"'"'"'“”‘’]+$'
if ! [[ "${install_secret}" =~ ${install_secret_shape} ]]; then
    echo "Error: Parameters__stripe_webhook_secret in .secrets.env is not a well-formed signing secret." >&2
    echo "  Expected a 'whsec_' prefix with no whitespace and no quote characters, straight or" >&2
    echo "  smart. The value itself is not echoed." >&2
    exit 1
fi

# Only write to the vault that already exposes the webhook secret.
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

# Keep the secret out of argv and avoid a trailing newline.
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

# Restart the app to load the new Key Vault secret.
shopsite_revision="$(az containerapp show \
    --subscription "${Azure__SubscriptionId}" \
    --resource-group "${Azure__ResourceGroup}" \
    --name shopsite \
    --query properties.latestRevisionName \
    --output tsv 2>/dev/null || true)"
if [[ -z "${shopsite_revision}" ]]; then
    echo "Error: could not read the shopsite revision name; the secret is stored but not yet live." >&2
    echo "  Restart the shopsite revision by hand, then re-run:" >&2
    echo "    just verify-stripe-webhook-secret ${withlove_environment}" >&2
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
        echo "    just verify-stripe-webhook-secret ${withlove_environment}" >&2
        exit 1
    fi
    sleep 5
done

# Verify the installed secret.
just verify-stripe-webhook-secret "${withlove_environment}"

