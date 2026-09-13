using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using TemporalCommunity.Extensions.AI;
using Temporalio.Common;
using Temporalio.Extensions.Hosting;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Workflows;

namespace WithLove.Workflows.Chat;

public static class GiftShopChatRegistrationExtensions
{
    public const int MaxToolCallsPerTurn = 40;

    public static IServiceCollection AddGiftShopChatWorkflowClient(this IServiceCollection services)
    {
        services.AddDurableChatWorkflowInputFactory(
            WorkflowConstants.DefaultTaskQueue,
            ConfigureDurableExecution);

        foreach (var declaration in GiftShopChatToolCatalog.CreateDeclarations())
            services.AddDurableToolDeclaration(declaration);

        return services;
    }

    public static ITemporalWorkerServiceOptionsBuilder ConfigureGiftShopChatWorker(
        this ITemporalWorkerServiceOptionsBuilder worker,
        Func<AIFunction, DurableToolInvocationMetadata, AIFunction>? decorateTool = null)
    {
        worker.Services.AddScoped<GiftShopChatToolService>();
        worker.AddDurableAI(ConfigureDurableExecution)
            .AddWorkflow<GiftShopChatWorkflow>();

        foreach (var declaration in GiftShopChatToolCatalog.CreateDeclarations())
        {
            worker.AddDurableToolFactory<GiftShopChatRequestData, GiftShopChatTurnState>(
                declaration,
                (services, context) =>
                {
                    var activation = GiftShopChatToolCatalog.CreateActivation(
                        services,
                        context,
                        declaration);
                    if (decorateTool is null)
                        return activation;

                    return new DurableToolActivation<GiftShopChatTurnState>
                    {
                        Function = decorateTool(activation.Function, context.Metadata),
                        CompleteState = activation.CompleteState,
                    };
                });
        }

        return worker;
    }

    public static void ConfigureDurableExecution(DurableExecutionOptions options)
    {
        options.RegisterDefaultWorkflow = false;
        options.WorkflowIdPrefix = "giftshop-chat-";
        options.SessionTimeToLive = TimeSpan.FromHours(24);
        options.ActivityTimeout = TimeSpan.FromMinutes(2);
        options.HeartbeatTimeout = TimeSpan.FromMinutes(2);
        options.RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromSeconds(30),
            MaximumAttempts = 3,
        };
        options.MaxToolCallsPerTurn = MaxToolCallsPerTurn;
        options.MaximumConsecutiveErrorsPerRequest = 3;
        options.MaxEntryCount = 1000;
        // Upserts TurnCount (Long) and SessionCreatedAt (Datetime) at workflow start and after
        // every completed turn. TurnCount distinguishes a started session with no completed turn
        // from a real conversation; merely opening the panel starts no workflow. Replay-safe: the
        // flag is read from the frozen workflow input, so in-flight runs and the recorded replay
        // fixture keep their original value.
        // Requires both attributes registered at namespace level:
        //   temporal operator search-attribute create --name TurnCount --type Int
        //   temporal operator search-attribute create --name SessionCreatedAt --type Datetime
        options.EnableSearchAttributes = true;
        options.IncludeDetailedErrors = false;
    }
}
