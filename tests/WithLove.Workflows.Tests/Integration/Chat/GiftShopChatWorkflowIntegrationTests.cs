using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using TemporalCommunity.Extensions.AI;
using TemporalCommunity.Extensions.AI.Session;
using WithLove.OpenInference;
using WithLove.OpenInference.Spans;
using WithLove.Web.Telemetry;
using WithLove.Web.Models;
using WithLove.WorkflowServer.Services;
using WebChatService = WithLove.Web.Services.ChatService;
using WebGiftShopChatWorkflowClient = WithLove.Web.Services.GiftShopChatWorkflowClient;
using WebInstrumentation = WithLove.Web.Services.Instrumentation;
using WebWorkflowClient = WithLove.Web.Services.IGiftShopChatWorkflowClient;
using WebCartService = WithLove.Web.Services.ICartService;
using WebAnonymousChatSession = WithLove.Web.Services.AnonymousChatSession;

namespace WithLove.Workflows.Tests.Integration.Chat;

[Collection(GiftShopChatTemporalCollection.Name)]
public class GiftShopChatWorkflowIntegrationTests(GiftShopChatTemporalFixture fixture)
{
    private const string GetChatStepActivity = "TemporalCommunity.Extensions.AI.GetChatStep";
    private const string InvokeFunctionActivity = "TemporalCommunity.Extensions.AI.InvokeFunction";

    private static TelemetryIdentity CreateTelemetryIdentity() => TelemetryIdentity.Create(
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
        "test-v1");

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SequentialCartTurn_UsesSeparateActivitiesAndCompletedState()
    {
        var packageActivities = new ConcurrentBag<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddWorkflowServerTracingSources()
            .AddProcessor(new SimpleActivityExportProcessor(
                new CollectingActivityExporter(packageActivities)))
            .Build();
        var chatClient = new ScriptedGiftShopChatClient((call, messages, options) => call switch
        {
            1 => ToolCallsWithText(
                "I'll update the cart and verify it.",
                new FunctionCallContent(
                    "add-1",
                    "add_to_cart",
                    new Dictionary<string, object?>
                    {
                        ["productId"] = 7,
                        ["quantity"] = 2,
                    }),
                new FunctionCallContent(
                    "view-1",
                    "view_cart",
                    new Dictionary<string, object?>())),
            2 => Final(AssertCartToolResults(messages, options)),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-integration-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        var result = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("turn-1", "Add two keepsake boxes")),
            new WorkflowUpdateOptions { Id = "turn-1" });

        result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        result.Response.Messages.SelectMany(message => message.Contents)
            .OfType<TextContent>()
            .Should().Contain(content => content.Text == "I'll update the cart and verify it.");
        GiftShopChatResponseProjector.GetLastAssistantText(result.Response.Messages)
            .Should().Be("The cart is ready.");
        result.FinalTurnState.Should().NotBeNull();
        result.FinalTurnState!.CartActions.Should().ContainSingle(action =>
            action.Type == CartActionType.Add && action.ProductId == 7 && action.Quantity == 2);
        result.FinalTurnState.WorkingCart.Should().ContainSingle(item =>
            item.ProductId == 7 && item.Quantity == 2);

        var activityTypes = await GetScheduledActivityTypesAsync(handle);
        activityTypes.Count(type => type == GetChatStepActivity).Should().Be(2);
        activityTypes.Count(type => type == InvokeFunctionActivity).Should().Be(2);
        packageActivities.Should().Contain(activity => activity.OperationName.StartsWith("chat "));
        packageActivities.Should().Contain(activity => activity.OperationName.StartsWith("execute_tool "));

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
        await CaptureReplayHistoryIfRequestedAsync(handle);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task TracedUpdate_ConnectsModelAndToolSpansToTheChatTurn()
    {
        const string sourceName = "withlove-test-chat-turn";
        const string operationId = "trace-turn-1";
        using var source = new ActivitySource(sourceName);
        using var productsSource = new ActivitySource("productsApi");
        var allCompletedActivities = new ConcurrentBag<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(
                sourceName,
                Temporalio.Extensions.OpenTelemetry.TracingInterceptor.ClientSource.Name,
                Temporalio.Extensions.OpenTelemetry.TracingInterceptor.WorkflowsSource.Name,
                Temporalio.Extensions.OpenTelemetry.TracingInterceptor.ActivitiesSource.Name)
            .AddWorkflowServerTracingSources()
            .AddHttpClientInstrumentation()
            .AddProcessor(new SimpleActivityExportProcessor(
                new CollectingActivityExporter(allCompletedActivities)))
            .Build();
        var chatClient = new ScriptedGiftShopChatClient((call, _, _) => call switch
        {
            1 => ToolCalls(new FunctionCallContent(
                "search-1",
                "search_products",
                new Dictionary<string, object?> { ["query"] = "keepsake" })),
            2 => Final("I found a keepsake box."),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        var visibleContent = OpenInferenceTraceConfig.Enabled;
        await using var productsServer = await TracingProductsServer.StartAsync(
            productsSource,
            allCompletedActivities);
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            productsBaseAddress: productsServer.BaseUri,
            traceConfig: visibleContent);
        var targetHost = fixture.Environment.Client.Connection.Options.TargetHost
            ?? throw new InvalidOperationException("Temporal target host is unavailable.");
        var caller = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(targetHost)
        {
            Namespace = fixture.Environment.Client.Options.Namespace,
            DataConverter = DurableAIDataConverter.Instance,
            Interceptors =
                [Microsoft.Extensions.Hosting.Extensions.CreateSafeTemporalTracingInterceptor()],
        });
        var workflowId = $"giftshop-chat-trace-{Guid.NewGuid():N}";
        var handle = await caller.StartWorkflowAsync(
            (GiftShopChatWorkflow workflow) => workflow.RunAsync(harness.WorkflowInput),
            new WorkflowOptions(workflowId, harness.TaskQueue));
        allCompletedActivities.Clear();

        Activity chainActivity;
        using (OpenInferenceContextScope.Push(new OpenInferenceContextValues
               {
                   SessionId = "integration-test",
                   UserId = "safe-user",
                   Tags = ["withlove", "chat"],
               }))
        using (var chain = source.StartChain(
                   "chat.turn",
                   "Find a keepsake",
                   visibleContent))
        {
            chain.Activity.Should().NotBeNull();
            chainActivity = chain.Activity!;
            chainActivity.SetTag("chat.operation_id", operationId);
            var request = CreateRequest(operationId, "Find a keepsake");

            var result = await handle.ExecuteUpdateAsync(
                workflow => workflow.SendMessageAsync(request),
                new WorkflowUpdateOptions { Id = operationId });

            result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
            chain.Complete("I found a keepsake box.");
        }

        tracerProvider.ForceFlush().Should().BeTrue();
        productsServer.ForceFlush().Should().BeTrue();
        var turnTrace = allCompletedActivities
            .Where(activity => activity.TraceId == chainActivity.TraceId)
            .ToArray();
        var modelSpans = turnTrace
            .Where(activity => Equals(activity.GetTagItem("gen_ai.operation.name"), "chat"))
            .ToArray();
        var toolSpan = turnTrace.Should().ContainSingle(activity =>
                Equals(activity.GetTagItem("gen_ai.operation.name"), "execute_tool"))
            .Subject;
        var retrieverSpan = allCompletedActivities.Should().ContainSingle(activity =>
                Equals(activity.GetTagItem(OpenInferenceAttributes.OpenInferenceSpanKind), "RETRIEVER"))
            .Subject;

        modelSpans.Should().HaveCount(2);
        modelSpans.Should().OnlyContain(activity => IsDescendantOf(activity, chainActivity, turnTrace));
        modelSpans.Should().OnlyContain(activity =>
            Equals(activity.GetTagItem(OpenInferenceAttributes.SessionId), "integration-test"));
        toolSpan.OperationName.Should().Be("execute_tool search_products");
        toolSpan.GetTagItem(OpenInferenceAttributes.OpenInferenceSpanKind).Should().Be("TOOL");
        toolSpan.GetTagItem(OpenInferenceAttributes.ToolName).Should().Be("search_products");
        toolSpan.GetTagItem(OpenInferenceAttributes.ToolId).Should().Be("search-1");
        toolSpan.GetTagItem(OpenInferenceAttributes.SessionId).Should().Be("integration-test");
        toolSpan.GetTagItem("chat.operation_id").Should().Be(operationId);
        toolSpan.GetTagItem(OpenInferenceAttributes.InputValue).Should().Be("{\"query\":\"keepsake\"}");
        toolSpan.GetTagItem(OpenInferenceAttributes.OutputValue)?.ToString()
            .Should().Contain("Keepsake Box");
        IsDescendantOf(toolSpan, chainActivity, turnTrace).Should().BeTrue();
        retrieverSpan.TraceId.Should().Be(chainActivity.TraceId);
        IsDescendantOf(retrieverSpan, chainActivity, turnTrace).Should().BeTrue();

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SecondTurn_ReceivesHistoricalToolProtocolWhileUiProjectionHidesIt()
    {
        var observedHistoricalProtocol = false;
        var chatClient = new ScriptedGiftShopChatClient((call, messages, options) => call switch
        {
            1 => ToolCalls(new FunctionCallContent(
                "search-1",
                "search_products",
                new Dictionary<string, object?> { ["query"] = "keepsake" })),
            2 => Final("I found a keepsake box."),
            3 => Final(ObserveHistoricalProtocol(messages, options, ref observedHistoricalProtocol)),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-history-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("history-1", "Find a keepsake")),
            new WorkflowUpdateOptions { Id = "history-1" });
        await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("history-2", "Tell me more")),
            new WorkflowUpdateOptions { Id = "history-2" });

        observedHistoricalProtocol.Should().BeTrue();
        var history = await handle.QueryAsync(workflow => workflow.GetHistory());
        var visible = GiftShopChatResponseProjector.ProjectHistory(history);
        visible.Should().HaveCount(4);
        visible.Should().NotContain(entry => entry.Text.Contains("Keepsake Box"));
        visible.Select(entry => entry.Text).Should().ContainInOrder(
            "Find a keepsake",
            "I found a keepsake box.",
            "Tell me more",
            "Here are a few more details.");

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Theory]
    [InlineData("null-request")]
    [InlineData("null-request-data")]
    [InlineData("blank-operation-id")]
    [InlineData("correlation-mismatch")]
    [InlineData("null-state")]
    [InlineData("null-state-collection")]
    [InlineData("prepopulated-cart-action")]
    [InlineData("prepopulated-navigation-action")]
    [InlineData("null-messages")]
    [InlineData("empty-messages")]
    [InlineData("multiple-messages")]
    [InlineData("assistant-role")]
    [InlineData("blank-message")]
    [InlineData("multiple-content-items")]
    [InlineData("null-options")]
    [InlineData("parallel-dispatch")]
    [InlineData("caller-supplied-tools")]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task InvalidUpdate_IsRejectedBeforeActivityAndLaterValidUpdateSucceeds(string invalidCase)
    {
        var chatClient = new ScriptedGiftShopChatClient((_, _, _) => Final("Valid turn completed."));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-validation-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);
        var invalid = CreateInvalidRequest(invalidCase);

        Func<Task> submitInvalid;
        if (invalidCase is "null-options" or "caller-supplied-tools")
        {
            // MEAI's durable JSON shape omits null options and caller tool delegates, so those
            // invalid values must be rejected at the application client boundary while they are
            // still observable. The real workflow remains active below to prove zero dispatch and
            // successful processing of the next valid Update.
            var client = new WebGiftShopChatWorkflowClient(
                fixture.Environment.Client,
                A.Fake<IDurableChatWorkflowInputFactory>());
            submitInvalid = () => client.SendMessageAsync(
                workflowId,
                $"invalid-{invalidCase}",
                invalid!);
        }
        else
        {
            submitInvalid = () => handle.ExecuteUpdateAsync(
                workflow => workflow.SendMessageAsync(invalid!),
                new WorkflowUpdateOptions { Id = $"invalid-{invalidCase}" });
        }

        await submitInvalid.Should().ThrowAsync<Exception>();
        chatClient.CallCount.Should().Be(0);
        (await GetScheduledActivityTypesAsync(handle)).Should().BeEmpty();

        var valid = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("valid-after-invalid", "Continue")),
            new WorkflowUpdateOptions { Id = "valid-after-invalid" });
        valid.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        chatClient.CallCount.Should().Be(1);

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task IterationLimit_DiscardsStateChangingCommandsAndProtocolBeforeNextTurn()
    {
        var observedCleanNextTurn = false;
        var chatClient = new ScriptedGiftShopChatClient((call, messages, options) =>
        {
            options!.Tools.Should().HaveCount(13);
            if (call == GiftShopChatRegistrationExtensions.MaxToolCallsPerTurn + 1)
            {
                messages.Should().Contain(message =>
                    message.Text == GiftShopChatResponseProjector.IterationLimitMessage);
                messages.SelectMany(message => message.Contents)
                    .Any(content => content is FunctionCallContent or FunctionResultContent)
                    .Should().BeFalse();
                observedCleanNextTurn = true;
                return Final("The next turn starts without discarded tool protocol.");
            }

            if (call > GiftShopChatRegistrationExtensions.MaxToolCallsPerTurn + 1)
                throw new InvalidOperationException($"Unexpected model call {call}.");

            return ToolCalls(new FunctionCallContent(
                $"add-{call}",
                "add_to_cart",
                new Dictionary<string, object?>
                {
                    ["productId"] = 7,
                    ["quantity"] = 1,
                }));
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-limit-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);
        var cart = A.Fake<WebCartService>();
        A.CallTo(() => cart.Items).Returns(Array.Empty<CartItem>());
        using var instrumentation = new WebInstrumentation();
        var chatService = CreateWebChatService(handle, cart, instrumentation);
        await chatService.InitializeAsync();

        var result = await chatService.SendMessageAsync("Keep adding the keepsake");

        chatClient.CallCount.Should().Be(GiftShopChatRegistrationExtensions.MaxToolCallsPerTurn);
        result.AssistantMessage.Should().Be(GiftShopChatResponseProjector.IterationLimitMessage);
        result.NavigationActions.Should().BeEmpty();
        A.CallTo(() => cart.AddItemAsync(A<CartItem>._)).MustNotHaveHappened();
        A.CallTo(() => cart.RemoveItemAsync(A<int>._)).MustNotHaveHappened();
        A.CallTo(() => cart.ClearAsync()).MustNotHaveHappened();

        var activityTypes = await GetScheduledActivityTypesAsync(handle);
        activityTypes.Count(type => type == GetChatStepActivity).Should().Be(40);
        activityTypes.Count(type => type == InvokeFunctionActivity).Should().Be(40);

        var next = await chatService.SendMessageAsync("Start again");
        next.AssistantMessage.Should().Be("The next turn starts without discarded tool protocol.");
        observedCleanNextTurn.Should().BeTrue();
        chatClient.CallCount.Should().Be(
            GiftShopChatRegistrationExtensions.MaxToolCallsPerTurn + 1);
        A.CallTo(() => cart.AddItemAsync(A<CartItem>._)).MustNotHaveHappened();

        await chatService.EndSessionAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task IncompleteResponse_DiscardsProvisionalCommandsAndProtocolBeforeNextTurn()
    {
        var observedCleanNextTurn = false;
        var chatClient = new ScriptedGiftShopChatClient((call, messages, options) => call switch
        {
            1 => new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                [Call("add-provisional", "add_to_cart", ("productId", 7), ("quantity", 1))]))
            {
                FinishReason = ChatFinishReason.ToolCalls,
            },
            2 => new ChatResponse(new ChatMessage(ChatRole.Assistant, []))
            {
                FinishReason = ChatFinishReason.Length,
            },
            3 => new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                ObserveIncompleteHistory(messages, options, ref observedCleanNextTurn)))
            {
                FinishReason = ChatFinishReason.Stop,
            },
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-incomplete-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);
        var cart = A.Fake<WebCartService>();
        A.CallTo(() => cart.Items).Returns(Array.Empty<CartItem>());
        using var instrumentation = new WebInstrumentation();
        var chatService = CreateWebChatService(handle, cart, instrumentation);
        await chatService.InitializeAsync();

        var incomplete = await chatService.SendMessageAsync("Add the keepsake");

        incomplete.AssistantMessage.Should().Be(GiftShopChatResponseProjector.AssistantFallback);
        incomplete.NavigationActions.Should().BeEmpty();
        A.CallTo(() => cart.AddItemAsync(A<CartItem>._)).MustNotHaveHappened();
        var history = await handle.QueryAsync(workflow => workflow.GetHistory());
        var stored = history.OfType<DurableSessionResponse>().Should().ContainSingle().Which;
        stored.CompletionReason.Should().Be(DurableTurnCompletionReason.IncompleteResponse);
        stored.FinishReason.Should().Be(ChatFinishReason.Length);
        stored.Messages.SelectMany(message => message.Contents)
            .Should().NotContain(content =>
                content.GetType() == typeof(FunctionCallContent) ||
                content.GetType() == typeof(FunctionResultContent));
        GiftShopChatResponseProjector.ProjectHistory(history)
            .Last().Text.Should().Be(GiftShopChatResponseProjector.AssistantFallback);

        var activityTypes = await GetScheduledActivityTypesAsync(handle);
        activityTypes.Count(type => type == GetChatStepActivity).Should().Be(2);
        activityTypes.Count(type => type == InvokeFunctionActivity).Should().Be(1);

        var next = await chatService.SendMessageAsync("Start again");

        next.AssistantMessage.Should().Be("The next turn starts without incomplete tool protocol.");
        observedCleanNextTurn.Should().BeTrue();
        A.CallTo(() => cart.AddItemAsync(A<CartItem>._)).MustNotHaveHappened();

        await chatService.EndSessionAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task LoyaltyTool_UsesActivityTemporalClientWithoutDiClient()
    {
        var observedBalance = false;
        var chatClient = new ScriptedGiftShopChatClient((call, messages, _) => call switch
        {
            1 => ToolCalls(new FunctionCallContent(
                "loyalty-1",
                "view_loyalty_points",
                new Dictionary<string, object?>())),
            2 => Final(ObserveLoyaltyResult(messages, ref observedBalance)),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var userId = $"user-{Guid.NewGuid():N}";
        var loyaltyState = LoyaltyState.Empty with
        {
            Balance = 650,
            LifetimeEarned = 650,
        };
        var loyalty = await fixture.Environment.Client.StartWorkflowAsync(
            (LoyaltyAccountWorkflow workflow) => workflow.RunAsync(loyaltyState),
            new WorkflowOptions($"loyalty-{userId}", harness.TaskQueue));
        (await loyalty.QueryAsync(workflow => workflow.GetBalance())).Should().Be(650);
        // The workflow ID must be the one this user owns; ValidateSendMessage asserts the pairing.
        var workflowId = GiftShopChatWorkflow.WorkflowIdFor(userId);
        var handle = await StartWorkflowAsync(harness, workflowId);

        var result = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest(
                "loyalty-turn",
                "How many Love Tokens do I have?",
                new UserContext("Riley", userId))),
            new WorkflowUpdateOptions { Id = "loyalty-turn" });

        result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        observedBalance.Should().BeTrue();
        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task NavigationTool_IsReturnedInCompletedTypedState()
    {
        var chatClient = new ScriptedGiftShopChatClient((call, _, options) => call switch
        {
            1 => ToolCalls(new FunctionCallContent(
                "navigate-1",
                "navigate_to_product",
                new Dictionary<string, object?> { ["productId"] = 7 })),
            2 => Final(AssertToolsAndReturn(options, "Opening the keepsake box.")),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-navigation-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        var result = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest(
                "navigation-turn",
                "Open product seven")),
            new WorkflowUpdateOptions { Id = "navigation-turn" });

        result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        result.FinalTurnState!.NavigationActions.Should().ContainSingle().Which.Should().Be(
            new NavigationAction(NavigationTarget.Product, "/product/7"));
        result.FinalTurnState.CartActions.Should().BeEmpty();

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task EveryCatalogDeclaration_MatchesItsWorkerImplementationSchema()
    {
        var chatClient = new ScriptedGiftShopChatClient((call, _, options) => call switch
        {
            1 => ToolCalls(
                Call("all-1", "search_products", ("query", "keepsake")),
                Call("all-2", "get_product_details", ("productId", 7)),
                Call("all-3", "get_categories"),
                Call("all-4", "browse_category", ("categoryId", 3)),
                Call("all-5", "add_to_cart", ("productId", 7), ("quantity", 1)),
                Call("all-6", "remove_from_cart", ("productIds", "7")),
                Call("all-7", "view_cart"),
                Call("all-8", "clear_cart"),
                Call("all-9", "navigate_to_product", ("productId", 7)),
                Call("all-10", "navigate_to_collection", ("categoryId", 3)),
                Call("all-11", "navigate_to_cart"),
                Call("all-12", "navigate_to_checkout"),
                Call("all-13", "view_loyalty_points")),
            2 => Final(AssertToolsAndReturn(options, "Every tool is wired.")),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-catalog-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        var result = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest(
                "catalog-turn",
                "Exercise every tool")),
            new WorkflowUpdateOptions { Id = "catalog-turn" });

        result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        result.FinalTurnState!.CartActions.Should().HaveCount(3);
        result.FinalTurnState.NavigationActions.Should().HaveCount(4);
        (await GetScheduledActivityTypesAsync(handle))
            .Count(type => type == InvokeFunctionActivity)
            .Should().Be(13);

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task ShutdownRejectsLaterUpdatesWithoutSchedulingActivities()
    {
        var chatClient = new ScriptedGiftShopChatClient((_, _, _) => Final("unused"));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-shutdown-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
        Func<Task> update = () => handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("after-shutdown", "Hello?")),
            new WorkflowUpdateOptions { Id = "after-shutdown" });

        await update.Should().ThrowAsync<Exception>();
        chatClient.CallCount.Should().Be(0);
        (await GetScheduledActivityTypesAsync(handle)).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task ConcurrentTurns_AreSerializedByTheWorkflowBase()
    {
        var firstTurnGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var chatClient = new ScriptedGiftShopChatClient(async (call, _, options) =>
        {
            options!.Tools.Should().HaveCount(13);
            if (call == 1)
                await firstTurnGate.Task;

            return Final($"Turn {call} completed.");
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient);
        var workflowId = $"giftshop-chat-concurrency-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        var first = handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("concurrent-1", "First")),
            new WorkflowUpdateOptions { Id = "concurrent-1" });
        await WaitForCallCountAsync(chatClient, 1);

        var second = handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("concurrent-2", "Second")),
            new WorkflowUpdateOptions { Id = "concurrent-2" });
        await Task.Delay(100);
        chatClient.CallCount.Should().Be(1);

        firstTurnGate.SetResult();
        var results = await Task.WhenAll(first, second);

        chatClient.CallCount.Should().Be(2);
        results.Should().OnlyContain(result =>
            result.CompletionReason == DurableTurnCompletionReason.FinalResponse);
        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task ContinueAsNew_PreservesConcreteWorkflowAndFrozenDeclarations()
    {
        var chatClient = new ScriptedGiftShopChatClient((call, _, options) =>
        {
            options!.Tools.Should().HaveCount(13);
            return Final($"Turn {call} completed.");
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            input => input with { MaxEntryCount = 2 });
        var workflowId = $"giftshop-chat-continue-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);
        var initialRunId = handle.ResultRunId;
        initialRunId.Should().NotBeNullOrEmpty();

        var first = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("continue-1", "First")),
            new WorkflowUpdateOptions { Id = "continue-1" });
        first.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);

        var nextRunId = await WaitForContinueAsNewAsync(
            fixture.Environment.Client,
            handle.Id,
            initialRunId!);
        nextRunId.Should().NotBe(initialRunId);

        var second = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest("continue-2", "Second")),
            new WorkflowUpdateOptions { Id = "continue-2" });
        second.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        chatClient.CallCount.Should().Be(2);

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    /// <summary>
    /// A hallucinated product or collection ID produces a correctable answer and no navigation.
    /// </summary>
    /// <remarks>
    /// Both assertions matter and neither is sufficient. Returning the "there is no ..." string
    /// while still recording the navigation is the half-fix: the model reads an error, the customer
    /// is sent to a dead route anyway, and a test that only inspected the tool result would pass.
    /// The turn state is therefore asserted empty as well — that is the value Web actually acts on.
    /// The requested paths are asserted too, because the guard is only a guard if it really asked
    /// ProductsAPI; a range check that happened to reject 4242 would satisfy everything else here.
    /// </remarks>
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task NavigationTools_WithUnknownIds_AnswerWithoutQueueingNavigation()
    {
        var toolResults = new List<string>();
        var products = CreateNavigationProductsHandler();
        var chatClient = new ScriptedGiftShopChatClient((call, messages, options) => call switch
        {
            1 => ToolCalls(
                Call("nav-product-missing", "navigate_to_product", ("productId", 4242)),
                Call("nav-collection-missing", "navigate_to_collection", ("categoryId", 4242))),
            2 => Final(CaptureToolResults(
                messages,
                options,
                toolResults,
                "Neither of those exists in the catalogue.")),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            productsHandler: products);
        var workflowId = $"giftshop-chat-nav-unknown-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        var result = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest(
                "nav-unknown-turn",
                "Open product 4242 and collection 4242")),
            new WorkflowUpdateOptions { Id = "nav-unknown-turn" });

        result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        result.FinalTurnState!.NavigationActions.Should().BeEmpty();
        result.FinalTurnState.CartActions.Should().BeEmpty();
        toolResults.Should().Contain(toolResult => toolResult.Contains(
            "There is no product with ID 4242, so nothing was opened."));
        toolResults.Should().Contain(toolResult => toolResult.Contains(
            "There is no collection with ID 4242, so nothing was opened."));
        products.Paths.Should().Contain("/api/products/4242");
        products.Paths.Should().Contain("/api/categories/4242");

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    /// <summary>
    /// Verifying IDs did not cost the tools their ordinary behaviour, including collection 0.
    /// </summary>
    /// <remarks>
    /// The guard's failure mode in the other direction is a tool that now refuses work it used to
    /// do. Category 0 is the case to watch: it is the documented "show every collection" value and
    /// <c>/api/categories/0</c> does not exist, so resolving it like any other ID would turn the
    /// collections index into a permanent "there is no collection with ID 0". The absent lookup is
    /// asserted rather than inferred from the queued action, since a lookup that 404s and is then
    /// ignored would leave the same turn state behind.
    /// </remarks>
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task NavigationTools_WithValidIds_QueueNavigationAndSkipLookupForAllCollections()
    {
        var toolResults = new List<string>();
        var products = CreateNavigationProductsHandler();
        var chatClient = new ScriptedGiftShopChatClient((call, messages, options) => call switch
        {
            1 => ToolCalls(
                Call("nav-product-ok", "navigate_to_product", ("productId", 7)),
                Call("nav-collection-ok", "navigate_to_collection", ("categoryId", 3)),
                Call("nav-collection-all", "navigate_to_collection", ("categoryId", 0))),
            2 => Final(CaptureToolResults(
                messages,
                options,
                toolResults,
                "Taking you there.")),
            _ => throw new InvalidOperationException($"Unexpected model call {call}."),
        });
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            productsHandler: products);
        var workflowId = $"giftshop-chat-nav-valid-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(harness, workflowId);

        var result = await handle.ExecuteUpdateAsync(
            workflow => workflow.SendMessageAsync(CreateRequest(
                "nav-valid-turn",
                "Open the keepsake box, then Comfort, then everything")),
            new WorkflowUpdateOptions { Id = "nav-valid-turn" });

        result.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        result.FinalTurnState!.NavigationActions.Should().Equal(
            new NavigationAction(NavigationTarget.Product, "/product/7"),
            new NavigationAction(NavigationTarget.Collection, "/collections/3"),
            new NavigationAction(NavigationTarget.Collection, "/collections"));
        toolResults.Should().Contain(toolResult =>
            toolResult.Contains("Navigating to the Keepsake Box page."));
        toolResults.Should().Contain(toolResult =>
            toolResult.Contains("Navigating to the Comfort collection."));
        toolResults.Should().Contain(toolResult =>
            toolResult.Contains("Navigating to all collections."));
        products.Paths.Should().NotContain("/api/categories/0");

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    /// <summary>ProductsAPI stub knowing only product 7 and collection 3.</summary>
    private static ScriptedProductsHandler CreateNavigationProductsHandler() =>
        new(request =>
        {
            var json = (request.RequestUri?.PathAndQuery ?? string.Empty) switch
            {
                "/api/products/7" => """{"id":7,"name":"Keepsake Box","price":25.00}""",
                "/api/categories/3" => """{"id":3,"name":"Comfort"}""",
                _ => string.Empty,
            };
            return new HttpResponseMessage(
                json.Length == 0 ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

    /// <summary>Records the tool results the model can see, then returns the final text.</summary>
    /// <remarks>
    /// Recorded rather than asserted in place: an assertion that throws inside the scripted client
    /// fails the GetChatStep activity, which Temporal retries and eventually surfaces as a workflow
    /// failure naming the retry, not the expectation. Collecting here and asserting in the test body
    /// keeps the failure legible. Appends, never replaces — a retried model step would otherwise
    /// discard what the first attempt observed.
    /// </remarks>
    private static string CaptureToolResults(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        List<string> toolResults,
        string text)
    {
        options!.Tools.Should().HaveCount(13);
        lock (toolResults)
        {
            toolResults.AddRange(messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(functionResult => JsonSerializer.Serialize(functionResult.Result)));
        }

        return text;
    }

    private async Task<WorkflowHandle<GiftShopChatWorkflow>> StartWorkflowAsync(
        GiftShopChatWorkerHarness harness,
        string workflowId) =>
        await fixture.Environment.Client.StartWorkflowAsync(
            (GiftShopChatWorkflow workflow) => workflow.RunAsync(harness.WorkflowInput),
            new WorkflowOptions(workflowId, harness.TaskQueue));

    private static WebChatService CreateWebChatService(
        WorkflowHandle<GiftShopChatWorkflow> handle,
        WebCartService cart,
        WebInstrumentation instrumentation)
    {
        var authentication = A.Fake<AuthenticationStateProvider>();
        A.CallTo(() => authentication.GetAuthenticationStateAsync())
            .Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        return new WebChatService(
            new WorkflowHandleChatClient(handle),
            authentication,
            cart,
            new WebAnonymousChatSession { ChatId = Guid.NewGuid().ToString("N") },
            instrumentation,
            CreateTelemetryIdentity(),
            OpenInferenceTraceConfig.Enabled,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WebChatService>.Instance);
    }

    private static DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> CreateRequest(
        string operationId,
        string message,
        UserContext? user = null) =>
        new()
        {
            Messages = [new ChatMessage(ChatRole.User, message)],
            RequestData = new GiftShopChatRequestData(operationId, user),
            InitialTurnState = GiftShopChatTurnState.Create([]),
            CorrelationId = operationId,
            ConversationId = "integration-test",
            ChatOptions = new ChatOptions
            {
                Instructions = GiftShopChatPrompt.BuildInstructions(user),
            }.WithChatClientTag("chat.operation_id", operationId),
            Options = new DurableTurnOptions
            {
                DispatchMode = DurableToolDispatchMode.Sequential,
            },
        };

    private static DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>?
        CreateInvalidRequest(string invalidCase)
    {
        var request = CreateRequest("invalid-operation", "Hello");
        return invalidCase switch
        {
            "null-request" => null,
            "null-request-data" => Copy(request, forceNullRequestData: true),
            "blank-operation-id" => Copy(
                request,
                requestData: new GiftShopChatRequestData(" "),
                correlationId: " "),
            "correlation-mismatch" => Copy(request, correlationId: "different"),
            "null-state" => Copy(request, forceNullInitialState: true),
            "null-state-collection" => Copy(
                request,
                initialState: new GiftShopChatTurnState(null!, [], [])),
            "prepopulated-cart-action" => Copy(
                request,
                initialState: new GiftShopChatTurnState(
                    [],
                    [new CartAction(CartActionType.Clear)],
                    [])),
            "prepopulated-navigation-action" => Copy(
                request,
                initialState: new GiftShopChatTurnState(
                    [],
                    [],
                    [new NavigationAction(NavigationTarget.Cart, "/cart")])),
            "null-messages" => Copy(request, forceNullMessages: true),
            "empty-messages" => Copy(request, messages: []),
            "multiple-messages" => Copy(
                request,
                messages:
                [
                    new ChatMessage(ChatRole.User, "one"),
                    new ChatMessage(ChatRole.User, "two"),
                ]),
            "assistant-role" => Copy(
                request,
                messages: [new ChatMessage(ChatRole.Assistant, "not a user")]),
            "blank-message" => Copy(
                request,
                messages: [new ChatMessage(ChatRole.User, " ")]),
            "multiple-content-items" => Copy(
                request,
                messages:
                [
                    new ChatMessage(
                        ChatRole.User,
                        [new TextContent("one"), new TextContent("two")]),
                ]),
            "null-options" => Copy(request, forceNullOptions: true),
            "parallel-dispatch" => Copy(
                request,
                options: new DurableTurnOptions
                {
                    DispatchMode = DurableToolDispatchMode.Parallel,
                }),
            "caller-supplied-tools" => Copy(
                request,
                chatOptions: new ChatOptions
                {
                    Tools = [GiftShopChatToolCatalog.CreateDeclarations()[0]],
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null),
        };
    }

    private static DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> Copy(
        DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> request,
        IReadOnlyList<ChatMessage>? messages = null,
        GiftShopChatRequestData? requestData = null,
        GiftShopChatTurnState? initialState = null,
        string? correlationId = null,
        ChatOptions? chatOptions = null,
        DurableTurnOptions? options = null,
        bool forceNullMessages = false,
        bool forceNullRequestData = false,
        bool forceNullInitialState = false,
        bool forceNullOptions = false) =>
        new()
        {
            Messages = forceNullMessages ? null! : messages ?? request.Messages,
            RequestData = forceNullRequestData ? null! : requestData ?? request.RequestData,
            InitialTurnState = forceNullInitialState ? null : initialState ?? request.InitialTurnState,
            CorrelationId = correlationId ?? request.CorrelationId,
            ConversationId = request.ConversationId,
            ChatOptions = chatOptions ?? request.ChatOptions,
            Options = forceNullOptions ? null! : options ?? request.Options,
        };

    private static ChatResponse ToolCalls(params FunctionCallContent[] calls) =>
        new(new ChatMessage(ChatRole.Assistant, calls));

    private static FunctionCallContent Call(
        string callId,
        string name,
        params (string Name, object? Value)[] arguments) =>
        new(
            callId,
            name,
            arguments.ToDictionary(argument => argument.Name, argument => argument.Value));

    private static ChatResponse ToolCallsWithText(
        string text,
        params FunctionCallContent[] calls) =>
        new(new ChatMessage(
            ChatRole.Assistant,
            [new TextContent(text), .. calls]));

    private static ChatResponse Final(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));

    private static string AssertToolsAndReturn(ChatOptions? options, string text)
    {
        options!.Tools.Should().HaveCount(13);
        return text;
    }

    private static string AssertCartToolResults(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options)
    {
        options!.Tools.Should().HaveCount(13);
        var results = messages.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .ToList();
        results.Should().HaveCount(2);
        JsonSerializer.Serialize(results[1].Result).Should().Contain("Keepsake Box");
        JsonSerializer.Serialize(results[1].Result).Should().Contain("x 2");
        return "The cart is ready.";
    }

    private static string ObserveHistoricalProtocol(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        ref bool observed)
    {
        options!.Tools.Should().HaveCount(13);
        observed = messages.SelectMany(message => message.Contents)
                       .OfType<FunctionCallContent>()
                       .Any(call => call.Name == "search_products")
                   && messages.SelectMany(message => message.Contents)
                       .OfType<FunctionResultContent>()
                       .Any(result => result.CallId == "search-1");
        return "Here are a few more details.";
    }

    private static string ObserveIncompleteHistory(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        ref bool observed)
    {
        options!.Tools.Should().HaveCount(13);
        messages.Should().Contain(message =>
            message.Role == ChatRole.Assistant
            && message.Text.Contains("did not produce a complete final response"));
        observed = messages.SelectMany(message => message.Contents)
            .All(content => content is not FunctionCallContent and not FunctionResultContent);
        return "The next turn starts without incomplete tool protocol.";
    }

    private static string ObserveLoyaltyResult(
        IReadOnlyList<ChatMessage> messages,
        ref bool observed)
    {
        observed = messages.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => JsonSerializer.Serialize(result.Result))
            .Any(result => result.Contains("650") && result.Contains("Silver"));
        return "You have 650 Love Tokens.";
    }

    private static async Task<List<string>> GetScheduledActivityTypesAsync(
        WorkflowHandle handle)
    {
        var result = new List<string>();
        await foreach (var historyEvent in handle.FetchHistoryEventsAsync())
        {
            if (historyEvent.ActivityTaskScheduledEventAttributes is { } scheduled)
                result.Add(scheduled.ActivityType.Name);
        }

        return result;
    }

    private static async Task WaitForCallCountAsync(
        ScriptedGiftShopChatClient client,
        int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (client.CallCount >= expected)
                return;

            await Task.Delay(25);
        }

        client.CallCount.Should().BeGreaterThanOrEqualTo(expected);
    }

    private static async Task<string> WaitForContinueAsNewAsync(
        ITemporalClient client,
        string workflowId,
        string initialRunId)
    {
        var firstRun = client.GetWorkflowHandle<GiftShopChatWorkflow>(
            workflowId,
            initialRunId,
            firstExecutionRunId: null);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await foreach (var historyEvent in firstRun.FetchHistoryEventsAsync())
            {
                if (historyEvent.WorkflowExecutionContinuedAsNewEventAttributes is { } continued)
                    return continued.NewExecutionRunId;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The GiftShop chat workflow did not continue as new.");
    }

    private static async Task CaptureReplayHistoryIfRequestedAsync(WorkflowHandle handle)
    {
        var outputPath = Environment.GetEnvironmentVariable("GIFT_SHOP_CHAT_HISTORY_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        await handle.GetResultAsync();
        var history = await handle.FetchHistoryAsync();
        await File.WriteAllTextAsync(outputPath, history.ToJson());
    }

    private static bool IsDescendantOf(
        Activity descendant,
        Activity ancestor,
        IReadOnlyCollection<Activity> trace)
    {
        var spansById = trace.ToDictionary(activity => activity.SpanId);
        var parentId = descendant.ParentSpanId;
        while (parentId != default)
        {
            if (parentId == ancestor.SpanId)
                return true;
            if (!spansById.TryGetValue(parentId, out var parent))
                return false;

            parentId = parent.ParentSpanId;
        }

        return false;
    }

    private sealed class WorkflowHandleChatClient(
        WorkflowHandle<GiftShopChatWorkflow> handle) : WebWorkflowClient
    {
        public Task EnsureStartedAsync(string workflowId) => Task.CompletedTask;

        public Task<DurableTurnResult<GiftShopChatTurnState>> SendMessageAsync(
            string workflowId,
            string operationId,
            DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> request) =>
            handle.ExecuteUpdateAsync(
                workflow => workflow.SendMessageAsync(request),
                new WorkflowUpdateOptions { Id = operationId });

        public Task<IReadOnlyList<TemporalCommunity.Extensions.AI.Session.DurableSessionEntry>>
            GetHistoryAsync(string workflowId) =>
            handle.QueryAsync(workflow => workflow.GetHistory());

        public async Task ShutdownAsync(
            string workflowId,
            CancellationToken cancellationToken = default)
        {
            await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
            await handle.GetResultAsync();
        }
    }
}

internal sealed class CollectingActivityExporter(ConcurrentBag<Activity> completed)
    : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
            completed.Add(activity);

        return ExportResult.Success;
    }
}
