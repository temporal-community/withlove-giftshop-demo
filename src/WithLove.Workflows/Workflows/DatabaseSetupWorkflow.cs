using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using WithLove.Workflows.Activities;

namespace WithLove.Workflows.Workflows;

[Workflow]
public class DatabaseSetupWorkflow
{
    [WorkflowRun]
    public async Task<DatabaseSetupResult> RunAsync()
    { 
        Workflow.Logger.LogInformation("Starting database setup workflow");

        var migrationResult = await Workflow.ExecuteActivityAsync(
            (DatabaseActivities act) => act.ApplyMigrationsAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new()
                {
                    InitialInterval = TimeSpan.FromSeconds(2),
                    BackoffCoefficient = 2.0f,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 5
                }
            });

        Workflow.Logger.LogInformation(
            "Migration complete: {Count} applied — {Message}",
            migrationResult.AppliedCount, migrationResult.Message);

        // Apply schema upgrades (vector column, full-text index) — idempotent
        await Workflow.ExecuteActivityAsync(
            (DatabaseActivities act) => act.ApplySchemaUpgradesAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(2),
                RetryPolicy = new()
                {
                    InitialInterval = TimeSpan.FromSeconds(2),
                    BackoffCoefficient = 2.0f,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 5
                }
            });

        Workflow.Logger.LogInformation("Schema upgrades applied successfully");

        var seedResult = await Workflow.ExecuteActivityAsync(
            (DatabaseActivities act) => act.SeedDatabaseAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new()
                {
                    InitialInterval = TimeSpan.FromSeconds(2),
                    BackoffCoefficient = 2.0f,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 3
                }
            });

        Workflow.Logger.LogInformation(
            "Seeding complete: {Categories} categories, {Products} products",
            seedResult.CategoriesSeeded, seedResult.ProductsSeeded);

        // Generate vector embeddings for all products that don't have one yet
        var embeddingResult = await Workflow.ExecuteActivityAsync(
            (DatabaseActivities act) => act.GenerateEmbeddingsAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new()
                {
                    InitialInterval = TimeSpan.FromSeconds(2),
                    BackoffCoefficient = 2.0f,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 3
                }
            });

        Workflow.Logger.LogInformation(
            "Embedding generation complete: {Count} products embedded",
            embeddingResult.ProductsEmbedded);

        return new DatabaseSetupResult(migrationResult, seedResult, embeddingResult);
    }
}
