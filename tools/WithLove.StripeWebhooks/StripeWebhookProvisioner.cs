using Stripe;

namespace WithLove.StripeWebhooks;

/// <summary>
/// The result of one command: an exit code plus exactly what should be printed.
/// </summary>
/// <remarks>
/// Nothing here is printed by the provisioner itself. Returning the text lets every branch,
/// including the failure branches, be asserted in a unit test without capturing console output —
/// and makes it structurally impossible for a secret to reach a log, since the secret never enters
/// one of these strings.
/// </remarks>
/// <param name="ExitCode">See <see cref="WithLove.StripeWebhooks.ExitCode"/>.</param>
/// <param name="Status">One line for stdout describing what happened. Never contains a secret.</param>
/// <param name="Errors">Lines for stderr: diagnosis, listings and remediation.</param>
internal sealed record ProvisionResult(int ExitCode, string? Status, IReadOnlyList<string> Errors)
{
    public static ProvisionResult Ok(int exitCode, string status) => new(exitCode, status, []);

    public static ProvisionResult Fail(int exitCode, params string[] errors) => new(exitCode, null, errors);
}

/// <summary>
/// Creates, reconciles and removes this application's Stripe webhook endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the v1 API and not v2.</b> The two APIs are two views over one object pool — an endpoint
/// created through v1 appears in a v2 list and vice versa, and <c>DELETE
/// /v1/webhook_endpoints/{id}</c> removes a destination that was created through v2 (those carry a
/// <c>we_</c> id prefix). So a v1-only tool still sees and manages anything a human created in the
/// Dashboard's v2 UI; that is the property that makes v1-only safe rather than merely convenient,
/// and it is worth stating because it otherwise reads as an oversight.
/// </para>
/// <para>
/// Beyond that: v1 needs no <c>Stripe-Version</c> header handling, where a v2 call fails without
/// one; v2's stricter URL validation buys nothing here because the URL is produced by
/// <c>az containerapp show</c>, not typed by a human; and v2's distinguishing feature is thin
/// events, while <c>src/WithLove.Web/Program.cs</c> maps <c>MapStripeWebhookHandler</c>, which is
/// the snapshot-payload handler.
/// </para>
/// </remarks>
internal sealed class StripeWebhookProvisioner
{
    private readonly WebhookEndpointService _endpoints;

    public StripeWebhookProvisioner(IStripeClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _endpoints = new WebhookEndpointService(client);
    }

    public Task<ProvisionResult> RunAsync(
        ToolCommand command,
        WebhookIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(identity);

        return command.Verb switch
        {
            CommandVerb.Ensure => EnsureAsync(command, identity, cancellationToken),
            CommandVerb.Remove => RemoveAsync(command, identity, cancellationToken),
            _ => RecreateAsync(command, identity, cancellationToken),
        };
    }

    // ─── ensure ───────────────────────────────────────────────────────────────

    private async Task<ProvisionResult> EnsureAsync(
        ToolCommand command,
        WebhookIdentity identity,
        CancellationToken cancellationToken)
    {
        var url = command.Url!;

        List<WebhookEndpoint> all;
        try
        {
            all = await ListAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException exception)
        {
            return StripeFailure("list webhook endpoints", exception, mutated: false);
        }

        var plan = EndpointPlanner.PlanEnsure(all, identity, url);

        switch (plan.Kind)
        {
            case PlanKind.Duplicates:
                return Duplicates(plan, identity);

            case PlanKind.CapReached:
                return CapReached(plan, identity);

            case PlanKind.AlreadyCurrent:
                return ProvisionResult.Ok(
                    command.DryRun ? ExitCode.Success : ExitCode.Reconciled,
                    command.DryRun
                        ? $"would make no changes: endpoint {plan.Target!.Id} already matches for tag '{identity.Tag}'"
                        : $"endpoint {plan.Target!.Id} already configured for tag '{identity.Tag}' at {url}; "
                          + "no changes, no secret written");

            case PlanKind.Reconcile:
                if (command.DryRun)
                {
                    return ProvisionResult.Ok(
                        ExitCode.Success,
                        $"would reconcile endpoint {plan.Target!.Id} for tag '{identity.Tag}': "
                        + string.Join("; ", plan.Changes));
                }

                try
                {
                    await ReconcileAsync(plan.Target!, identity, url, cancellationToken).ConfigureAwait(false);
                }
                catch (StripeException exception)
                {
                    return StripeFailure($"update webhook endpoint {plan.Target!.Id}", exception, mutated: false);
                }

                return ProvisionResult.Ok(
                    ExitCode.Reconciled,
                    $"endpoint {plan.Target!.Id} reconciled for tag '{identity.Tag}' at {url} "
                    + $"({string.Join("; ", plan.Changes)}); no secret written");

            case PlanKind.Create:
                if (command.DryRun)
                {
                    return ProvisionResult.Ok(
                        ExitCode.Success,
                        $"would create endpoint for tag '{identity.Tag}' at {url} with api_version "
                        + $"{StripeConfiguration.ApiVersion} and events {string.Join(',', EndpointPlanner.EnabledEvents)}");
                }

                return await CreateAndPersistAsync(command, identity, url, deleted: [], cancellationToken)
                    .ConfigureAwait(false);

            default:
                // Unreachable: PlanEnsure returns only the five kinds above. Throwing rather than
                // falling through to create means a new PlanKind cannot quietly acquire the
                // create-a-Stripe-endpoint behaviour by default.
                throw new InvalidOperationException($"Unexpected plan kind {plan.Kind} from PlanEnsure.");
        }
    }

    // ─── remove ───────────────────────────────────────────────────────────────

    private async Task<ProvisionResult> RemoveAsync(
        ToolCommand command,
        WebhookIdentity identity,
        CancellationToken cancellationToken)
    {
        List<WebhookEndpoint> all;
        try
        {
            all = await ListAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException exception)
        {
            return StripeFailure("list webhook endpoints", exception, mutated: false);
        }

        var plan = EndpointPlanner.PlanRemove(all, identity);

        // Teardown must be re-runnable. "Nothing of ours is here" is the desired end state, so
        // reporting it as an error would mean `just destroy` fails the second time it is run — at
        // exactly the moment the operator is already trying to clean up after something.
        if (plan.Kind == PlanKind.Absent)
        {
            return ProvisionResult.Ok(
                ExitCode.Success,
                $"no withlove-managed endpoint for tag '{identity.Tag}'; nothing to delete");
        }

        var ids = plan.Matches.Select(endpoint => endpoint.Id).ToList();

        if (command.DryRun)
            return ProvisionResult.Ok(ExitCode.Success, $"would delete {string.Join(", ", ids)} for tag '{identity.Tag}'");

        var deleted = new List<string>();
        var failed = new List<string>();
        foreach (var endpoint in plan.Matches)
        {
            try
            {
                await _endpoints.DeleteAsync(endpoint.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                deleted.Add(endpoint.Id);
            }
            catch (StripeException)
            {
                failed.Add(endpoint.Id);
            }
        }

        if (failed.Count > 0)
        {
            // Report the truth and let the caller decide. `just destroy` applies its own best-effort
            // posture; swallowing the failure here would hide an orphan that counts against the
            // 16-endpoint cap on the next deploy.
            return ProvisionResult.Fail(
                ExitCode.StripeApiError,
                $"Deleted {deleted.Count} endpoint(s) for tag '{identity.Tag}', but could not delete: "
                + string.Join(", ", failed),
                "Finish by hand with: stripe webhook_endpoints delete <id>");
        }

        return ProvisionResult.Ok(
            ExitCode.Success,
            $"deleted endpoint{(deleted.Count == 1 ? string.Empty : "s")} {string.Join(", ", deleted)} for tag '{identity.Tag}'");
    }

    // ─── recreate ─────────────────────────────────────────────────────────────

    private async Task<ProvisionResult> RecreateAsync(
        ToolCommand command,
        WebhookIdentity identity,
        CancellationToken cancellationToken)
    {
        var url = command.Url!;

        List<WebhookEndpoint> all;
        try
        {
            all = await ListAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException exception)
        {
            return StripeFailure("list webhook endpoints", exception, mutated: false);
        }

        // recreate is the repair path, so unlike ensure it is allowed to act on duplicates: every
        // endpoint carrying our identity goes, and one takes its place. That is precisely the state
        // ensure refuses to resolve on its own.
        var matches = all.Where(endpoint => identity.Matches(endpoint.Metadata)).ToList();

        if (all.Count - matches.Count >= EndpointPlanner.AccountEndpointCap)
        {
            return CapReached(
                new EndpointPlan(PlanKind.CapReached, null, matches, all.Count, []),
                identity);
        }

        if (command.DryRun)
        {
            var prefix = matches.Count == 0
                ? "would create"
                : $"would delete {string.Join(", ", matches.Select(endpoint => endpoint.Id))} then create";
            return ProvisionResult.Ok(
                ExitCode.Success,
                $"{prefix} an endpoint for tag '{identity.Tag}' at {url} with api_version {StripeConfiguration.ApiVersion}");
        }

        var deleted = new List<string>();
        foreach (var endpoint in matches)
        {
            try
            {
                await _endpoints.DeleteAsync(endpoint.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                deleted.Add(endpoint.Id);
            }
            catch (StripeException exception)
            {
                return StripeFailure($"delete webhook endpoint {endpoint.Id}", exception, mutated: deleted.Count > 0);
            }
        }

        return await CreateAndPersistAsync(command, identity, url, deleted, cancellationToken).ConfigureAwait(false);
    }

    // ─── create + persist ─────────────────────────────────────────────────────

    /// <summary>
    /// The only path that produces a signing secret, and therefore the only path with a genuinely
    /// dangerous intermediate state: between the create response and the file write, the secret
    /// exists nowhere but this process's memory. Every step after the create is ordered to shrink
    /// that window — the file is written before the fingerprint stamp and before the duplicate
    /// re-check, both of which are network calls that could otherwise strand it.
    /// </summary>
    private async Task<ProvisionResult> CreateAndPersistAsync(
        ToolCommand command,
        WebhookIdentity identity,
        string url,
        IReadOnlyList<string> deleted,
        CancellationToken cancellationToken)
    {
        WebhookEndpoint created;
        try
        {
            created = await _endpoints.CreateAsync(
                new WebhookEndpointCreateOptions
                {
                    Url = url,
                    EnabledEvents = [.. EndpointPlanner.EnabledEvents],
                    Description = identity.Description,
                    Metadata = identity.BuildMetadata(null),

                    // Not optional, and not cosmetic. Omitting api_version makes Stripe stamp the
                    // endpoint with the *account's* default version; the webhook handler in
                    // WithLove.Web compares every delivered event's api_version against
                    // StripeConfiguration.ApiVersion and throws on a mismatch, because
                    // StripeOptions.ThrowOnWebhookApiVersionMismatch defaults to true. Reading the
                    // value as a symbol is what keeps it correct across a Stripe.net upgrade — and
                    // is the reason this tool is .NET rather than curl and jq.
                    ApiVersion = StripeConfiguration.ApiVersion,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException exception)
        {
            return StripeFailure("create webhook endpoint", exception, mutated: deleted.Count > 0);
        }

        var secret = created.Secret;
        if (string.IsNullOrEmpty(secret))
        {
            return ProvisionResult.Fail(
                ExitCode.CreatedButNotPersisted,
                $"Created endpoint {created.Id}, but the create response carried no signing secret.",
                "A signing secret is readable exactly once, in the create response, and there is no "
                + "rotate API. This endpoint can never be used. Delete it and run `recreate`:",
                $"  stripe webhook_endpoints delete {created.Id}");
        }

        // The shape check runs after the write rather than before it on purpose. A malformed value
        // here would mean Stripe returned something unexpected, and withholding the file would
        // destroy the only copy of a value that can never be read again. Writing it and then
        // failing loudly keeps it recoverable by a human while still refusing to report success.
        try
        {
            SecretFile.Write(command.SecretOut!, secret);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ProvisionResult.Fail(
                ExitCode.CreatedButNotPersisted,
                $"Created endpoint {created.Id}, but could not write its signing secret to "
                + $"'{command.SecretOut}': {exception.Message}",
                "The secret is readable exactly once and is now lost. Delete the endpoint and run "
                + "`recreate` once the path is writable:",
                $"  stripe webhook_endpoints delete {created.Id}");
        }

        var shapeProblem = WebhookSecretShape.Validate(secret);
        if (shapeProblem is not null)
        {
            return ProvisionResult.Fail(
                ExitCode.CreatedButNotPersisted,
                $"Created endpoint {created.Id} and wrote its signing secret to '{command.SecretOut}', "
                + $"but {shapeProblem}.",
                "The value was written anyway so it is not lost, but it is not installable as-is. "
                + "Inspect the file before using it.");
        }

        var warnings = new List<string>();

        // Fingerprint stamping is a second call by necessity: the value being fingerprinted only
        // exists once the create has returned. Failing the whole run over it would be wrong — the
        // secret is already safely on disk, and the fingerprint is a drift signal, not a dependency.
        try
        {
            await _endpoints.UpdateAsync(
                created.Id,
                new WebhookEndpointUpdateOptions
                {
                    Metadata = identity.BuildMetadata(created.Metadata, SecretFingerprint.Compute(secret)),
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException exception)
        {
            warnings.Add(
                $"Warning: endpoint {created.Id} was created and its secret persisted, but the drift "
                + $"fingerprint could not be stamped ({Describe(exception)}). Drift detection will be "
                + "unavailable for this endpoint until the next `recreate`.");
        }

        // List-then-create is not atomic, so two concurrent runs against one tag can both observe
        // "absent" and both create. This detects that; it deliberately does not resolve it. One of
        // the duplicates holds the secret the other process just persisted, and deleting the wrong
        // one invalidates a secret that by then looks correct everywhere.
        try
        {
            var after = await ListAllAsync(cancellationToken).ConfigureAwait(false);
            var matches = after.Where(endpoint => identity.Matches(endpoint.Metadata)).Select(e => e.Id).ToList();
            if (matches.Count > 1)
            {
                string[] errors =
                [
                    $"Created endpoint {created.Id} and wrote its signing secret to '{command.SecretOut}', "
                    + $"but {matches.Count} endpoints now claim tag '{identity.Tag}': {string.Join(", ", matches)}.",
                    $"Two runs raced. The written secret belongs to {created.Id} only. Delete the others "
                    + "by hand and re-run:",
                    .. matches.Where(id => id != created.Id).Select(id => $"  stripe webhook_endpoints delete {id}"),
                ];

                return ProvisionResult.Fail(ExitCode.Blocked, errors);
            }
        }
        catch (StripeException exception)
        {
            warnings.Add(
                $"Warning: could not re-list endpoints to check for a concurrent duplicate "
                + $"({Describe(exception)}). The secret is persisted and {created.Id} is usable.");
        }

        var deletedNote = deleted.Count == 0
            ? string.Empty
            : $"replaced {string.Join(", ", deleted)}; ";

        return new ProvisionResult(
            ExitCode.Created,
            $"{deletedNote}created endpoint {created.Id} for tag '{identity.Tag}' at {url} "
            + $"(api_version {StripeConfiguration.ApiVersion}, events {string.Join(',', EndpointPlanner.EnabledEvents)}); "
            + $"signing secret written to {command.SecretOut}",
            warnings);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task ReconcileAsync(
        WebhookEndpoint target,
        WebhookIdentity identity,
        string url,
        CancellationToken cancellationToken)
    {
        var options = new WebhookEndpointUpdateOptions
        {
            Url = url,
            EnabledEvents = [.. EndpointPlanner.EnabledEvents],
            Description = identity.Description,

            // Merged over what is already there: a metadata update replaces the whole map, and the
            // secret fingerprint in it cannot be recomputed without the secret.
            Metadata = identity.BuildMetadata(target.Metadata),
        };

        if (EndpointPlanner.RequiresEnable(target))
            options.Disabled = false;

        await _endpoints.UpdateAsync(target.Id, options, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<WebhookEndpoint>> ListAllAsync(CancellationToken cancellationToken)
    {
        // One page is always enough: Stripe caps an account at 16 webhook endpoints, well under the
        // 100-item maximum page size.
        var page = await _endpoints.ListAsync(
            new WebhookEndpointListOptions { Limit = 100 },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return [.. page.Data];
    }

    private static ProvisionResult Duplicates(EndpointPlan plan, WebhookIdentity identity)
    {
        string[] errors =
        [
            $"{plan.Matches.Count} endpoints claim tag '{identity.Tag}'. Refusing to guess which one is "
            + "live, because each has a different signing secret and only one of them matches the "
            + "secret currently installed.",
            .. plan.Matches.Select(endpoint => $"  {endpoint.Id}  {endpoint.Url}  \"{endpoint.Description}\""),
            "Delete all but one in the Stripe Dashboard and re-run, or run `recreate` to replace them "
            + "all with a single fresh endpoint (which mints a new signing secret).",
        ];

        return ProvisionResult.Fail(ExitCode.Blocked, errors);
    }

    private static ProvisionResult CapReached(EndpointPlan plan, WebhookIdentity identity)
        => ProvisionResult.Fail(
            ExitCode.Blocked,
            $"This Stripe account already has {plan.TotalEndpoints} webhook endpoints "
            + $"(the limit is {EndpointPlanner.AccountEndpointCap}) and none of them carries tag "
            + $"'{identity.Tag}', so a new one cannot be created.",
            "Remove obsolete endpoints in the Stripe Dashboard and re-run. Endpoints tagged "
            + "withlove_managed=true but belonging to an environment you no longer deploy are the "
            + "likely candidates; endpoints without that tag belong to other integrations and must "
            + "not be removed on this tool's account.");

    private static ProvisionResult StripeFailure(string operation, StripeException exception, bool mutated)
        => ProvisionResult.Fail(
            ExitCode.StripeApiError,
            $"Stripe API call failed while attempting to {operation}: {Describe(exception)}",
            mutated
                ? "Some endpoints were already deleted before this failure; re-run `recreate` to reach a good state."
                : "No Stripe state was changed.");

    /// <summary>
    /// Renders a <see cref="StripeException"/> for a deploy log. Only the status code, error type
    /// and Stripe's own message are used — never the request body, which on the create path
    /// contains nothing secret but on no path is worth the risk of habituating.
    /// </summary>
    private static string Describe(StripeException exception)
        => $"{(int)exception.HttpStatusCode} {exception.StripeError?.Type ?? "error"}: "
           + (exception.StripeError?.Message ?? exception.Message);
}
