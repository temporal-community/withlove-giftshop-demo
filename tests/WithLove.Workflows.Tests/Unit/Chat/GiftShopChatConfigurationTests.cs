using System.Globalization;
using Microsoft.Extensions.AI;
using TemporalCommunity.Extensions.AI;
using TemporalCommunity.Extensions.AI.Session;

namespace WithLove.Workflows.Tests.Unit.Chat;

public class GiftShopChatConfigurationTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void ConfigureDurableExecution_FreezesGiftShopBoundaries()
    {
        var options = new DurableExecutionOptions { TaskQueue = WorkflowConstants.DefaultTaskQueue };

        GiftShopChatRegistrationExtensions.ConfigureDurableExecution(options);

        options.RegisterDefaultWorkflow.Should().BeFalse();
        options.WorkflowIdPrefix.Should().Be("giftshop-chat-");
        options.SessionTimeToLive.Should().Be(TimeSpan.FromHours(24));
        options.ActivityTimeout.Should().Be(TimeSpan.FromMinutes(2));
        options.HeartbeatTimeout.Should().Be(TimeSpan.FromMinutes(2));
        options.MaxToolCallsPerTurn.Should().Be(40);
        options.MaximumConsecutiveErrorsPerRequest.Should().Be(3);
        options.MaxEntryCount.Should().Be(1000);
        // Enabled deliberately: TurnCount/SessionCreatedAt distinguish a started session with no
        // completed turn from a real conversation. Requires both attributes registered in every
        // namespace the workflow runs in — see the AppHost and GiftShopChatTemporalFixture.
        options.EnableSearchAttributes.Should().BeTrue();
        options.IncludeDetailedErrors.Should().BeFalse();
        options.RetryPolicy.Should().NotBeNull();
        options.RetryPolicy!.InitialInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.RetryPolicy.BackoffCoefficient.Should().Be(2.0f);
        options.RetryPolicy.MaximumInterval.Should().Be(TimeSpan.FromSeconds(30));
        options.RetryPolicy.MaximumAttempts.Should().Be(3);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void CreateDeclarations_HasStableNamesAndDescriptions()
    {
        var expected = new (string Name, string Description)[]
        {
            ("search_products", "Search for products by name or description. Use this to find gifts matching customer needs."),
            ("get_product_details", "Get full details for a specific product including materials, features, and story."),
            ("get_categories", "Get all product collections (categories) available in the shop."),
            ("browse_category", "Browse products in a specific collection (category) by ID."),
            ("add_to_cart", "Add a product to the customer's cart. Use the exact product ID from search or browse results."),
            ("remove_from_cart", "Remove one or more products from the cart. Use exact product IDs from view_cart."),
            ("view_cart", "View the current contents of the customer's cart. Call this before answering questions about the cart."),
            ("clear_cart", "Empty the entire cart. Use this when the customer wants to start fresh or remove everything."),
            ("navigate_to_product", "Navigate the customer to a product detail page."),
            ("navigate_to_collection", "Navigate the customer to a product collection (category) page. ALWAYS call get_categories first to find the numeric category ID — never guess it."),
            ("navigate_to_cart", "Navigate the customer to their cart page."),
            ("navigate_to_checkout", "Navigate the customer to the checkout page."),
            ("view_loyalty_points", "Get the current customer's Love Tokens balance, tier, and progress toward the next tier. Use when the customer asks about their points, balance, rewards, or tier status."),
        };

        var declarations = GiftShopChatToolCatalog.CreateDeclarations();

        declarations.Select(declaration => (declaration.Name, declaration.Description))
            .Should().Equal(expected);
        declarations.Should().OnlyContain(declaration => declaration.AdditionalProperties.Count == 0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void BuildInstructions_AddsCustomerContextWithoutChangingToolConfiguration()
    {
        var instructions = GiftShopChatPrompt.BuildInstructions(
            new UserContext("Avery", "user-7"));

        instructions.Should().Contain("You are LA");
        instructions.Should().Contain("<customer_name>Avery</customer_name>");
        instructions.Should().NotContain("user-7");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    /// <summary>
    /// The prompt pins catalogue fidelity and says nothing whatever about images.
    /// </summary>
    /// <remarks>
    /// This test previously asserted the presence of an explicit "Do not include image URLs"
    /// instruction, which existed only because <c>FormatProductSummary</c> emitted an
    /// <c>"| Image: {url}"</c> field into tool results that the model then had to be told to
    /// discard. Now that the catalogue no longer emits the URL, the instruction has no referent —
    /// and a rule describing a field the model never sees is a standing invitation to mention one.
    /// The assertion is therefore inverted: presentation is the catalogue's job, so the prompt is
    /// expected to be silent on it. What the prompt must still carry is name fidelity, because a
    /// paraphrased product name is unrecoverable once the customer acts on it.
    /// </remarks>
    public void BuildInstructions_LeavesProductPresentationToTheCatalog()
    {
        var instructions = GiftShopChatPrompt.BuildInstructions(null);

        instructions.Should().Contain("copy each product name exactly from the tool result");
        instructions.Should().NotContain("image URL");
        instructions.Should().NotContain("Image:");
        instructions.Should().NotContain("markdown image");
        instructions.Should().NotContain("![Product Name]");
    }

    /// <summary>
    /// The tier table and redemption rate the prompt quotes are projections of
    /// <see cref="LoyaltyContracts"/>, not a second copy of the numbers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prompt states the tiers so LA can answer "what is the next tier" without a tool call,
    /// which means the same three numbers are quoted here and enforced in
    /// <c>LoyaltyState.Tier</c> and <c>LoyaltyAccountWorkflow</c>. Prose does not fail a build: if
    /// the thresholds move and the prompt holds literals, LA keeps confidently quoting the old
    /// table to customers and nothing anywhere goes red.
    /// </para>
    /// <para>
    /// This test is the thing that goes red. Because the expectations are built from the constants,
    /// it stays green when a threshold legitimately changes <i>and the prompt is a projection</i>,
    /// and fails the moment the prompt stops deriving from them — which is the only point at which
    /// the drift is still cheap to fix.
    /// </para>
    /// <para>
    /// Expectations are formatted with <see cref="CultureInfo.InvariantCulture"/> to match how the
    /// prompt builds them. That is not merely cosmetic: on a machine whose culture uses a different
    /// group separator, a prompt that stopped pinning the culture would render "1.999" and fail
    /// here — the prompt is a shared cache prefix, so it must be byte-identical across workers.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void BuildInstructions_ProjectsLoyaltyThresholdsFromLoyaltyContracts()
    {
        var instructions = GiftShopChatPrompt.BuildInstructions(null);

        instructions.Should().Contain(string.Create(
            CultureInfo.InvariantCulture,
            $"Bronze (0–{LoyaltyContracts.SilverThreshold - 1:N0} lifetime pts)"));
        instructions.Should().Contain(string.Create(
            CultureInfo.InvariantCulture,
            $"Silver ({LoyaltyContracts.SilverThreshold:N0}–{LoyaltyContracts.GoldThreshold - 1:N0})"));
        instructions.Should().Contain(string.Create(
            CultureInfo.InvariantCulture,
            $"Gold ({LoyaltyContracts.GoldThreshold:N0}+)"));
        instructions.Should().Contain(string.Create(
            CultureInfo.InvariantCulture,
            $"{LoyaltyContracts.PointsPerDiscountDollar:N0} tokens = $1 off"));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void ProjectHistory_HidesToolProtocolAndUsesLastAssistantText()
    {
        var timestamp = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var request = DurableSessionRequest.FromMessages(
            [new ChatMessage(ChatRole.User, "Show me a gift") { CreatedAt = timestamp }],
            "turn-1",
            timestamp);
        var response = DurableSessionResponse.FromChatResponse(
            "turn-1",
            new ChatResponse(
            [
                new ChatMessage(ChatRole.Assistant, "Let me look."),
                new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "call-1",
                        "search_products",
                        new Dictionary<string, object?> { ["query"] = "gift" }),
                ]),
                new ChatMessage(ChatRole.Tool,
                [
                    new FunctionResultContent("call-1", "internal product result"),
                ]),
                new ChatMessage(ChatRole.Assistant, "This keepsake would be lovely."),
            ]),
            timestamp);

        var projected = GiftShopChatResponseProjector.ProjectHistory([request, response]);

        projected.Should().Equal(
            new ChatHistoryEntry(true, "Show me a gift", timestamp.UtcDateTime),
            new ChatHistoryEntry(false, "This keepsake would be lovely.", timestamp.UtcDateTime));
        projected.Should().NotContain(entry => entry.Text.Contains("internal product result"));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void ProjectHistory_MapsIncompleteResponseSentinelToCustomerFallback()
    {
        var timestamp = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var response = DurableSessionResponse.FromChatResponse(
            "incomplete-1",
            new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                "The model did not produce a complete final response."))
            {
                FinishReason = ChatFinishReason.Length,
            },
            timestamp,
            DurableTurnCompletionReason.IncompleteResponse);

        var projected = GiftShopChatResponseProjector.ProjectHistory([response]);

        projected.Should().ContainSingle().Which.Text.Should().Be(
            GiftShopChatResponseProjector.AssistantFallback);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void IterationLimitMessage_MatchesPackageContractForConfiguredBoundary()
    {
        GiftShopChatResponseProjector.IterationLimitMessage.Should().Be(
            "Maximum tool-call iterations (40) exceeded; " +
            "the conversation did not converge on a final answer.");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Validator_RejectsNullOptionsBeforeManagedTurnExecution()
    {
        var workflow = new GiftShopChatWorkflow();
        var invalid = new DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>
        {
            Messages = [new ChatMessage(ChatRole.User, "Hello")],
            RequestData = new GiftShopChatRequestData("null-options"),
            InitialTurnState = GiftShopChatTurnState.Create([]),
            CorrelationId = "null-options",
            Options = null!,
        };

        var validate = () => workflow.ValidateSendMessage(invalid);

        validate.Should().Throw<ArgumentException>()
            .WithMessage("GiftShop tools must run sequentially.*");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Validator_RejectsCallerSuppliedToolsBeforeManagedTurnExecution()
    {
        var workflow = new GiftShopChatWorkflow();
        var invalid = new DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>
        {
            Messages = [new ChatMessage(ChatRole.User, "Hello")],
            RequestData = new GiftShopChatRequestData("caller-tools"),
            InitialTurnState = GiftShopChatTurnState.Create([]),
            CorrelationId = "caller-tools",
            ChatOptions = new ChatOptions
            {
                Tools = [GiftShopChatToolCatalog.CreateDeclarations()[0]],
            },
            Options = new DurableTurnOptions
            {
                DispatchMode = DurableToolDispatchMode.Sequential,
            },
        };

        var validate = () => workflow.ValidateSendMessage(invalid);

        validate.Should().Throw<ArgumentException>()
            .WithMessage("Caller-supplied tools are not supported.*");
    }
}
