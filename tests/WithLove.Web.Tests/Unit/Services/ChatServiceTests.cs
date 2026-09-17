using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.AI;
using TemporalCommunity.Extensions.AI;
using TemporalCommunity.Extensions.AI.Session;
using WithLove.OpenInference;
using WithLove.Web.Telemetry;
using WithLove.Workflows;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Workflows;

namespace WithLove.Web.Tests.Unit.Services;

public class ChatServiceTests : IDisposable
{
    private static readonly OpenInferenceTraceConfig VisibleContent = OpenInferenceTraceConfig.Enabled;
    private static readonly TelemetryIdentity TestTelemetryIdentity = TelemetryIdentity.Create(
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
        "test-v1");
    private readonly IGiftShopChatWorkflowClient _workflowClient =
        A.Fake<IGiftShopChatWorkflowClient>();
    private readonly AuthenticationStateProvider _authentication =
        A.Fake<AuthenticationStateProvider>();
    private readonly ICartService _cart = A.Fake<ICartService>();
    private readonly ILogger<ChatService> _logger = A.Fake<ILogger<ChatService>>();

    // Stands in for the wl-chat-id cookie that AnonymousChatMiddleware puts on the circuit. Real
    // shape — 32 lowercase hex characters — so anything asserting on the derived workflow ID sees
    // what production would produce.
    private readonly AnonymousChatSession _chatSession = new()
    {
        ChatId = Guid.NewGuid().ToString("N"),
    };
    private readonly Instrumentation _instrumentation = new();

    public ChatServiceTests()
    {
        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        A.CallTo(() => _cart.Items).Returns(Array.Empty<CartItem>());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_UsesOneOperationIdentityAndSequentialTypedState()
    {
        var service = CreateService();
        await service.InitializeAsync();
        DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>? capturedRequest = null;
        string? capturedWorkflowId = null;
        string? capturedUpdateId = null;
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Invokes((string workflowId, string updateId,
                DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> request) =>
            {
                capturedWorkflowId = workflowId;
                capturedUpdateId = updateId;
                capturedRequest = request;
            })
            .Returns(FinalResult(
                "Added it for you.",
                new GiftShopChatTurnState(
                    [new CartSnapshot(7, "Keepsake", 25m, 1)],
                    [new CartAction(CartActionType.Add, 7, "Keepsake", "/7.jpg", 25m, "price_7")],
                    [new NavigationAction(NavigationTarget.Cart, "/cart")] )));

        var result = await service.SendMessageAsync("Add the keepsake");

        capturedWorkflowId.Should().StartWith("giftshop-chat-anon-");
        capturedUpdateId.Should().NotBeNullOrWhiteSpace();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestData.OperationId.Should().Be(capturedUpdateId);
        capturedRequest.CorrelationId.Should().Be(capturedUpdateId);
        capturedRequest.ConversationId.Should().StartWith("hmac-test-v1-");
        capturedRequest.ConversationId.Should().NotContain(capturedWorkflowId!);
        capturedRequest.Messages.Should().ContainSingle();
        capturedRequest.Messages[0].Role.Should().Be(ChatRole.User);
        capturedRequest.Messages[0].Text.Should().Be("Add the keepsake");
        capturedRequest.Options.DispatchMode.Should().Be(DurableToolDispatchMode.Sequential);
        capturedRequest.ChatOptions!.Tools.Should().BeNull();
        capturedRequest.ChatOptions.AdditionalProperties.Should().ContainKey(
            $"{TemporalChatOptionsExtensions.ChatClientTagsKeyPrefix}chat.operation_id")
            .WhoseValue.Should().Be(capturedUpdateId);
        capturedRequest.InitialTurnState!.CartActions.Should().BeEmpty();
        capturedRequest.InitialTurnState.NavigationActions.Should().BeEmpty();
        result.AssistantMessage.Should().Be("Added it for you.");
        result.OperationId.Should().Be(capturedUpdateId);
        result.NavigationActions.Should().ContainSingle(action => action.Url == "/cart");
        A.CallTo(() => _cart.AddItemAsync(A<CartItem>.That.Matches(item => item.ProductId == 7)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_WithoutExplicitInitialization_InitializesBeforeDispatch()
    {
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(FinalResult("Ready.", GiftShopChatTurnState.Create([])));
        var service = CreateService();

        var result = await service.SendMessageAsync("Hello");

        result.AssistantMessage.Should().Be("Ready.");
        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _workflowClient.EnsureStartedAsync(
                $"giftshop-chat-anon-{_chatSession.ChatId}"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                $"giftshop-chat-anon-{_chatSession.ChatId}",
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_DuringInitialization_WaitsForTheSingleInitialization()
    {
        var authenticationReady = new TaskCompletionSource<AuthenticationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .Returns(authenticationReady.Task);
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(FinalResult("Ready.", GiftShopChatTurnState.Create([])));
        var service = CreateService();

        var initialize = service.InitializeAsync();
        var send = service.SendMessageAsync("Hello");

        initialize.IsCompleted.Should().BeFalse();
        send.IsCompleted.Should().BeFalse();
        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .MustNotHaveHappened();

        authenticationReady.SetResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        await Task.WhenAll(initialize, send);

        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _workflowClient.EnsureStartedAsync(
                $"giftshop-chat-anon-{_chatSession.ChatId}"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                $"giftshop-chat-anon-{_chatSession.ChatId}",
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_ExportsOnlyPseudonymousIdentityAndRoutesWithRawWorkflowId()
    {
        const string rawUserId = "raw-authenticated-user-123";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, rawUserId)],
            authenticationType: "test"));
        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .Returns(new AuthenticationState(principal));
        string? routedWorkflowId = null;
        DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>? capturedRequest = null;
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Invokes((string workflowId, string _,
                DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> request) =>
            {
                routedWorkflowId = workflowId;
                capturedRequest = request;
            })
            .Returns(FinalResult("Done.", GiftShopChatTurnState.Create([])));
        var service = CreateService();
        await service.InitializeAsync();
        using var telemetry = new ChatTurnTelemetryCapture(_instrumentation);

        var result = await service.SendMessageAsync("private prompt");

        routedWorkflowId.Should().Be(GiftShopChatWorkflow.WorkflowIdFor(rawUserId));
        capturedRequest!.ConversationId.Should().StartWith("hmac-test-v1-");
        capturedRequest.ConversationId.Should().NotContain(rawUserId);
        result.OperationId.Should().NotBeNullOrWhiteSpace();
        telemetry.ShouldContainOnlySafeIdentity(rawUserId, routedWorkflowId!);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_IterationLimitDisplaysSentinelAndAppliesNoCommands()
    {
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(new DurableTurnResult<GiftShopChatTurnState>
            {
                Response = new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    GiftShopChatResponseProjector.IterationLimitMessage)),
                CompletionReason = DurableTurnCompletionReason.IterationLimitReached,
                FinalTurnState = new GiftShopChatTurnState(
                    [],
                    [new CartAction(CartActionType.Clear)],
                    [new NavigationAction(NavigationTarget.Checkout, "/checkout")]),
            });

        var result = await service.SendMessageAsync("Keep trying");

        result.AssistantMessage.Should().Be(GiftShopChatResponseProjector.IterationLimitMessage);
        result.NavigationActions.Should().BeEmpty();
        A.CallTo(() => _cart.ClearAsync()).MustNotHaveHappened();
        A.CallTo(() => _cart.AddItemAsync(A<CartItem>._)).MustNotHaveHappened();
        A.CallTo(() => _cart.RemoveItemAsync(A<int>._)).MustNotHaveHappened();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task IncompleteResponse_UsesSameFallbackImmediatelyAndAfterReconnect()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var response = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, string.Empty),
        ])
        {
            FinishReason = ChatFinishReason.Length,
        };
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(new DurableTurnResult<GiftShopChatTurnState>
            {
                Response = response,
                CompletionReason = DurableTurnCompletionReason.IncompleteResponse,
                FinalTurnState = new GiftShopChatTurnState(
                    [],
                    [new CartAction(CartActionType.Clear)],
                    [new NavigationAction(NavigationTarget.Checkout, "/checkout")]),
            });
        A.CallTo(() => _workflowClient.GetHistoryAsync(A<string>._))
            .Returns(
            [
                DurableSessionRequest.FromMessages(
                    [new ChatMessage(ChatRole.User, "Try this")],
                    "fallback-1",
                    timestamp),
                DurableSessionResponse.FromChatResponse(
                    "fallback-1",
                    new ChatResponse(new ChatMessage(
                        ChatRole.Assistant,
                        "The model did not produce a complete final response."))
                    {
                        FinishReason = ChatFinishReason.Length,
                    },
                    timestamp,
                    DurableTurnCompletionReason.IncompleteResponse),
            ]);

        var immediate = await service.SendMessageAsync("Try this");
        await service.LoadHistoryAsync();

        immediate.AssistantMessage.Should().Be(GiftShopChatResponseProjector.AssistantFallback);
        immediate.NavigationActions.Should().BeEmpty();
        A.CallTo(() => _cart.ClearAsync()).MustNotHaveHappened();
        service.Messages.Select(message => message.Text).Should().Equal(
            "Try this",
            GiftShopChatResponseProjector.AssistantFallback);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task IncompleteResponse_HidesPartialProviderText()
    {
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(new DurableTurnResult<GiftShopChatTurnState>
            {
                Response = new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    "This answer was cut off before it was complete"))
                {
                    FinishReason = ChatFinishReason.Length,
                },
                CompletionReason = DurableTurnCompletionReason.IncompleteResponse,
                FinalTurnState = GiftShopChatTurnState.Create([]),
            });

        var result = await service.SendMessageAsync("Try this");

        result.AssistantMessage.Should().Be(GiftShopChatResponseProjector.AssistantFallback);
        service.Messages.Last().Text.Should().Be(GiftShopChatResponseProjector.AssistantFallback);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void DisplayProjection_UsesIntermediateAssistantTextAndHidesToolProtocol()
    {
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var messages = new ChatMessage[]
        {
            new(ChatRole.Assistant, "Let me check that for you."),
            new(ChatRole.Assistant,
            [
                new FunctionCallContent(
                    "call-1",
                    "search_products",
                    new Dictionary<string, object?> { ["query"] = "gift" }),
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "internal result")]),
            new(ChatRole.Assistant, string.Empty),
        };
        var history = new DurableSessionEntry[]
        {
            DurableSessionResponse.FromChatResponse(
                "projection-1",
                new ChatResponse(messages),
                timestamp),
        };

        GiftShopChatResponseProjector.GetDisplayAssistantText(messages)
            .Should().Be("Let me check that for you.");
        GiftShopChatResponseProjector.ProjectHistory(history)
            .Should().ContainSingle()
            .Which.Text.Should().Be("Let me check that for you.");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_FinalResponseRecordsAlignedCompletionTelemetry()
    {
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(FinalResult("Done.", GiftShopChatTurnState.Create([])));
        using var telemetry = new ChatTurnTelemetryCapture(_instrumentation);

        await service.SendMessageAsync("Finish");

        telemetry.ShouldHaveRecorded(
            "FinalResponse",
            ActivityStatusCode.Ok,
            expectedInput: "Finish",
            expectedOutput: "Done.");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_IterationLimitRecordsAlignedCompletionTelemetry()
    {
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(new DurableTurnResult<GiftShopChatTurnState>
            {
                Response = new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    GiftShopChatResponseProjector.IterationLimitMessage)),
                CompletionReason = DurableTurnCompletionReason.IterationLimitReached,
                FinalTurnState = GiftShopChatTurnState.Create([]),
            });
        using var telemetry = new ChatTurnTelemetryCapture(_instrumentation);

        await service.SendMessageAsync("Keep trying");

        telemetry.ShouldHaveRecorded(
            "IterationLimitReached",
            ActivityStatusCode.Ok,
            expectedInput: "Keep trying",
            expectedOutput: GiftShopChatResponseProjector.IterationLimitMessage);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_IncompleteResponseRecordsAlignedCompletionTelemetry()
    {
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(new DurableTurnResult<GiftShopChatTurnState>
            {
                Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty))
                {
                    FinishReason = ChatFinishReason.Length,
                },
                CompletionReason = DurableTurnCompletionReason.IncompleteResponse,
                FinalTurnState = GiftShopChatTurnState.Create([]),
            });
        using var telemetry = new ChatTurnTelemetryCapture(_instrumentation);

        await service.SendMessageAsync("Try again");

        telemetry.ShouldHaveRecorded(
            "IncompleteResponse",
            ActivityStatusCode.Ok,
            expectedInput: "Try again",
            expectedOutput: GiftShopChatResponseProjector.AssistantFallback);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_WorkflowFailureRecordsFailedTelemetryAndErrorStatus()
    {
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .ThrowsAsync(new InvalidOperationException("workflow failed"));
        using var telemetry = new ChatTurnTelemetryCapture(_instrumentation);

        Func<Task> send = () => service.SendMessageAsync("Fail");

        await send.Should().ThrowAsync<InvalidOperationException>();
        telemetry.ShouldHaveRecorded(
            "Failed",
            ActivityStatusCode.Error,
            expectedInput: "Fail");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Initialize_AuthenticatedUserUsesStableGiftShopWorkflowPrefix()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "customer-42"),
            new Claim(ClaimTypes.GivenName, "Morgan"),
            new Claim(ClaimTypes.Email, "morgan@example.test"),
        ], "test");
        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .Returns(new AuthenticationState(new ClaimsPrincipal(identity)));
        var service = CreateService();

        await service.InitializeAsync();
        await service.EnsureWorkflowStartedAsync();

        A.CallTo(() => _workflowClient.EnsureStartedAsync("giftshop-chat-customer-42"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task LoadHistory_ProjectsOnlyVisibleUserAndFinalAssistantText()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        A.CallTo(() => _workflowClient.GetHistoryAsync(A<string>._))
            .Returns(
            [
                DurableSessionRequest.FromMessages(
                    [new ChatMessage(ChatRole.User, "Find a gift")],
                    "history-1",
                    timestamp),
                DurableSessionResponse.FromChatResponse(
                    "history-1",
                    new ChatResponse(
                    [
                        new ChatMessage(ChatRole.Assistant,
                        [
                            new FunctionCallContent(
                                "call-1",
                                "search_products",
                                new Dictionary<string, object?> { ["query"] = "gift" }),
                        ]),
                        new ChatMessage(ChatRole.Tool,
                        [
                            new FunctionResultContent("call-1", "internal result"),
                        ]),
                        new ChatMessage(ChatRole.Assistant, "This keepsake is a lovely choice."),
                    ]),
                    timestamp),
            ]);

        await service.LoadHistoryAsync();

        service.Messages.Select(message => message.Text).Should().Equal(
            "Find a gift",
            "This keepsake is a lovely choice.");
        service.Messages.Should().NotContain(message => message.Text.Contains("internal result"));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    /// <remarks>
    /// This test replaces <c>EndSession_SignalsShutdownAndResetsAnonymousSession</c>, and the
    /// assertion it used to make — that the second workflow ID <em>differs</em> from the first —
    /// is now deliberately inverted to <c>Be</c>. That is not a weakened safety test.
    /// <para>
    /// The old assertion was a proxy. It held only because an anonymous session minted a fresh
    /// GUID per circuit, so the identity changed whether or not shutdown did anything, and End
    /// Chat's shutdown signal was decorative for anonymous users and outright broken for
    /// authenticated ones — whose ID was already stable, so their "cleared" transcript came back
    /// on reopen. With wl-chat-id the ID is stable for everyone, and shutdown becomes the only
    /// mechanism that ends a session. So this asserts the thing the product actually promises —
    /// the transcript is gone — rather than a proxy for it, and it now covers both auth states
    /// with one behaviour instead of accidentally covering neither.
    /// </para>
    /// </remarks>
    public async Task EndSession_ShutsDownTheRunAndLeavesNoTranscript()
    {
        var service = CreateService();
        await service.InitializeAsync();
        await service.EnsureWorkflowStartedAsync();
        var firstWorkflowId = Fake.GetCalls(_workflowClient)
            .Single(call => call.Method.Name == nameof(IGiftShopChatWorkflowClient.EnsureStartedAsync))
            .Arguments[0] as string;
        service.Messages.Add(new ChatHistoryEntry(true, "Find a gift", DateTime.UtcNow));

        await service.EndSessionAsync();

        A.CallTo(() => _workflowClient.ShutdownAsync(firstWorkflowId!, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        service.Messages.Should().BeEmpty();

        // Reopening rebuilds the *same* ID, because the cookie did not change and cannot: it is
        // HttpOnly and End Chat runs in a circuit with no HttpResponse.
        await service.InitializeAsync();
        await service.EnsureWorkflowStartedAsync();

        var workflowIds = Fake.GetCalls(_workflowClient)
            .Where(call => call.Method.Name == nameof(IGiftShopChatWorkflowClient.EnsureStartedAsync))
            .Select(call => call.Arguments[0] as string)
            .ToList();
        workflowIds.Should().HaveCount(2);
        workflowIds[1].Should().Be(firstWorkflowId);

        // Rehydration under that same ID still shows nothing, because GetHistoryAsync refuses to
        // read a closed run. That refusal — not an ID change — is what makes End Chat mean
        // something.
        A.CallTo(() => _workflowClient.GetHistoryAsync(firstWorkflowId!))
            .Throws(new Temporalio.Exceptions.WorkflowQueryRejectedException(
                Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Completed));

        await service.LoadHistoryAsync();

        service.Messages.Should().BeEmpty();
    }

    public void Dispose() => _instrumentation.Dispose();

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_UsesLargerOutputBudgetAndLowReasoningEffort()
    {
        var service = CreateService();
        await service.InitializeAsync();
        ChatOptions? capturedOptions = null;
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Invokes((string _, string _,
                DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> request) =>
                capturedOptions = request.ChatOptions)
            .Returns(FinalResult("Done.", GiftShopChatTurnState.Create([])));

        await service.SendMessageAsync("Hello");

        capturedOptions.Should().NotBeNull();

        // An unbounded step does not cost one runaway generation. A single turn runs up to the
        // workflow's 40-iteration tool cap, and Temporal retries each step three times, so the
        // worst case is 120 unbounded generations for one customer message.
        capturedOptions!.MaxOutputTokens.Should().Be(4000);
        capturedOptions.ModelId.Should().Be(WorkflowConstants.ChatModelId);
        capturedOptions.Reasoning.Should().NotBeNull();
        capturedOptions.Reasoning!.Effort.Should().Be(ReasoningEffort.Low);
        capturedOptions.Reasoning.Output.Should().Be(ReasoningOutput.None);

        // gpt-5-nano is a reasoning model: sampling parameters are rejected or ignored, so pinning
        // Temperature would advertise control the deployment does not actually have.
        capturedOptions.Temperature.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SendMessage_WhenTheTurnFails_PropagatesAndStopsThinking()
    {
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Throws(new InvalidOperationException("Workflow update failed"));
        service.IsThinking = true;

        var send = () => service.SendMessageAsync("Hello");

        // ChatService deliberately does not swallow this. There is no ErrorBoundary above ChatFab,
        // so its catch block is the only thing between a failed durable turn and a torn-down
        // circuit — this test is what makes that catch load-bearing rather than defensive noise.
        await send.Should().ThrowAsync<InvalidOperationException>();

        // The finally block still runs, so the thinking indicator does not stick on the failure
        // path. ChatFab clears it a second time because this only happens once the try is entered.
        service.IsThinking.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void AssistantFallback_IsCustomerSafeProse() =>
        // Rendered verbatim to the customer by ChatFab's catch block, so it must never grow an
        // exception message, a workflow ID or a stack frame.
        GiftShopChatResponseProjector.AssistantFallback.Should().Be(
            "Hmm, something went sideways on my end. Mind trying that again?");

    #region Stable anonymous identity (wl-chat-id)

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Initialize_AnonymousDerivesTheWorkflowIdFromTheChatCookieIdentity()
    {
        var service = CreateService();

        await service.InitializeAsync();
        await service.EnsureWorkflowStartedAsync();

        // Derived, not random. The anon- prefix is what guarantees this can never collide with
        // giftshop-chat-{userId} for a real user whose ID happens to look like a Guid.
        A.CallTo(() => _workflowClient.EnsureStartedAsync(
                $"giftshop-chat-anon-{_chatSession.ChatId}"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Initialize_AnonymousIsStableAcrossTwoServiceInstances()
    {
        // This is the feature. ChatService is scoped per SignalR circuit, so an F5, a second tab
        // and a dropped-then-reconnected circuit each construct a new instance — and before
        // wl-chat-id each of those minted a fresh GUID and therefore a brand new workflow. Two
        // instances reading one cookie-backed session must land on one workflow ID, or resume is
        // not implemented no matter what the rest of the wiring says.
        var firstCircuit = CreateService();
        var secondCircuit = CreateService();

        await firstCircuit.InitializeAsync();
        await firstCircuit.EnsureWorkflowStartedAsync();
        await secondCircuit.InitializeAsync();
        await secondCircuit.EnsureWorkflowStartedAsync();

        var workflowIds = Fake.GetCalls(_workflowClient)
            .Where(call => call.Method.Name == nameof(IGiftShopChatWorkflowClient.EnsureStartedAsync))
            .Select(call => call.Arguments[0] as string)
            .ToList();
        workflowIds.Should().HaveCount(2);
        workflowIds[1].Should().Be(workflowIds[0]);
        workflowIds[0].Should().Be($"giftshop-chat-anon-{_chatSession.ChatId}");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Initialize_TwoDifferentVisitorsDoNotShareAWorkflow()
    {
        // The counterweight to the test above: "stable" must not have been bought by making the ID
        // constant. Two visitors carrying different cookies must never meet in one transcript.
        var visitor = new ChatService(
            _workflowClient,
            _authentication,
            _cart,
            new AnonymousChatSession { ChatId = Guid.NewGuid().ToString("N") },
            _instrumentation,
            TestTelemetryIdentity,
            VisibleContent,
            _logger);
        var otherVisitor = CreateService();

        await visitor.InitializeAsync();
        await visitor.EnsureWorkflowStartedAsync();
        await otherVisitor.InitializeAsync();
        await otherVisitor.EnsureWorkflowStartedAsync();

        var workflowIds = Fake.GetCalls(_workflowClient)
            .Where(call => call.Method.Name == nameof(IGiftShopChatWorkflowClient.EnsureStartedAsync))
            .Select(call => call.Arguments[0] as string)
            .ToList();
        workflowIds.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    [InlineData("")]
    [InlineData(null)]
    public async Task Initialize_AnonymousWithNoChatIdentity_ThrowsAndDispatchesNothing(
        string? missingChatId)
    {
        // Deliberately *not* the cart's pattern. FusionCacheCartService mints a per-circuit GUID
        // when its cookie value did not arrive; copied here that would hand every visitor a fresh
        // workflow while the feature looked implemented and nothing reported otherwise. The wiring
        // bug this catches — middleware unregistered, RegisterPersistentService dropped, middleware
        // placed after MapRazorComponents — reproduces on the first page load in development and
        // never in production if development is correct, so failing loudly costs nothing and
        // silently degrading costs the whole feature.
        _chatSession.ChatId = missingChatId!;
        var service = CreateService();

        var initialize = () => service.InitializeAsync();

        (await initialize.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*anonymous chat identity is missing*");
        Fake.GetCalls(_workflowClient).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Initialize_AnonymousWithNoChatIdentity_CountsAnIdentityFailure()
    {
        // A log line is not a report. This counter is zero in a healthy system and non-zero
        // exactly when stable identity has silently reverted, which is the only signal that
        // survives a future cookie-consent gate switching the cookie off.
        _chatSession.ChatId = string.Empty;
        var service = CreateService();
        using var counter = new CounterCapture(_instrumentation, "chat.session.identity_failures");

        var initialize = () => service.InitializeAsync();

        await initialize.Should().ThrowAsync<InvalidOperationException>();
        counter.Total.Should().Be(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Initialize_AuthenticatedUser_NeedsNoAnonymousChatIdentity()
    {
        // An authenticated visitor's ID comes from the auth claim, so a missing cookie must not
        // break their chat — the throw is scoped to the case where it is genuinely load-bearing.
        _chatSession.ChatId = string.Empty;
        A.CallTo(() => _authentication.GetAuthenticationStateAsync())
            .Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "customer-42")],
                authenticationType: "test"))));
        var service = CreateService();

        await service.InitializeAsync();
        await service.EnsureWorkflowStartedAsync();

        A.CallTo(() => _workflowClient.EnsureStartedAsync("giftshop-chat-customer-42"))
            .MustHaveHappenedOnceExactly();
    }

    #endregion

    #region Lazy start and hydration

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task OpeningThePanel_StartsNoWorkflowUntilTheFirstMessage()
    {
        // ChatFab.OpenChat is exactly these two calls now; the EnsureWorkflowStartedAsync it used
        // to make was deleted so a visitor who opens the panel and never types creates nothing in
        // Temporal. Safe only because hydration handles both resulting cases on its own — a
        // never-started ID comes back NotFound and a closed run is refused by NotOpen.
        var service = CreateService();
        A.CallTo(() => _workflowClient.GetHistoryAsync(A<string>._))
            .Throws(new Temporalio.Exceptions.RpcException(
                Temporalio.Exceptions.RpcException.StatusCode.NotFound,
                "workflow not found",
                null));
        A.CallTo(() => _workflowClient.SendMessageAsync(
                A<string>._,
                A<string>._,
                A<DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>>._))
            .Returns(FinalResult("Hello there.", GiftShopChatTurnState.Create([])));

        await service.InitializeAsync();
        await service.LoadHistoryAsync();

        A.CallTo(() => _workflowClient.EnsureStartedAsync(A<string>._)).MustNotHaveHappened();

        await service.SendMessageAsync("Hello");

        A.CallTo(() => _workflowClient.EnsureStartedAsync(
                $"giftshop-chat-anon-{_chatSession.ChatId}"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task LoadHistory_WhenTheRunIsNotOpen_LeavesMessagesEmpty()
    {
        // The other half of the NotOpen reject condition. A closed run still answers queries with
        // its full transcript, and rendering it would put a conversation on screen that the model
        // handling the next turn has no memory of — the customer only finds out by leaning on
        // earlier context and being contradicted.
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.GetHistoryAsync(A<string>._))
            .Throws(new Temporalio.Exceptions.WorkflowQueryRejectedException(
                Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Completed));

        var load = () => service.LoadHistoryAsync();

        await load.Should().NotThrowAsync();
        service.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task LoadHistory_WhenTheWorkflowWasNeverStarted_LeavesMessagesEmpty()
    {
        // Regression guard. With lazy start this is now the ordinary first-open path, not an edge
        // case, so it must stay a swallowed NotFound rather than an unavailable panel.
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.GetHistoryAsync(A<string>._))
            .Throws(new Temporalio.Exceptions.RpcException(
                Temporalio.Exceptions.RpcException.StatusCode.NotFound,
                "workflow not found",
                null));

        var load = () => service.LoadHistoryAsync();

        await load.Should().NotThrowAsync();
        service.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task LoadHistory_WhenTemporalIsUnreachable_Propagates()
    {
        // The two catches above are narrow on purpose. A broader one would turn "Temporal is
        // down" into a panel that looks like a working, empty chat and fails at the first send —
        // ChatFab needs this to reach it so it can render the unavailable state instead.
        var service = CreateService();
        await service.InitializeAsync();
        A.CallTo(() => _workflowClient.GetHistoryAsync(A<string>._))
            .Throws(new Temporalio.Exceptions.RpcException(
                Temporalio.Exceptions.RpcException.StatusCode.Unavailable,
                "Temporal is down",
                null));

        var load = () => service.LoadHistoryAsync();

        await load.Should().ThrowAsync<Temporalio.Exceptions.RpcException>();
    }

    #endregion

    #region End Chat resilience

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    [InlineData("unavailable")]
    [InlineData("not-found")]
    [InlineData("timed-out")]
    public async Task EndSession_ClearsTheTranscriptEvenWhenTheShutdownFails(string failure)
    {
        // Temporal being down must not mean a customer cannot clear their own conversation. An
        // orphaned run that reaps itself is the better of the two bad outcomes.
        Exception thrown = failure switch
        {
            "unavailable" => new Temporalio.Exceptions.RpcException(
                Temporalio.Exceptions.RpcException.StatusCode.Unavailable,
                "Temporal is down",
                null),
            "not-found" => new Temporalio.Exceptions.RpcException(
                Temporalio.Exceptions.RpcException.StatusCode.NotFound,
                "never started",
                null),
            _ => new OperationCanceledException(),
        };
        var service = CreateService();
        await service.InitializeAsync();
        service.Messages.Add(new ChatHistoryEntry(true, "Find a gift", DateTime.UtcNow));
        A.CallTo(() => _workflowClient.ShutdownAsync(A<string>._, A<CancellationToken>._))
            .ThrowsAsync(thrown);

        var end = () => service.EndSessionAsync();

        await end.Should().NotThrowAsync();
        service.Messages.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task EndSession_BoundsTheWaitForTheRunToClose()
    {
        // A signal is not a completion, so ShutdownAsync waits for the run to actually close —
        // which is what stops the next message attaching to a still-open, shutdown-flagged run and
        // being rejected by the update validator. Waiting unbounded would trade that race for a
        // spinner, so the wait carries a budget the call site owns.
        var service = CreateService();
        await service.InitializeAsync();
        CancellationToken budget = default;
        A.CallTo(() => _workflowClient.ShutdownAsync(A<string>._, A<CancellationToken>._))
            .Invokes((string _, CancellationToken token) => budget = token);

        await service.EndSessionAsync();

        budget.CanBeCanceled.Should().BeTrue();
        budget.IsCancellationRequested.Should().BeFalse();
    }

    #endregion

    private ChatService CreateService() =>
        new(
            _workflowClient,
            _authentication,
            _cart,
            _chatSession,
            _instrumentation,
            TestTelemetryIdentity,
            VisibleContent,
            _logger);

    private static DurableTurnResult<GiftShopChatTurnState> FinalResult(
        string assistantMessage,
        GiftShopChatTurnState state) =>
        new()
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, assistantMessage)),
            CompletionReason = DurableTurnCompletionReason.FinalResponse,
            FinalTurnState = state,
        };

    /// <summary>Sums every measurement recorded on one named counter of one meter.</summary>
    private sealed class CounterCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private long _total;

        public CounterCapture(Instrumentation instrumentation, string instrumentName)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ReferenceEquals(instrument.Meter, instrumentation.Meter)
                        && instrument.Name == instrumentName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, measurement, _, _) => Interlocked.Add(ref _total, measurement));
            _listener.Start();
        }

        public long Total => Interlocked.Read(ref _total);

        public void Dispose() => _listener.Dispose();
    }

    private sealed class ChatTurnTelemetryCapture : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;
        private readonly ConcurrentQueue<Activity> _activities = new();
        private readonly ConcurrentQueue<string> _histogramReasons = new();

        public ChatTurnTelemetryCapture(Instrumentation instrumentation)
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => ReferenceEquals(source, instrumentation.ActivitySource),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _activities.Enqueue(activity),
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ReferenceEquals(instrument.Meter, instrumentation.Meter)
                        && instrument.Name == "chat.turn.duration_ms")
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meterListener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "completion_reason" && tag.Value is string reason)
                        _histogramReasons.Enqueue(reason);
                }
            });
            _meterListener.Start();
        }

        public void ShouldHaveRecorded(
            string completionReason,
            ActivityStatusCode status,
            string? expectedInput = null,
            string? expectedOutput = null)
        {
            _activities.Should().ContainSingle();
            var activity = _activities.Single();
            activity.GetTagItem("chat.completion_reason").Should().Be(completionReason);
            activity.Status.Should().Be(status);
            if (expectedInput is not null)
            {
                activity.GetTagItem(OpenInferenceAttributes.InputValue).Should().Be(expectedInput);
                activity.GetTagItem(OpenInferenceAttributes.InputMimeType).Should().Be("text/plain");
            }
            if (expectedOutput is not null)
            {
                activity.GetTagItem(OpenInferenceAttributes.OutputValue).Should().Be(expectedOutput);
                activity.GetTagItem(OpenInferenceAttributes.OutputMimeType).Should().Be("text/plain");
            }
            else
            {
                activity.GetTagItem(OpenInferenceAttributes.OutputValue).Should().BeNull();
            }
            _histogramReasons.Should().ContainSingle().Which.Should().Be(completionReason);
        }

        public void ShouldContainOnlySafeIdentity(params string[] rawIdentifiers)
        {
            var activity = _activities.Should().ContainSingle().Subject;
            activity.GetTagItem(OpenInferenceAttributes.OpenInferenceSpanKind).Should().Be("CHAIN");
            activity.GetTagItem(OpenInferenceAttributes.SessionId).Should().BeOfType<string>()
                .Which.Should().StartWith("hmac-test-v1-");
            activity.GetTagItem(OpenInferenceAttributes.UserId).Should().BeOfType<string>()
                .Which.Should().StartWith("hmac-test-v1-");
            var exportedText = string.Join('\n', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
            foreach (var rawIdentifier in rawIdentifiers)
                exportedText.Should().NotContain(rawIdentifier);
            activity.TagObjects.Should().NotContain(tag => tag.Key == "temporalWorkflowID");
        }

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }
    }
}
