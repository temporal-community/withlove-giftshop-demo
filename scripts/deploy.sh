#!/usr/bin/env bash
set -euo pipefail
readonly withlove_environment="$1" withlove_reset_state="$2" withlove_apphost="$3"
if [[ ! -f .secrets.env ]]; then
    echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
    exit 1
fi
# shellcheck disable=SC1091
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

# The deployment needs a secret before it produces the URL needed to create one.
# Seed a placeholder after account checks; a new Stripe destination supplies the real secret.
if [[ -z "${Parameters__stripe_webhook_secret:-}" ]]; then
    echo "Seeding placeholder Parameters__stripe_webhook_secret; ensure-stripe-webhook will replace it."
    WITHLOVE_STRIPE_WEBHOOK_VALUE="whsec_placeholder" just write-stripe-webhook-secret
    Parameters__stripe_webhook_secret="whsec_placeholder"
fi
export Parameters__stripe_webhook_secret

apphost_path="$(cd "$(dirname "${withlove_apphost}")" && pwd)/$(basename "${withlove_apphost}")"
normalized_apphost_path="$(printf '%s' "${apphost_path}" | tr '[:upper:]' '[:lower:]')"
if command -v shasum >/dev/null 2>&1; then
    apphost_sha="$(printf '%s' "${normalized_apphost_path}" | shasum -a 256 | awk '{print toupper($1)}')"
elif command -v sha256sum >/dev/null 2>&1; then
    apphost_sha="$(printf '%s' "${normalized_apphost_path}" | sha256sum | awk '{print toupper($1)}')"
else
    apphost_sha="$(printf '%s' "${normalized_apphost_path}" | openssl dgst -sha256 | awk '{print toupper($NF)}')"
fi
environment_name="$(printf '%s' "${withlove_environment}" | tr '[:upper:]' '[:lower:]')"
deployment_state_file="${HOME}/.aspire/deployments/${apphost_sha}/${environment_name}.json"

if [[ -f "${deployment_state_file}" ]]; then
    cached_subscription="$(jq -r '.["Azure:SubscriptionId"] // empty' "${deployment_state_file}")"
    cached_resource_group="$(jq -r '.["Azure:ResourceGroup"] // empty' "${deployment_state_file}")"
    cached_location="$(jq -r '.["Azure:Location"] // empty' "${deployment_state_file}")"

    if [[ "${withlove_reset_state}" == "true" ||
          "${cached_subscription}" != "${Azure__SubscriptionId}" ||
          "${cached_resource_group}" != "${Azure__ResourceGroup}" ||
          "${cached_location}" != "${Azure__Location}" ]]; then
        echo "Removing stale Aspire state for '${withlove_environment}' at ${deployment_state_file}." >&2
        rm -f -- "${deployment_state_file}"
    fi
elif [[ "${withlove_reset_state}" == "true" ]]; then
    echo "No cached Aspire state exists for '${withlove_environment}'."
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
    --environment "${withlove_environment}"
    --non-interactive
)
if [[ "${withlove_reset_state}" == "true" ]]; then
    # Clearing the local file above is not sufficient for every Aspire CLI cache path.
    # Use the CLI's own environment-scoped cache reset as well.
    deploy_args+=(--clear-cache)
fi

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

        # Wait for Redis before creating a destination for this deployment.
        set +e
        just ensure-stripe-webhook "${withlove_environment}"
        stripe_webhook_status=$?
        set -e
        if (( stripe_webhook_status != 0 )); then
            echo "Deploy failed at the Stripe event destination step (exit ${stripe_webhook_status})." >&2
            echo "The Azure resources deployed successfully, but the webhook is not fully configured," >&2
            echo "so Stripe signature verification will fail. This is deliberately a red deploy:" >&2
            echo "'deployed but webhooks unconfigured' is the silent failure this step exists to" >&2
            echo "eliminate, and it would otherwise surface days later as loyalty workflows that" >&2
            echo "never start. Address the problem reported above; the repair it names is the one" >&2
            echo "to run — re-running 'just deploy ${withlove_environment}' by itself may not be it." >&2
            exit "${stripe_webhook_status}"
        fi

        # Verify every successful reconciliation, including existing destinations.
        set +e
        just verify-stripe-webhook-secret "${withlove_environment}"
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

    # shellcheck disable=SC2012
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

