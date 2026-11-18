#!/usr/bin/env bash
set -euo pipefail

readonly withlove_environment="$1" withlove_mode="$2" withlove_stripewebhooks="$3"

if [[ "${withlove_mode}" != "ensure" && "${withlove_mode}" != "recreate" ]]; then
    echo "Error: mode must be 'ensure' or 'recreate', got '${withlove_mode}'." >&2
    exit 1
fi

if [[ ! -f .secrets.env ]]; then
    echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
    exit 1
fi
# shellcheck disable=SC1091
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

if [[ ! -f "${withlove_stripewebhooks}" ]]; then
    echo "Error: ${withlove_stripewebhooks} not found." >&2
    exit 1
fi

# Use the default route mapped by the web app.
shopsite_fqdn="$(az containerapp show \
    --subscription "${Azure__SubscriptionId}" \
    --resource-group "${Azure__ResourceGroup}" \
    --name shopsite \
    --query properties.configuration.ingress.fqdn \
    --output tsv 2>/dev/null || true)"
if [[ -z "${shopsite_fqdn}" ]]; then
    echo "Error: could not read the shopsite ingress FQDN from resource group '${Azure__ResourceGroup}'." >&2
    echo "  Deploy the environment first: just deploy ${withlove_environment}" >&2
    exit 1
fi
webhook_url="https://${shopsite_fqdn}/stripe/webhook"

# Check operator access before creating a Stripe destination.
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

# Keep the probe secret: soft deletion would prevent reusing its name.
kv_probe_name=withlove-stripe-webhook-preflight

# Capture failures; the probe value is not sensitive.
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

# Distinguish authorization failures from inconclusive errors.
key_vault_write_denied=false
if [[ -n "${key_vault_probe_error}" ]] && \
   grep -qiE 'forbidden|accessdenied|authorizationfailed|unauthorized|does not have secrets [a-z]+ permission' \
       <<< "${key_vault_probe_error}"; then
    key_vault_write_denied=true
fi

# An inconclusive probe still permits a later Key Vault write.
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
        echo "    just install-stripe-webhook-secret ${withlove_environment}" >&2
        echo "  (direct 'az keyvault secret set' plus a revision restart), or a full" >&2
        echo "    just destroy ${withlove_environment} && just deploy ${withlove_environment}" >&2
    fi
else
    echo "Preflight: this principal can write secrets to Key Vault '${key_vault_name}'."
fi

# Keep the generated secret in a private file, never stdout.
stripe_secret_dir="$(mktemp -d)"
chmod 700 "${stripe_secret_dir}"
trap 'rm -rf -- "${stripe_secret_dir}"' EXIT
stripe_secret_out="${stripe_secret_dir}/signing-secret"

echo "Reconciling the Stripe event destination for ${webhook_url} (mode: ${withlove_mode})."
set +e
dotnet run --project "${withlove_stripewebhooks}" -- \
    "${withlove_mode}" \
    --url "${webhook_url}" \
    --tag "${withlove_environment}" \
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
        echo "Error: stripe-webhook ${withlove_mode} failed with exit code ${stripe_tool_status}." >&2
        exit "${stripe_tool_status}"
        ;;
esac

if [[ ! -s "${stripe_secret_out}" ]]; then
    echo "Error: the tool reported a new destination (exit ${stripe_tool_status}) but wrote no secret." >&2
    echo "  A Stripe destination may now exist whose signing secret is recorded nowhere, and no" >&2
    echo "  Stripe API can read it back. Recover with:" >&2
    echo "    just ensure-stripe-webhook ${withlove_environment} recreate" >&2
    exit 1
fi

stripe_webhook_secret="$(tr -d '\r\n' < "${stripe_secret_out}")"
rm -f -- "${stripe_secret_out}"

# Validate the outgoing secret separately from the deployment parameter.
# shellcheck disable=SC1112
stripe_secret_shape='^whsec_[^[:space:]"'"'"'“”‘’]+$'
if ! [[ "${stripe_webhook_secret}" =~ ${stripe_secret_shape} ]]; then
    echo "Error: the value written to --secret-out is not a well-formed Stripe signing secret." >&2
    echo "  Expected a 'whsec_' prefix with no whitespace and no quote characters." >&2
    echo "  The value itself is not echoed. Recover with:" >&2
    echo "    just ensure-stripe-webhook ${withlove_environment} recreate" >&2
    exit 1
fi

# Save locally first: Stripe cannot return the secret again if Key Vault fails.
WITHLOVE_STRIPE_WEBHOOK_VALUE="${stripe_webhook_secret}" just write-stripe-webhook-secret
echo "Recorded the signing secret in .secrets.env (Parameters__stripe_webhook_secret)."

if [[ "${key_vault_write_blocked}" == "true" ]]; then
    echo "" >&2
    echo "The Azure deployment itself succeeded and the new signing secret is safely recorded in" >&2
    echo ".secrets.env — nothing has been lost. It is NOT yet live: Key Vault still holds the" >&2
    echo "previous value, so Stripe signature verification will fail until it is installed." >&2
    echo "" >&2
    echo "Re-running 'just deploy ${withlove_environment}' will NOT install it. Aspire only re-runs the" >&2
    echo "Key Vault deployment when the Bicep changes; the secret is a deployment parameter, not" >&2
    echo "template content, so an unchanged template short-circuits and the stored value is left" >&2
    echo "alone. That run would reach this step, find the destination already present, mint no" >&2
    echo "secret, and exit 0 over a Key Vault that is still stale." >&2
    echo "" >&2
    echo "Nor will re-running this recipe: the destination now exists, so Stripe reconciles it" >&2
    echo "and issues no secret, and there is nothing new to install." >&2
    echo "" >&2
    echo "Repair — get 'Key Vault Secrets Officer' on the vault, then push the recorded value:" >&2
    echo "    just install-stripe-webhook-secret ${withlove_environment}" >&2
    echo "Confirm with:" >&2
    echo "    just verify-stripe-webhook-secret ${withlove_environment}" >&2
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
    echo "    just install-stripe-webhook-secret ${withlove_environment}" >&2
    exit 2
fi
echo "Updated Key Vault secret 'kv-stripe-webhook-secret' in '${key_vault_name}'."

# Restart the app to load the new Key Vault secret.
shopsite_revision="$(az containerapp show \
    --subscription "${Azure__SubscriptionId}" \
    --resource-group "${Azure__ResourceGroup}" \
    --name shopsite \
    --query properties.latestRevisionName \
    --output tsv 2>/dev/null || true)"
if [[ -z "${shopsite_revision}" ]]; then
    echo "Error: could not read the shopsite revision name; the secret is stored but not yet live." >&2
    echo "  Restart it manually, or re-run 'just deploy ${withlove_environment}'." >&2
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
        echo "  is unconfirmed. Check the revision, then re-run 'just ensure-stripe-webhook ${withlove_environment}'." >&2
        exit 1
    fi
    sleep 5
done

echo "Stripe event destination configured for ${webhook_url}."

