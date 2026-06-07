using Temporalio.Client;
using Temporalio.Common.EnvConfig;
using Temporalio.Exceptions;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Workflows;

namespace WithLove.WorkflowServer.Services;

public partial class DatabaseSetupHostedService(ILogger<DatabaseSetupHostedService> logger) : BackgroundService
{
    private const string WorkflowId = "withlove-db-setup";
    private const string TaskQueue = "with-love-tasks";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var client = await ConnectWithRetryAsync(stoppingToken);

            LogStartingSetup(logger, WorkflowId);

            var handle = await client.StartWorkflowAsync(
                (DatabaseSetupWorkflow wf) => wf.RunAsync(),
                new(id: WorkflowId, taskQueue: TaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate
                });

            var result = await handle.GetResultAsync<DatabaseSetupResult>();

            LogSetupCompleted(logger,
                result.Migration.AppliedCount,
                result.Seed.CategoriesSeeded,
                result.Seed.ProductsSeeded,
                result.Embedding?.ProductsEmbedded ?? 0);
        }
        catch (WorkflowAlreadyStartedException)
        {
            LogSetupAlreadyRan(logger, WorkflowId);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            LogSetupFailed(logger, ex);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting database setup workflow (ID: {WorkflowId})")]
    private static partial void LogStartingSetup(ILogger logger, string workflowId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Database setup workflow ({WorkflowId}) already ran — skipping")]
    private static partial void LogSetupAlreadyRan(ILogger logger, string workflowId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Database setup workflow failed")]
    private static partial void LogSetupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Database setup complete — Migrations: {MigrationCount} applied, Seeding: {Categories} categories, {Products} products, Embeddings: {Embeddings} generated")]
    private static partial void LogSetupCompleted(ILogger logger, int migrationCount, int categories, int products, int embeddings);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to Temporal at {Host}")]
    private static partial void LogConnectedToTemporal(ILogger logger, string? host);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Temporal not ready (attempt {Attempt}/{Max}), retrying in {Delay}s...")]
    private static partial void LogTemporalNotReady(ILogger logger, int attempt, int max, double delay);

    private async Task<TemporalClient> ConnectWithRetryAsync(CancellationToken stoppingToken)
    {
        var connectOptions = ClientEnvConfig.LoadClientConnectOptions();
        var delay = TimeSpan.FromSeconds(2);
        const int maxAttempts = 10;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var client = await TemporalClient.ConnectAsync(connectOptions);
                LogConnectedToTemporal(logger, connectOptions.TargetHost);
                return client;
            }
            catch (RpcException) when (attempt < maxAttempts)
            {
                LogTemporalNotReady(logger, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        // Final attempt: let connection errors fail startup.
        return await TemporalClient.ConnectAsync(connectOptions);
    }
}
