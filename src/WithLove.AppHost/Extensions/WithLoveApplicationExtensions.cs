using Aspire.Hosting.Azure;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Pipelines;
using Azure.Provisioning.KeyVault;
using TemporalCommunity.Aspire.Hosting;
using Temporalio.Common;

namespace WithLove.AppHost.Extensions;

internal static partial class WithLoveApplicationExtensions
{
    private const string ProductsDatabaseResourceName = "productsDatabase";
    private const string StripeWebhookSecretParameterName = "stripe-webhook-secret";
    private const string StripeWebhookSecretPrefix = "whsec_";

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

        if (isPublishMode)
            ConfigureAzureDependencies(builder, application, infrastructure, parameters);
        else
            ConfigureLocalDependencies(builder, application, parameters);

        ConfigureWorkflowServer(application.WorkflowServer);
        ConfigureShopSite(application.ShopSite);
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

        // Validate after parameters resolve and before publishing any infrastructure.
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
            requiredBy: [WellKnownPipelineSteps.PublishPrereq],
            description: "Rejects a malformed Stripe webhook signing secret before it reaches Key Vault.");
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
            "this parameter is publish-only, and `aspire deploy` reads its own cache from " +
            ".secrets.env, not the `aspire secret set` user-secret store (see " +
            "docs/azure-deployment.md Step 5). The value itself is deliberately not shown.");
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

    private static IResourceBuilder<DotnetProjectResource> AddProductsApi(
        IDistributedApplicationBuilder builder,
        WithLoveInfrastructure infrastructure,
        WithLoveParameters parameters)
    {
        var productsApi = builder.AddDotnetProject("productsApi", "../WithLove.ProductsAPI/WithLove.ProductsAPI.csproj")
            .WithEnvironment("OPENAI_API_KEY", parameters.OpenAiKey);

        productsApi.WaitForAndReference(infrastructure.RedisCache);
        productsApi.WaitForAndReference(infrastructure.ProductsDatabase);

        return productsApi;
    }

    private static void ConfigureScalarEndpoint(IResourceBuilder<DotnetProjectResource> productsApi)
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
        IResourceBuilder<DotnetProjectResource> productsApi)
    {
        var workflowServer = builder.AddDotnetProject("workflowServer", "../WithLove.WorkflowServer/WithLove.WorkflowServer.csproj")
            .WithEnvironment("OPENAI_API_KEY", parameters.OpenAiKey);

        workflowServer.WaitForAndReference(infrastructure.ProductsDatabase);
        workflowServer.WithReference(productsApi);

        var shopSite = builder.AddDotnetProject("shopSite", "../WithLove.Web/WithLove.Web.csproj")
            .WithEnvironment("OPENAI_API_KEY", parameters.OpenAiKey);

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

    private static IResourceBuilder<DotnetProjectResource> ConfigureKeyVaultAccess(
        IResourceBuilder<DotnetProjectResource> project,
        IResourceBuilder<AzureKeyVaultResource> keyVault,
        IResourceBuilder<AzureUserAssignedIdentityResource> identity)
        => project
            .WithAzureUserAssignedIdentity(identity)
            .WithRoleAssignments(keyVault, KeyVaultBuiltInRole.KeyVaultSecretsUser)
            .WithReference(keyVault);

    private static IResourceBuilder<DotnetProjectResource> WaitForAndReference(
        this IResourceBuilder<DotnetProjectResource> project,
        IResourceBuilder<IResourceWithConnectionString> dependency)
        => project.WaitFor(dependency).WithReference(dependency);

    private static IResourceBuilder<DotnetProjectResource> WaitForAndReference(
        this IResourceBuilder<DotnetProjectResource> project,
        IResourceBuilder<DotnetProjectResource> dependency)
        => project.WaitFor(dependency).WithReference(dependency);

    private static IResourceBuilder<DotnetProjectResource> WaitForAndReference(
        this IResourceBuilder<DotnetProjectResource> project,
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
        IResourceBuilder<DotnetProjectResource> ProductsApi,
        IResourceBuilder<DotnetProjectResource> WorkflowServer,
        IResourceBuilder<DotnetProjectResource> ShopSite);
}
