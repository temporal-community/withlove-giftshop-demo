using System.Linq.Expressions;
using TemporalCommunity.Extensions.AI;
using TemporalCommunity.Extensions.AI.Exceptions;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Client.Interceptors;
using WithLove.Workflows;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Workflows;

namespace WithLove.Web.Tests.Unit.Services;

public class GiftShopChatWorkflowClientTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task EnsureStarted_UsesConcreteWorkflowFactoryInputAndExplicitIdPolicies()
    {
        var temporalClient = A.Fake<ITemporalClient>();
        var inputFactory = A.Fake<IDurableChatWorkflowInputFactory>();
        var input = new DurableChatWorkflowInput();
        A.CallTo(() => inputFactory.Create()).Returns(input);
        Expression<Func<GiftShopChatWorkflow, Task>>? runCall = null;
        WorkflowOptions? options = null;
        A.CallTo(() => temporalClient.StartWorkflowAsync(
                A<Expression<Func<GiftShopChatWorkflow, Task>>>._,
                A<WorkflowOptions>._))
            .Invokes((Expression<Func<GiftShopChatWorkflow, Task>> expression,
                WorkflowOptions startOptions) =>
            {
                runCall = expression;
                options = startOptions;
            })
            .Returns(Task.FromResult<WorkflowHandle<GiftShopChatWorkflow>>(null!));
        var client = new GiftShopChatWorkflowClient(temporalClient, inputFactory);

        await client.EnsureStartedAsync("giftshop-chat-customer-42");

        runCall.Should().NotBeNull();
        ((MethodCallExpression)runCall!.Body).Method.DeclaringType
            .Should().Be(typeof(GiftShopChatWorkflow));
        A.CallTo(() => inputFactory.Create()).MustHaveHappenedOnceExactly();
        options.Should().NotBeNull();
        options!.Id.Should().Be("giftshop-chat-customer-42");
        options.TaskQueue.Should().Be(WorkflowConstants.DefaultTaskQueue);
        options.IdConflictPolicy.Should().Be(WorkflowIdConflictPolicy.UseExisting);
        options.IdReusePolicy.Should().Be(WorkflowIdReusePolicy.AllowDuplicate);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task GetHistory_RefusesToReadARunThatIsNoLongerOpen()
    {
        // Render only what the model can see. Without NotOpen a closed run answers the query with
        // its full transcript, so End Chat "clears" a conversation that comes straight back on
        // reopen — which is not a new hazard introduced by stable identity but the pre-existing
        // authenticated-user bug that stable identity generalises to every visitor.
        const string workflowId = "giftshop-chat-anon-0123456789abcdef0123456789abcdef";
        var temporalClient = A.Fake<ITemporalClient>();
        var interceptor = new CapturingOutboundInterceptor();
        A.CallTo(() => temporalClient.OutboundInterceptor).Returns(interceptor);
        A.CallTo(() => temporalClient.GetWorkflowHandle<GiftShopChatWorkflow>(
                A<string>._,
                A<string?>._,
                A<string?>._))
            .ReturnsLazily((string id, string? runId, string? firstRunId) =>
                new WorkflowHandle<GiftShopChatWorkflow>(temporalClient, id, runId, null, firstRunId));
        var client = new GiftShopChatWorkflowClient(
            temporalClient,
            A.Fake<IDurableChatWorkflowInputFactory>());

        await client.GetHistoryAsync(workflowId);

        interceptor.Query.Should().NotBeNull();
        interceptor.Query!.Id.Should().Be(workflowId);
        interceptor.Query.Query.Should().Be(nameof(GiftShopChatWorkflow.GetHistory));
        interceptor.Query.Options.Should().NotBeNull();
        interceptor.Query.Options!.RejectCondition.Should().Be(QueryRejectCondition.NotOpen);
    }

    /// <summary>
    /// Records the query the client hands to Temporal, options included.
    /// </summary>
    /// <remarks>
    /// The reject condition is deliberately set per query rather than on the client, because this
    /// <c>ITemporalClient</c> is shared with <c>StripeEventHandler</c> and
    /// <c>TemporalLoyaltyService</c>; a client-level condition would silently change their query
    /// behaviour too. Reading it off the outbound interceptor is what makes "per query" observable
    /// rather than merely intended.
    /// </remarks>
    private sealed class CapturingOutboundInterceptor : ClientOutboundInterceptor
    {
        public CapturingOutboundInterceptor()
            : base(null!)
        {
        }

        public QueryWorkflowInput? Query { get; private set; }

        public override Task<TResult> QueryWorkflowAsync<TResult>(QueryWorkflowInput input)
        {
            Query = input;
            return Task.FromResult<TResult>(default!);
        }
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_RejectsCallerSuppliedToolsBeforeTemporalDispatch()
    {
        var temporalClient = A.Fake<ITemporalClient>();
        var client = new GiftShopChatWorkflowClient(
            temporalClient,
            A.Fake<IDurableChatWorkflowInputFactory>());
        var request = new DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>
        {
            Messages = [new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.User,
                "Hello")],
            RequestData = new GiftShopChatRequestData("caller-tools"),
            InitialTurnState = GiftShopChatTurnState.Create([]),
            CorrelationId = "caller-tools",
            ChatOptions = new Microsoft.Extensions.AI.ChatOptions
            {
                Tools = [GiftShopChatToolCatalog.CreateDeclarations()[0]],
            },
        };

        Func<Task> send = () => client.SendMessageAsync(
            "giftshop-chat-test",
            "caller-tools",
            request);

        await send.Should().ThrowAsync<DurableConfigurationException>()
            .WithMessage("ChatOptions.Tools cannot be used*");
        Fake.GetCalls(temporalClient).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_RejectsNullOptionsBeforeTemporalDispatch()
    {
        var temporalClient = A.Fake<ITemporalClient>();
        var client = new GiftShopChatWorkflowClient(
            temporalClient,
            A.Fake<IDurableChatWorkflowInputFactory>());
        var request = new DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>
        {
            Messages = [new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.User,
                "Hello")],
            RequestData = new GiftShopChatRequestData("null-options"),
            InitialTurnState = GiftShopChatTurnState.Create([]),
            CorrelationId = "null-options",
            Options = null!,
        };

        Func<Task> send = () => client.SendMessageAsync(
            "giftshop-chat-test",
            "null-options",
            request);

        await send.Should().ThrowAsync<DurableConfigurationException>()
            .WithMessage("DurableTurnRequest.Options cannot be null*");
        Fake.GetCalls(temporalClient).Should().BeEmpty();
    }
}
