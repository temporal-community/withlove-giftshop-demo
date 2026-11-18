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

# Match SecretFingerprint.Compute; printf avoids adding a newline.
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

# Local secret
local_secret="${Parameters__stripe_webhook_secret:-}"
if [[ -z "${local_secret}" ]]; then
    echo "UNVERIFIED — .secrets.env carries no Parameters__stripe_webhook_secret value." >&2
    echo "  There is nothing to compare Key Vault against, so the running app's signing secret" >&2
    echo "  is unaccounted for. Reconcile the destination first:" >&2
    echo "    just ensure-stripe-webhook ${withlove_environment}" >&2
    exit 1
fi
if [[ "${local_secret}" == "whsec_placeholder" ]]; then
    echo "UNVERIFIED — .secrets.env still holds the seed placeholder 'whsec_placeholder'." >&2
    echo "  deploy seeds that value so the parameter resolves before the FQDN exists, and" >&2
    echo "  ensure-stripe-webhook replaces it only when it *creates* a destination. A" >&2
    echo "  destination that already existed issues no secret, so the real one is now recorded" >&2
    echo "  nowhere and no Stripe API can return it." >&2
    echo "  The only repair is to mint a fresh one:" >&2
    echo "    just recreate-stripe-webhook ${withlove_environment}" >&2
    exit 1
fi

# Distinguish a missing secret from a Key Vault access failure.
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
        echo "    just verify-stripe-webhook-secret ${withlove_environment}" >&2
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

# Match Stripe destinations by managed environment metadata, not URL.
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
        stripe_fingerprints="$(jq -r --arg tag "${withlove_environment}" '
            [ .data[]?
              | select((.metadata.withlove_managed? // "") == "true")
              | select((.metadata.withlove_environment? // "") == $tag)
              | (.metadata.withlove_secret_fingerprint? // "") ]
            | map(select(. != "")) | unique | .[]' <<< "${stripe_endpoints}")"
        if [[ -z "${stripe_fingerprints//[[:space:]]/}" ]]; then
            stripe_note="no destination tagged '${withlove_environment}' carries a withlove_secret_fingerprint"
        elif (( $(wc -l <<< "${stripe_fingerprints}") > 1 )); then
            stripe_note="several destinations tagged '${withlove_environment}' carry different fingerprints; resolve the duplicates before trusting this leg"
        else
            stripe_fingerprint="${stripe_fingerprints}"
        fi
    fi
fi

# Compare fingerprints
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
    echo "  'just deploy ${withlove_environment}' does NOT repair this. Aspire re-runs the Key Vault" >&2
    echo "  deployment only when the Bicep changes, and the secret is a deployment parameter" >&2
    echo "  rather than template content — an unchanged template short-circuits with \"Using" >&2
    echo "  existing deployment for keyvault\" and the stored value survives untouched." >&2
    echo "" >&2
    echo "  Repair, in order of preference:" >&2
    echo "    1. Install the recorded value and restart the app:" >&2
    echo "         just install-stripe-webhook-secret ${withlove_environment}" >&2
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
    echo "         just destroy && just deploy ${withlove_environment}" >&2
    echo "" >&2
    echo "  Then re-check:  just verify-stripe-webhook-secret ${withlove_environment}" >&2
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
    echo "    just recreate-stripe-webhook ${withlove_environment}" >&2
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

