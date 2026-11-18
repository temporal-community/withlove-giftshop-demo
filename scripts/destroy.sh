#!/usr/bin/env bash
set -euo pipefail
readonly withlove_environment="$1" withlove_timeout="$2" withlove_apphost="$3"
if [[ ! -f .secrets.env ]]; then
    echo "Error: .secrets.env not found. Copy .secrets.env.example and fill in your values." >&2
    exit 1
fi
# shellcheck disable=SC1091
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
    echo "No Aspire state exists for '${withlove_environment}'; using exact resource-group cleanup if needed." >&2
fi

# Stop a running setup workflow before deleting its database.
# shellcheck disable=SC2154
temporal workflow terminate \
    --workflow-id withlove-db-setup \
    --namespace "${Parameters__temporal_namespace}" \
    --address "${Parameters__temporal_address}" \
    --api-key "${Parameters__temporal_api_key}" \
    --reason "Azure resources being destroyed" \
    2>/dev/null && echo "Terminated running db-setup workflow." || true
# Attempt Stripe cleanup before the potentially long Azure teardown.
set +e
just remove-stripe-webhook "${withlove_environment}"
stripe_remove_status=$?
set -e
if (( stripe_remove_status != 0 )); then
    echo "WARNING: Stripe event destination teardown failed (exit ${stripe_remove_status})." >&2
    echo "  Azure teardown continues, but destinations tagged '${withlove_environment}' may still exist." >&2
    echo "  Finish later with: just remove-stripe-webhook ${withlove_environment}" >&2
    echo "  Or delete them from the Stripe Dashboard under Developers > Event destinations." >&2
fi

if [[ "${use_aspire_destroy}" == "true" ]]; then
    set +e
    aspire destroy \
        --apphost "${apphost_path}" \
        --environment "${withlove_environment}" \
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
    echo "Waiting for resource group '${Azure__ResourceGroup}' to finish deleting (timeout: ${withlove_timeout}s)..."
    # shellcheck disable=SC2004
    destroy_deadline=$((SECONDS + ${withlove_timeout}))
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

            echo "Timed out after ${withlove_timeout}s waiting for resource group '${Azure__ResourceGroup}' to disappear." >&2
            echo "  Current state: ${resource_group_state:-unknown}" >&2
            echo "  Remaining resources: ${remaining_resource_count:-unknown}" >&2
            echo "  Resource locks: ${resource_lock_count:-unknown}" >&2
            echo "Azure deletion continues asynchronously; it is not safe to deploy yet." >&2
            echo "Re-run 'just destroy ${withlove_environment} ${withlove_timeout}' to resume waiting and complete Key Vault cleanup." >&2
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

