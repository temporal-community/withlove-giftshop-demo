using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.AI;
using TemporalCommunity.Extensions.AI;
using WithLove.Web.Models;
using WithLove.OpenInference;
using WithLove.OpenInference.Spans;
using WithLove.Web.Telemetry;
using WithLove.Workflows;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Workflows;

namespace WithLove.Web.Services;

/// <summary>Result from SendMessageAsync containing the response and any navigation requests.</summary>
public record ChatMessageResult(
    string AssistantMessage,
    List<NavigationAction> NavigationActions,
    string OperationId);

/// <summary>
/// Scoped service (one per SignalR circuit) that bridges Blazor UI with the Temporal chat workflow.
/// </summary>
public class ChatService(
    IGiftShopChatWorkflowClient workflowClient,
    AuthenticationStateProvider authStateProvider,
    ICartService cartService,
    AnonymousChatSession anonymousChatSession,
    Instrumentation instrumentation,
    TelemetryIdentity telemetryIdentity,
    OpenInferenceTraceConfig openInferenceTraceConfig,
    ILogger<ChatService> logger)
{
    private const int MaxOutputTokens = 4000;

    /// <summary>
    /// Upper bound on how long End Chat waits for the run to finish closing.
    /// </summary>
    /// <remarks>
    /// A shutdown signal only has to wake the workflow and let it unwind, so this is generous. If
    /// it is ever hit, the workflow's own time-to-live remains the final cleanup bound; the user is
    /// not made to wait on it.
    /// </remarks>
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(5);

    private string? _workflowId;
    private bool _initialized;
    private UserContext? _userContext;
    private string? _sessionTelemetryId;
    private string? _userTelemetryId;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    /// <summary>Chat messages for UI rendering.</summary>
    public List<ChatHistoryEntry> Messages { get; } = [];

    /// <summary>Whether a message is currently being processed.</summary>
    public bool IsThinking { get; set; }

    /// <summary>Determines the session workflow ID based on authentication state.</summary>
    public async Task InitializeAsync()
    {
        await _initializationGate.WaitAsync();
        try
        {
            if (_initialized)
                return;

            logger.ChatSessionInitializationStarted();

            try
            {
                var auth = await authStateProvider.GetAuthenticationStateAsync();
                var userId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // Built through GiftShopChatWorkflow.WorkflowIdFor so the session ID has exactly one
                // definition. The workflow's update validator rejects a turn whose UserContext.UserId does
                // not resolve to Workflow.Info.WorkflowId, so a second copy of this format string here
                // would break every authenticated chat the moment the two drifted.
                // An anonymous session has no user identity to bind, so it is keyed by the unguessable
                // 128-bit value carried in the wl-chat-id cookie, which intentionally cannot collide with
                // any real user ID. Reading it from the cookie rather than minting one per circuit is what
                // makes an anonymous conversation survive an F5, a second tab, or a dropped circuit.
                _workflowId = userId is not null
                    ? GiftShopChatWorkflow.WorkflowIdFor(userId)
                    : GiftShopChatWorkflow.WorkflowIdFor($"anon-{RequireAnonymousChatId()}");
                _sessionTelemetryId = telemetryIdentity.ForSession(_workflowId);
                _userTelemetryId = userId is null ? null : telemetryIdentity.ForUser(userId);

                var name = auth.User.FindFirst(ClaimTypes.Name)?.Value
                           ?? auth.User.FindFirst(ClaimTypes.GivenName)?.Value;

                // The email claim is deliberately not read. UserContext is serialized into Temporal
                // workflow history on every model step and every tool call, and history is append-only —
                // so only data with a real server-side consumer is allowed to travel on it.
                if (name is not null || userId is not null)
                    _userContext = new UserContext(name, userId);

                instrumentation.ChatSessionsStarted.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "auth_type",
                        userId is not null ? "authenticated" : "anonymous"));

                _initialized = true;
                logger.ChatSessionInitializationCompleted();
            }
            catch (Exception exception)
            {
                ResetInitializationState();
                logger.ChatSessionInitializationFailed(exception);
                throw;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    /// <summary>
    /// Returns the anonymous chat identity established during the HTTP request, or throws.
    /// </summary>
    /// <remarks>
    /// There is deliberately no fallback here. The obvious one — mint a GUID when the cookie value
    /// did not arrive, as <c>FusionCacheCartService</c> does for the cart — is the negation of this
    /// feature: every visitor would get a fresh workflow and stable identity would appear
    /// implemented while nothing reported that it was not.
    /// <para>
    /// An empty <c>ChatId</c> is not a user-facing condition. It means the middleware is
    /// unregistered, the persistent-state registration was dropped, or the middleware was placed
    /// after <c>MapRazorComponents</c> — a wiring bug that reproduces on the first page load in
    /// development and never in production if development is correct. The throw is already
    /// contained: <c>ChatFab.OpenChat</c> catches it, so only chat breaks, and it renders the
    /// unavailable state rather than a working-looking empty panel.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The anonymous chat identity is missing.</exception>
    private string RequireAnonymousChatId()
    {
        if (!string.IsNullOrEmpty(anonymousChatSession.ChatId))
            return anonymousChatSession.ChatId;

        instrumentation.ChatSessionIdentityFailures.Add(1);

        throw new InvalidOperationException(
            "The anonymous chat identity is missing, so a stable chat session cannot be derived. "
            + $"Check that AnonymousChatMiddleware is registered before MapRazorComponents and "
            + $"that {nameof(AnonymousChatSession)} is registered with RegisterPersistentService.");
    }

    /// <summary>Starts the workflow, or returns the currently running session.</summary>
    public async Task EnsureWorkflowStartedAsync()
    {
        if (_workflowId is null)
            throw new InvalidOperationException("Call InitializeAsync first.");
        if (_sessionTelemetryId is null)
            throw new InvalidOperationException("Telemetry identity was not initialized.");

        await workflowClient.EnsureStartedAsync(_workflowId);
    }

    /// <summary>Loads visible conversation history from Temporal for reconnect hydration.</summary>
    public async Task LoadHistoryAsync()
    {
        if (_workflowId is null)
            return;

        try
        {
            var history = await workflowClient.GetHistoryAsync(_workflowId);

            Messages.Clear();
            Messages.AddRange(GiftShopChatResponseProjector.ProjectHistory(history));
        }
        catch (Temporalio.Exceptions.RpcException exception)
            when (exception.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            // The workflow does not exist yet, so there is no history to hydrate. This is the
            // ordinary path now that the run starts lazily on the first message.
        }
        catch (Temporalio.Exceptions.WorkflowQueryRejectedException)
        {
            // The run under this ID is closed — ended by the customer, expired, or terminated — so
            // its transcript is no longer something the model can see. Leaving Messages empty is
            // the whole point of the NotOpen reject condition; see GetHistoryAsync.
        }
    }

    /// <summary>
    /// Sends one durable turn. The caller adds the user message to <see cref="Messages"/> before
    /// invoking this method so the UI can render it immediately.
    /// </summary>
    public async Task<ChatMessageResult> SendMessageAsync(string message)
    {
        // Correlation only — this is a telemetry and log-stitching key, not an idempotency key.
        // A fresh GUID per call can never deduplicate anything, so it must not be used as a
        // Temporal Update ID; doing so advertises a safety guarantee that does not exist.
        var operationId = Guid.NewGuid().ToString("N");
        logger.ChatTurnRequested(operationId, initializationRequired: !_initialized);
        await InitializeAsync();

        if (_workflowId is null)
            throw new InvalidOperationException("Chat initialization completed without a workflow ID.");

        var completion = "Failed";
        UsageDetails? usage = null;
        var stopwatch = Stopwatch.StartNew();
        using var context = OpenInferenceContextScope.Push(new OpenInferenceContextValues
        {
            SessionId = _sessionTelemetryId,
            UserId = _userTelemetryId,
            Tags = ["withlove", "chat"],
        });
        using var chain = instrumentation.ActivitySource.StartChain(
            "chat.turn",
            message,
            openInferenceTraceConfig);
        var activity = chain.Activity;
        activity?.SetTag("chat.operation_id", operationId);

        try
        {
            await EnsureWorkflowStartedAsync();

            var cartSnapshot = cartService.Items
                .Select(item => new CartSnapshot(
                    item.ProductId,
                    item.ProductName,
                    item.Price,
                    item.Quantity))
                .ToArray();

            var request = new DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState>
            {
                Messages = [new ChatMessage(ChatRole.User, message)],
                RequestData = new GiftShopChatRequestData(operationId, _userContext),
                InitialTurnState = GiftShopChatTurnState.Create(cartSnapshot),
                CorrelationId = operationId,
                // This value becomes conversation.id on durable-AI spans. It must be the safe
                // session pseudonym; the raw workflow ID remains exclusively the Temporal route.
                ConversationId = _sessionTelemetryId,
                ChatOptions = new ChatOptions
                {
                    Instructions = GiftShopChatPrompt.BuildInstructions(_userContext),
                    ModelId = WorkflowConstants.ChatModelId,

                    // Always bound the output budget. A single turn runs up to the workflow's
                    // 40-iteration tool cap, so an unbounded step does not cost one runaway
                    // generation — it costs forty, each retried up to three times by Temporal.
                    // The previous 2,000-token ceiling was observed to be fully consumed by
                    // reasoning before any visible assistant text was produced. Doubling it gives
                    // the model room to finish while retaining a firm per-step cost bound.
                    //
                    // Temperature is intentionally left unset: reasoning models reject or ignore
                    // sampling parameters, so pinning it here would be misleading at best.
                    MaxOutputTokens = MaxOutputTokens,
                    Reasoning = new ReasoningOptions
                    {
                        Effort = ReasoningEffort.Low,
                        Output = ReasoningOutput.None,
                    },
                }.WithChatClientTag("chat.operation_id", operationId),
                Options = new DurableTurnOptions
                {
                    DispatchMode = DurableToolDispatchMode.Sequential,
                },
            };

            var result = await workflowClient.SendMessageAsync(
                _workflowId,
                operationId,
                request);

            // Summed by the package across every model step in this turn, so it is the whole cost
            // of the turn and not just its last call. Recorded in the finally block so the
            // completion reason tag is the same one the duration histogram carries.
            usage = result.Response.Usage;

            var assistantMessage = result.CompletionReason ==
                DurableTurnCompletionReason.IncompleteResponse
                    ? GiftShopChatResponseProjector.AssistantFallback
                    : GiftShopChatResponseProjector.GetDisplayAssistantText(
                        result.Response.Messages);

            Messages.Add(new ChatHistoryEntry(false, assistantMessage, DateTime.UtcNow));

            var navigationActions = new List<NavigationAction>();
            if (result.CompletionReason == DurableTurnCompletionReason.FinalResponse
                && result.FinalTurnState is { } finalState)
            {
                await ApplyCartActionsAsync(finalState.CartActions);
                navigationActions.AddRange(finalState.NavigationActions);
            }

            completion = result.CompletionReason.ToString();
            chain.Complete(assistantMessage);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new ChatMessageResult(assistantMessage, navigationActions, operationId);
        }
        catch (Exception exception)
        {
            chain.Fail(exception, escaped: true);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            activity?.SetTag("chat.completion_reason", completion);
            if (completion == "Failed")
                activity?.SetStatus(ActivityStatusCode.Error);
            var completionTag = new KeyValuePair<string, object?>("completion_reason", completion);
            instrumentation.ChatTurnDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                completionTag);
            RecordTokenUsage(usage, completionTag);
            IsThinking = false;
        }
    }

    /// <summary>
    /// Records the aggregated token cost of one durable turn.
    /// </summary>
    private void RecordTokenUsage(
        UsageDetails? usage,
        KeyValuePair<string, object?> completionTag)
    {
        if (usage is null)
        {
            instrumentation.ChatTurnsWithoutUsage.Add(1, completionTag);
            return;
        }

        var inputTokens = usage.InputTokenCount.GetValueOrDefault();
        var outputTokens = usage.OutputTokenCount.GetValueOrDefault();

        instrumentation.ChatTokensUsed.Add(
            inputTokens,
            new KeyValuePair<string, object?>("token_type", "input"),
            completionTag);
        instrumentation.ChatTokensUsed.Add(
            outputTokens,
            new KeyValuePair<string, object?>("token_type", "output"),
            completionTag);

        // The package sums InputTokenCount, OutputTokenCount and TotalTokenCount across steps and
        // nothing else, so a provider that reports no total leaves this at zero rather than null.
        // CachedInputTokenCount and ReasoningTokenCount survive only on the per-step gen_ai spans —
        // they are deliberately not broken out here, because a turn-level cached-token series would
        // read as a flat zero and imply this workload never hits the prompt cache.
        var totalTokens = usage.TotalTokenCount.GetValueOrDefault();
        if (totalTokens == 0)
            totalTokens = inputTokens + outputTokens;

        instrumentation.ChatTurnTokens.Record(totalTokens, completionTag);
    }

    /// <summary>Ends the chat session by shutting the package workflow down.</summary>
    /// <remarks>
    /// The session ID is deliberately <em>not</em> rotated here. It cannot be — <c>wl-chat-id</c> is
    /// <c>HttpOnly</c> and this runs inside a SignalR circuit with no <c>HttpResponse</c> — and it
    /// does not need to be: closing the run is enough, because <c>GetHistoryAsync</c> refuses to
    /// read a closed run, so reopening the panel under the same ID shows nothing.
    /// </remarks>
    public async Task EndSessionAsync()
    {
        if (_workflowId is null)
            return;

        try
        {
            using var budget = new CancellationTokenSource(ShutdownBudget);
            await workflowClient.ShutdownAsync(_workflowId, budget.Token);
        }
        catch (Temporalio.Exceptions.RpcException)
        {
            // The workflow may already be completed, or never have started at all.
        }
        catch (OperationCanceledException)
        {
            // Cancellation can happen while sending the signal or while waiting for the run to
            // close, so delivery is not assumed. The customer asked to clear the conversation,
            // therefore clear it — the workflow TTL is the fallback cleanup bound if Temporal is
            // slow or unavailable.
        }

        Messages.Clear();
        ResetInitializationState();
    }

    private void ResetInitializationState()
    {
        _workflowId = null;
        _sessionTelemetryId = null;
        _userTelemetryId = null;
        _userContext = null;
        _initialized = false;
    }

    private async Task ApplyCartActionsAsync(IReadOnlyList<CartAction> actions)
    {
        foreach (var action in actions)
        {
            instrumentation.ChatCartActions.Add(
                1,
                new KeyValuePair<string, object?>("action", action.Type.ToString()));

            switch (action.Type)
            {
                case CartActionType.Add:
                    await cartService.AddItemAsync(new CartItem
                    {
                        ProductId = action.ProductId,
                        ProductName = action.ProductName,
                        ImageUrl = action.ImageUrl,
                        Price = action.Price,
                        StripePriceId = action.StripePriceId,
                        Quantity = action.Quantity,
                    });
                    break;

                case CartActionType.Remove:
                    await cartService.RemoveItemAsync(action.ProductId);
                    break;

                case CartActionType.Clear:
                    await cartService.ClearAsync();
                    break;
            }
        }
    }
}
