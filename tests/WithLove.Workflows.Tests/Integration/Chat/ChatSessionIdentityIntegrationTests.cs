using Microsoft.Extensions.AI;
using TemporalCommunity.Extensions.AI;
using Temporalio.Api.Enums.V1;
using Temporalio.Exceptions;
using WebGiftShopChatWorkflowClient = WithLove.Web.Services.GiftShopChatWorkflowClient;

namespace WithLove.Workflows.Tests.Integration.Chat;

/// <summary>
/// Executes the behaviour the stable-identity design rests on, against a real Temporal server.
/// </summary>
/// <remarks>
/// Everything the design assumes about shutdown and closed-run queries was established by reading
/// decompiled package source and the Temporal SDK. That is enough to design against and not enough
/// to ship on: the whole feature — End Chat meaning something, a returning visitor not being shown
/// a transcript the model cannot see — reduces to two claims that only a running server can settle.
/// First, that a shutdown signal genuinely <em>closes</em> the run rather than just flagging it.
/// Second, that a closed run still answers an unguarded query with its full transcript, which is
/// why the reject condition has to exist at all.
/// </remarks>
[Collection(GiftShopChatTemporalCollection.Name)]
public class ChatSessionIdentityIntegrationTests(GiftShopChatTemporalFixture fixture)
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task ClosedRun_AnswersAnUnguardedQueryButIsRefusedByTheGuardedOne()
    {
        var chatClient = new ScriptedGiftShopChatClient((_, _, _) => Final("A keepsake box, then."));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-anon-{Guid.NewGuid():N}";
        var handle = await StartAsync(harness, workflowId);
        await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("closed-1", "Find me a gift")),
            new WorkflowUpdateOptions { Id = "closed-1" });
        var client = CreateProductionClient();

        await client.ShutdownAsync(workflowId);

        // A signal is not a completion, but ShutdownAsync waits for the result — so by the time it
        // returns the run really is closed, not merely flagged.
        var description = await handle.DescribeAsync();
        description.Status.Should().Be(WorkflowExecutionStatus.Completed);

        // The half of this that is easy to disbelieve: closing a run does not make its transcript
        // unreadable. Query it without the reject condition and every entry is still there. This
        // is the pre-existing End Chat bug for authenticated users — whose workflow ID was already
        // stable, so their "cleared" conversation came straight back on reopen — and it is exactly
        // what a stable anonymous ID would have generalised to every visitor.
        var unguarded = await handle.QueryAsync(workflow => workflow.GetHistory());
        unguarded.Should().NotBeEmpty();

        // The guard, doing the only thing that makes End Chat mean anything.
        var guarded = () => client.GetHistoryAsync(workflowId);

        var rejection = await guarded.Should().ThrowAsync<WorkflowQueryRejectedException>();
        rejection.Which.WorkflowStatus.Should().Be(WorkflowExecutionStatus.Completed);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Shutdown_ThenRestartUnderTheSameId_StartsAFreshRunWithNoHistory()
    {
        // The returning-visitor case, end to end. The cookie outliving the workflow is the normal
        // case rather than an error: UseExisting plus AllowDuplicate mean a stale pointer starts a
        // new run under the same ID, and the customer sees an empty panel that matches exactly
        // what the model will see on the next turn.
        var chatClient = new ScriptedGiftShopChatClient((call, _, _) => Final($"Turn {call} done."));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            taskQueue: WorkflowConstants.DefaultTaskQueue);
        var workflowId = $"giftshop-chat-anon-{Guid.NewGuid():N}";
        var client = CreateProductionClient(harness);
        await client.EnsureStartedAsync(workflowId);
        var firstRunId = (await GetHandle(workflowId).DescribeAsync()).RunId;
        await client.SendMessageAsync(
            workflowId,
            "resume-1",
            CreateRequest("resume-1", "Something for my mum"));
        (await client.GetHistoryAsync(workflowId)).Should().NotBeEmpty();

        await client.ShutdownAsync(workflowId);
        await client.EnsureStartedAsync(workflowId);

        var secondRunId = (await GetHandle(workflowId).DescribeAsync()).RunId;
        secondRunId.Should().NotBe(firstRunId);
        (await client.GetHistoryAsync(workflowId)).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Shutdown_ThenAnImmediateTurn_IsNotRejectedByTheValidator()
    {
        // The race a stable ID makes reachable and a rotating one hid. End Chat, then type before
        // the run has finished closing: UseExisting attaches to the still-open, shutdown-flagged
        // run and the update validator answers "Session has been shut down." Waiting for the
        // result inside ShutdownAsync is what closes that window, so this asserts the fixed state
        // rather than documenting the hazard.
        var chatClient = new ScriptedGiftShopChatClient((call, _, _) => Final($"Turn {call} done."));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            taskQueue: WorkflowConstants.DefaultTaskQueue);
        var workflowId = $"giftshop-chat-anon-{Guid.NewGuid():N}";
        var client = CreateProductionClient(harness);
        await client.EnsureStartedAsync(workflowId);
        await client.SendMessageAsync(
            workflowId,
            "race-1",
            CreateRequest("race-1", "First question"));

        await client.ShutdownAsync(workflowId);
        await client.EnsureStartedAsync(workflowId);
        var result = await client.SendMessageAsync(
            workflowId,
            "race-2",
            CreateRequest("race-2", "Actually, one more thing"));

        result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
    }

    private WebGiftShopChatWorkflowClient CreateProductionClient(
        GiftShopChatWorkerHarness? harness = null)
    {
        var inputFactory = A.Fake<IDurableChatWorkflowInputFactory>();
        if (harness is not null)
            A.CallTo(() => inputFactory.Create()).Returns(harness.WorkflowInput);

        return new WebGiftShopChatWorkflowClient(fixture.Environment.Client, inputFactory);
    }

    private WorkflowHandle<GiftShopChatWorkflow> GetHandle(string workflowId) =>
        fixture.Environment.Client.GetWorkflowHandle<GiftShopChatWorkflow>(workflowId);

    private async Task<WorkflowHandle<GiftShopChatWorkflow>> StartAsync(
        GiftShopChatWorkerHarness harness,
        string workflowId) =>
        await fixture.Environment.Client.StartWorkflowAsync(
            (GiftShopChatWorkflow workflow) => workflow.RunAsync(harness.WorkflowInput),
            new WorkflowOptions(workflowId, harness.TaskQueue));

    private static DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> CreateRequest(
        string operationId,
        string message) =>
        new()
        {
            Messages = [new ChatMessage(ChatRole.User, message)],
            RequestData = new GiftShopChatRequestData(operationId),
            InitialTurnState = GiftShopChatTurnState.Create([]),
            CorrelationId = operationId,
            ConversationId = "identity-integration-test",
            ChatOptions = new ChatOptions
            {
                Instructions = GiftShopChatPrompt.BuildInstructions(null),
            }.WithChatClientTag("chat.operation_id", operationId),
            Options = new DurableTurnOptions
            {
                DispatchMode = DurableToolDispatchMode.Sequential,
            },
        };

    private static ChatResponse Final(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));
}
