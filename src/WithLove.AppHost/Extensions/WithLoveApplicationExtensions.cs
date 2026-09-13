using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting;
using Azure.Provisioning.KeyVault;
using Microsoft.Extensions.Configuration;
using TemporalCommunity.Aspire.Hosting;
using Temporalio.Common;

namespace WithLove.AppHost.Extensions;

internal static partial class WithLoveApplicationExtensions
{
    private const string ProductsDatabaseResourceName = "productsDatabase";
    private const string OpenInferenceProjectName = "withlove-giftshop";
    private const string ArizeTraceDestinationConfigurationKey = "Arize:TraceDestination";
    private const string CaptureAiContentConfigurationKey = "Telemetry:CaptureAiContent";
    private const string CaptureAiContentEnvironmentVariable = "Telemetry__CaptureAiContent";
    private const string AspireGenAiCaptureMessageContentEnvironmentVariable =
        "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
    private const string StripeWebhookSecretParameterName = "stripe-webhook-secret";
    private const string StripeWebhookSecretPrefix = "whsec_";

#pragma warning disable ASPIREPIPELINES001
    internal static readonly string[] StripeWebhookSecretValidationRequiredBy =
    [
        WellKnownPipelineSteps.PublishPrereq,
        WellKnownPipelineSteps.DeployPrereq,
    ];
#pragma warning restore ASPIREPIPELINES001

    public static void AddWithLoveApplication(
        this IDistributedApplicationBuilder builder,
        bool isPublishMode,
        bool isTestMode)
    {
        var parameters = AddParameters(builder, isPublishMode);
        var infrastructure = AddInfrastructure(builder, isPublishMode, isTestMode, parameters);
        var productsApi = AddProductsApi(builder, infrastructure, parameters);

        if (!isPublishMode)
            ConfigureScalarEndpoint(productsApi);

        // Integration tests exercise the Products API with disposable SQL Server and Redis containers.
        if (isTestMode)
            return;

        var application = AddFullApplication(builder, infrastructure, parameters, productsApi);
        var captureAiContent = ResolveCaptureAiContent(
            builder.Configuration[CaptureAiContentConfigurationKey]);
        ConfigureAiContentCapture(application, captureAiContent);
        var useAx = ResolveUseAxTraceDestination(
            builder.Configuration[ArizeTraceDestinationConfigurationKey],
            isPublishMode);

        if (isPublishMode)
        {
            ConfigureAzureDependencies(builder, application, infrastructure, parameters);
        }
        else
        {
            ConfigureLocalDependencies(builder, application, parameters);
        }

        ConfigureTraceDestination(builder, application, useAx);

        ConfigureWorkflowServer(application.WorkflowServer);
        ConfigureShopSite(application.ShopSite);
    }

    /// <summary>
    /// Resolves the Arize trace backend. Local runs default to Phoenix and published applications
    /// default to AX; either mode can be overridden explicitly for model and deployment testing.
    /// </summary>
    internal static bool ResolveUseAxTraceDestination(string? configuredDestination, bool isPublishMode)
    {
        if (string.IsNullOrWhiteSpace(configuredDestination))
            return isPublishMode;
        if (configuredDestination.Equals("Ax", StringComparison.OrdinalIgnoreCase))
            return true;
        if (configuredDestination.Equals("Phoenix", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new InvalidOperationException(
            $"Configuration '{ArizeTraceDestinationConfigurationKey}' must be 'Ax' or 'Phoenix'.");
    }

    /// <summary>
    /// Resolves the application-level authorization for exporting AI payload content. Capture is
    /// disabled when the setting is absent and malformed values fail during AppHost startup.
    /// </summary>
    internal static bool ResolveCaptureAiContent(string? configuredValue)
    {
        if (configuredValue is null)
            return false;
        if (configuredValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (configuredValue.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new InvalidOperationException(
            $"Configuration '{CaptureAiContentConfigurationKey}' must be 'true' or 'false'.");
    }

    private static WithLoveParameters AddParameters(IDistributedApplicationBuilder builder, bool isPublishMode)
        => new(
            builder.AddParameter("openai-api-key", secret: true),
            builder.AddParameter("stripe-api-key", secret: true),
            builder.AddParameter("stripe-public-key", secret: true),
            builder.AddParameter("redis-password", secret: true),
            builder.AddParameter("temporal-address"),
            builder.AddParameter("temporal-namespace"),
            builder.AddParameter("temporal-api-key", secret: true),
            // Publish-only. Locally, `stripe listen` mints a fresh signing secret per session and
            // ConfigureLocalDependencies supplies it through the CLI container's
            // Stripe__Default__WebhookSecret. Declaring the parameter in run mode would ask every
            // developer to maintain a value that no local code path reads — and a credential no
            // local path exercises is a credential whose corruption first surfaces in Azure, as a
            // silent Stripe signature mismatch. Declare it only where it is consumed.
            StripeWebhookSecret: isPublishMode ? AddStripeWebhookSecret(builder) : null);

    /// <summary>
    /// Declares the Azure-only Stripe webhook signing secret and rejects a malformed value while
    /// publishing, before it can be written into a Key Vault secret.
    /// </summary>
    private static IResourceBuilder<ParameterResource> AddStripeWebhookSecret(IDistributedApplicationBuilder builder)
    {
        var parameter = builder.AddParameter(StripeWebhookSecretParameterName, secret: true);

        // A named pipeline step rather than a BeforePublishEvent subscriber: the 13.5.3 pipeline
        // never raises BeforePublishEvent, so a subscriber there is silently never invoked. Ordering
        // uses the public WellKnownPipelineSteps constants — after parameter values are resolved,
        // before any publish work — so a malformed value fails the `aspire publish`/`aspire deploy`
        // pipeline with a named step and no Bicep is written. That beats a well-formed-looking
        // deployment that fails Stripe signature verification at runtime with no obvious cause.
        // WellKnownPipelineSteps is still marked evaluation-only in 13.5.3. The alternative is
        // hard-coding "process-parameters"/"publish-prereq" as literals, which breaks just as
        // easily and without a compiler diagnostic to warn you. Scoped like the AZPROVISION001
        // pragma in WithLoveApplicationExtensions.ContainerApps.cs.
#pragma warning disable ASPIREPIPELINES001
        parameter.WithPipelineStepFactory(
            "validate-stripe-webhook-secret",
            async context =>
            {
                var value = await parameter.Resource
                    .GetValueAsync(context.CancellationToken)
                    .ConfigureAwait(false);

                ValidateStripeWebhookSecret(parameter.Resource.Name, value);
            },
            dependsOn: [WellKnownPipelineSteps.ProcessParameters],
            requiredBy: StripeWebhookSecretValidationRequiredBy,
            description: "Rejects a malformed Stripe webhook signing secret before it reaches Key Vault.");
#pragma warning restore ASPIREPIPELINES001

        return parameter;
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is not shaped like a Stripe webhook signing secret.
    /// The value is never included in the exception message — publish output lands in CI logs and
    /// issue reports.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> only so it can be tested. It is a pure function
    /// reached at publish time from a pipeline step, and booting the AppHost to exercise it would
    /// need real secrets — see <c>StripeWebhookSecretValidationTests</c> and the
    /// <c>InternalsVisibleTo</c> entry in WithLove.AppHost.csproj.
    /// </remarks>
    internal static void ValidateStripeWebhookSecret(string parameterName, string? value)
    {
        // Ordered most-specific first: a smart-quote-wrapped value also fails the prefix check, but
        // "wrapped in quotes" is the message that actually tells someone what to fix. Shells and
        // note-taking apps are how the wrapping gets there in the first place.
        var reason = value switch
        {
            null or "" => "the value is empty",
            _ when value.Trim() != value => "the value has leading or trailing whitespace",
            _ when IsQuoteWrapped(value) => "the value is wrapped in quote characters (straight or smart quotes)",
            _ when !value.StartsWith(StripeWebhookSecretPrefix, StringComparison.Ordinal)
                => $"the value does not start with '{StripeWebhookSecretPrefix}'",
            _ when value.Length == StripeWebhookSecretPrefix.Length
                => $"the value is only the '{StripeWebhookSecretPrefix}' prefix",
            _ => null,
        };

        if (reason is null)
            return;

        throw new DistributedApplicationException(
            $"Parameter '{parameterName}' is not a valid Stripe webhook signing secret: {reason}. " +
            $"Expected a single-line value beginning with '{StripeWebhookSecretPrefix}' and no surrounding " +
            "quotes or whitespace, as printed by `stripe listen --print-secret` or shown on the Stripe " +
            $"Dashboard webhook endpoint. Observed length: {value?.Length ?? 0} characters. " +
            $"Fix it in .secrets.env: Parameters__{parameterName.Replace('-', '_')}=\"whsec_...\" — " +
            "or remove a manually supplied value when using `just deploy`, which seeds its bootstrap " +
            "value automatically. Deployment inputs come from .secrets.env rather than the " +
            "`aspire secret set` user-secret store; see docs/azure-deployment.md. The value itself " +
            "is deliberately not shown.");
    }

    private static bool IsQuoteWrapped(string value)
        // U+2018/U+2019/U+201C/U+201D are the smart quotes editors and chat clients substitute for
        // straight quotes; a pasted secret carrying them is longer than the real secret and will
        // never verify.
        => value.Length > 0
           && (IsQuote(value[0]) || IsQuote(value[^1]));

    private static bool IsQuote(char candidate)
        => candidate is '"' or '\'' or '`' or '‘' or '’' or '“' or '”';

    private static WithLoveInfrastructure AddInfrastructure(
        IDistributedApplicationBuilder builder,
        bool isPublishMode,
        bool isTestMode,
        WithLoveParameters parameters)
    {
        IResourceBuilder<IResourceWithConnectionString> productsDatabase;
        IResourceBuilder<AzureSqlServerResource>? azureSqlServer = null;
        if (isPublishMode)
        {
            azureSqlServer = builder.AddAzureSqlServer("sqlServer")
                // Aspire 13.5 fixes the earlier SQL role-script implementation, but the shared-
                // identity case is still unverified. Provision one database principal for all
                // three application consumers until upstream role-module deduplication is proven.
                .ClearDefaultRoleAssignments();
            productsDatabase = azureSqlServer.AddDatabase(ProductsDatabaseResourceName);
        }
        else
        {
            var sqlServer = builder.AddSqlServer("sqlServer")
                .WithDockerfile("Resources/mssql-fts")
                .WithDbGate();

            if (!isTestMode)
                sqlServer.WithDataVolume("mssql-data");

            productsDatabase = sqlServer.AddDatabase(ProductsDatabaseResourceName);
        }

        // Azure Managed Redis has no Balanced SKUs available in US regions on this subscription,
        // and Azure Cache for Redis is being retired. Keep one stable password across deployments
        // so a reused Redis revision and newly deployed consumers cannot drift out of sync.
        // The disposable integration-test topology intentionally has no external parameters.
        var redis = isTestMode
            ? builder.AddRedis("redisCache")
            : builder.AddRedis("redisCache", password: parameters.RedisPassword);
        if (!isPublishMode)
            redis.WithRedisInsight();
        if (!isTestMode && !isPublishMode)
            redis.WithDataVolume("redis-data");

        IResourceBuilder<IResourceWithConnectionString> redisCache = redis;
        return new WithLoveInfrastructure(productsDatabase, redisCache, azureSqlServer);
    }

    private static IResourceBuilder<ProjectResource> AddProductsApi(
        IDistributedApplicationBuilder builder,
        WithLoveInfrastructure infrastructure,
        WithLoveParameters parameters)
    {
        var productsApi = builder.AddProject<Projects.WithLove_ProductsAPI>("productsApi")
            .WithEnvironment("OPENAI_API_KEY", parameters.OpenAiKey);

        productsApi.WaitForAndReference(infrastructure.RedisCache);
        productsApi.WaitForAndReference(infrastructure.ProductsDatabase);

        return productsApi;
    }

    private static void ConfigureScalarEndpoint(IResourceBuilder<ProjectResource> productsApi)
    {
        // Exclude Scalar from publish mode: ACA terminates TLS at ingress, not Kestrel.
        productsApi
            .WithEndpoint("scalar", callback: endpoint =>
            {
                endpoint.Port = 7001;
                endpoint.UriScheme = "https";
                endpoint.Transport = "http";
            })
            .WithUrlForEndpoint("scalar", url =>
            {
                url.DisplayText = "Scalar";
                url.Url = "/scalar";
            });
    }

    private static WithLoveApplication AddFullApplication(
        IDistributedApplicationBuilder builder,
        WithLoveInfrastructure infrastructure,
        WithLoveParameters parameters,
        IResourceBuilder<ProjectResource> productsApi)
    {
        var workflowServer = builder.AddProject<Projects.WithLove_WorkflowServer>("workflowServer")
            .WithEnvironment("OPENAI_API_KEY", parameters.OpenAiKey)
            .WithEnvironment("OpenInference__ProjectName", OpenInferenceProjectName);

        workflowServer.WaitForAndReference(infrastructure.ProductsDatabase);
        workflowServer.WithReference(productsApi);

        var shopSite = builder.AddProject<Projects.WithLove_Web>("shopSite")
            .WithEnvironment("OPENAI_API_KEY", parameters.OpenAiKey)
            .WithEnvironment("OpenInference__ProjectName", OpenInferenceProjectName);

        productsApi.WithEnvironment("OpenInference__ProjectName", OpenInferenceProjectName);

        shopSite.WaitForAndReference(infrastructure.RedisCache);
        shopSite.WaitForAndReference(infrastructure.ProductsDatabase);
        shopSite.WaitForAndReference(productsApi);
        shopSite.WithExternalHttpEndpoints();

        return new WithLoveApplication(productsApi, workflowServer, shopSite);
    }

    private static void ConfigureLocalDependencies(
        IDistributedApplicationBuilder builder,
        WithLoveApplication application,
        WithLoveParameters parameters)
    {
        var temporalServer = builder.AddTemporalDevContainer("temporal-server", options =>
        {
            options.ImageTag = "1.7.2";
            options.Namespace = "default";
            options.SearchAttributes =
            [
                SearchAttributeKey.CreateKeyword("StripeSessionId"),
                SearchAttributeKey.CreateKeyword("CustomerId"),
                // Required by GiftShopChatRegistrationExtensions' EnableSearchAttributes = true.
                // DurableChatWorkflowBase upserts these at start and after every completed turn,
                // distinguishing a started session with no completed turn from a real
                // conversation. Merely opening the panel starts no workflow. Names and types are
                // fixed by DurableSessionAttributes; they are not ours to choose.
                // These cover local only — Azure/Cloud namespaces need them registered separately.
                SearchAttributeKey.CreateLong("TurnCount"),
                SearchAttributeKey.CreateDateTimeOffset("SessionCreatedAt"),
            ];
            options.DevServerOptions.DatabaseFilename = "/home/temporal/temporal.db";
        });

        // Docker copy-up preserves the temporal user's ownership of this volume.
        temporalServer.WithVolume("temporal-data", "/home/temporal");

        application.WorkflowServer.WaitForAndReference(temporalServer);
        application.ShopSite.WaitForAndReference(temporalServer);

        // Referencing the CLI container supplies the Stripe__Default__* configuration locally.
        var stripe = builder.AddStripeCliContainer(
            "stripe",
            apiKey: parameters.StripeApiKey,
            publishableKey: parameters.StripePublicKey);
        stripe.WithWebhookForwardTo(application.ShopSite, "/stripe/webhook");
        application.WorkflowServer.WithReference(stripe);
        application.ShopSite.WithReference(stripe);
    }

    private static void ConfigureAiContentCapture(
        WithLoveApplication application,
        bool captureAiContent)
    {
        Configure(application.ProductsApi, capture: false);
        Configure(application.ShopSite, captureAiContent);
        Configure(application.WorkflowServer, captureAiContent);

        static void Configure(IResourceBuilder<ProjectResource> resource, bool capture)
        {
            resource
                .WithEnvironment(CaptureAiContentEnvironmentVariable, capture ? "true" : "false")
                // Aspire project resources enable the generic GenAI content switch by default.
                // WithLove owns content authorization, so remove the framework switch rather than
                // exposing a second control that could bypass the application policy.
                .WithEnvironment(context =>
                    context.EnvironmentVariables.Remove(
                        AspireGenAiCaptureMessageContentEnvironmentVariable));
        }
    }

    private static void ConfigureTraceDestination(
        IDistributedApplicationBuilder builder,
        WithLoveApplication application,
        bool useAx)
    {
        if (useAx)
        {
            var ax = builder.AddArizeAx("arize-ax", protocol: ArizeOtlpProtocol.HttpProtobuf);
            application.ProductsApi.WithReference(ax);
            application.WorkflowServer.WithReference(ax);
            application.ShopSite.WithReference(ax);
            return;
        }

        var phoenix = builder.AddArize("arize");
        application.ProductsApi.WithReference(phoenix).WaitFor(phoenix);
        application.WorkflowServer.WithReference(phoenix).WaitFor(phoenix);
        application.ShopSite.WithReference(phoenix).WaitFor(phoenix);
    }

    private static void ConfigureAzureDependencies(
        IDistributedApplicationBuilder builder,
        WithLoveApplication application,
        WithLoveInfrastructure infrastructure,
        WithLoveParameters parameters)
    {
        var keyVault = builder.AddAzureKeyVault("keyvault");
        var sharedIdentity = builder.AddAzureUserAssignedIdentity("withlove-identity");
        var sqlIdentityAccess = AddAzureSqlIdentityAccess(builder, infrastructure, sharedIdentity);

        var stripeWebhookSecret = parameters.StripeWebhookSecret
            ?? throw new InvalidOperationException(
                $"The '{StripeWebhookSecretParameterName}' parameter is only declared in publish mode.");

        // Key Vault secret names must not collide with parameter resource names.
        keyVault.AddSecret("kv-openai-api-key", parameters.OpenAiKey);
        keyVault.AddSecret("kv-stripe-api-key", parameters.StripeApiKey);
        keyVault.AddSecret("kv-stripe-public-key", parameters.StripePublicKey);
        keyVault.AddSecret("kv-stripe-webhook-secret", stripeWebhookSecret);
        keyVault.AddSecret("kv-temporal-api-key", parameters.TemporalApiKey);

        var temporalCloud = builder.AddTemporalCloud(
            "temporal-cloud",
            parameters.TemporalAddress,
            parameters.TemporalNamespace,
            configure: options => options.ApiKey = keyVault.GetSecret("kv-temporal-api-key"));

        ConfigureKeyVaultAccess(application.ProductsApi, keyVault, sharedIdentity)
            .WithEnvironment("OPENAI_API_KEY", keyVault.GetSecret("kv-openai-api-key"));

        var workflowServer = ConfigureKeyVaultAccess(application.WorkflowServer, keyVault, sharedIdentity)
            .WithEnvironment("OPENAI_API_KEY", keyVault.GetSecret("kv-openai-api-key"))
            .WithEnvironment("Stripe__Default__ApiKey", keyVault.GetSecret("kv-stripe-api-key"));

        var shopSite = ConfigureKeyVaultAccess(application.ShopSite, keyVault, sharedIdentity)
            .WithEnvironment("OPENAI_API_KEY", keyVault.GetSecret("kv-openai-api-key"))
            .WithEnvironment("Stripe__Default__ApiKey", keyVault.GetSecret("kv-stripe-api-key"))
            .WithEnvironment("Stripe__Default__PublicKey", keyVault.GetSecret("kv-stripe-public-key"))
            .WithEnvironment("Stripe__Default__WebhookSecret", keyVault.GetSecret("kv-stripe-webhook-secret"));

        workflowServer.WithReference(temporalCloud);
        shopSite.WithReference(temporalCloud);

        // The output reference makes the Azure Container App modules depend on the completed
        // SQL access deployment. WaitFor alone only affects run-mode resource readiness.
        var sqlIdentityAccessMarker = sqlIdentityAccess.GetOutput("deploymentScriptName");
        application.ProductsApi
            .WithEnvironment("WITHLOVE_SQL_IDENTITY_ACCESS", sqlIdentityAccessMarker)
            .WaitFor(sqlIdentityAccess);
        application.WorkflowServer
            .WithEnvironment("WITHLOVE_SQL_IDENTITY_ACCESS", sqlIdentityAccessMarker)
            .WaitFor(sqlIdentityAccess);
        application.ShopSite
            .WithEnvironment("WITHLOVE_SQL_IDENTITY_ACCESS", sqlIdentityAccessMarker)
            .WaitFor(sqlIdentityAccess);
    }

    private static IResourceBuilder<AzureBicepResource> AddAzureSqlIdentityAccess(
        IDistributedApplicationBuilder builder,
        WithLoveInfrastructure infrastructure,
        IResourceBuilder<AzureUserAssignedIdentityResource> sharedIdentity)
    {
        var sqlServer = infrastructure.AzureSqlServer
            ?? throw new InvalidOperationException("Azure SQL identity access is only available in publish mode.");

        return builder.AddBicepTemplate(
                "sql-identity-access",
                "Resources/sql-identity-access.bicep")
            .WithParameter("sqlServerName", sqlServer.Resource.NameOutputReference)
            .WithParameter(
                "sqlServerAdminName",
                new BicepOutputReference("sqlServerAdminName", sqlServer.Resource))
            .WithParameter("databaseName", ProductsDatabaseResourceName)
            .WithParameter("principalName", sharedIdentity.Resource.NameOutputReference)
            .WithParameter("principalClientId", sharedIdentity.Resource.ClientId);
    }

    private static IResourceBuilder<ProjectResource> ConfigureKeyVaultAccess(
        IResourceBuilder<ProjectResource> project,
        IResourceBuilder<AzureKeyVaultResource> keyVault,
        IResourceBuilder<AzureUserAssignedIdentityResource> identity)
        => project
            .WithAzureUserAssignedIdentity(identity)
            .WithRoleAssignments(keyVault, KeyVaultBuiltInRole.KeyVaultSecretsUser)
            .WithReference(keyVault);

    private static IResourceBuilder<ProjectResource> WaitForAndReference(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<IResourceWithConnectionString> dependency)
        => project.WaitFor(dependency).WithReference(dependency);

    private static IResourceBuilder<ProjectResource> WaitForAndReference(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<ProjectResource> dependency)
        => project.WaitFor(dependency).WithReference(dependency);

    private static IResourceBuilder<ProjectResource> WaitForAndReference(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<TemporalContainerResource> dependency)
        => project.WaitFor(dependency).WithReference(dependency);

    private sealed record WithLoveParameters(
        IResourceBuilder<ParameterResource> OpenAiKey,
        IResourceBuilder<ParameterResource> StripeApiKey,
        IResourceBuilder<ParameterResource> StripePublicKey,
        IResourceBuilder<ParameterResource> RedisPassword,
        IResourceBuilder<ParameterResource> TemporalAddress,
        IResourceBuilder<ParameterResource> TemporalNamespace,
        IResourceBuilder<ParameterResource> TemporalApiKey,
        // Null in run mode: the Stripe CLI container supplies the webhook secret locally, so the
        // parameter is never requested there. Same shape as WithLoveInfrastructure.AzureSqlServer —
        // publish-only members are nullable and unwrapped with a throw on the Azure path.
        IResourceBuilder<ParameterResource>? StripeWebhookSecret);

    private sealed record WithLoveInfrastructure(
        IResourceBuilder<IResourceWithConnectionString> ProductsDatabase,
        IResourceBuilder<IResourceWithConnectionString> RedisCache,
        IResourceBuilder<AzureSqlServerResource>? AzureSqlServer);

    private sealed record WithLoveApplication(
        IResourceBuilder<ProjectResource> ProductsApi,
        IResourceBuilder<ProjectResource> WorkflowServer,
        IResourceBuilder<ProjectResource> ShopSite);
}
