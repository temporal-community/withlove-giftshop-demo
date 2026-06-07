using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using WithLove.Web.Models;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Workflows;

namespace WithLove.Web.Services;

/// <summary>Result from SendMessageAsync containing the response and any navigation requests.</summary>
public record ChatMessageResult(string AssistantMessage, List<NavigationAction> NavigationActions);

/// <summary>
/// Scoped service (one per SignalR circuit) that bridges Blazor UI with the Temporal chat workflow.
/// </summary>
public class ChatService(
    ITemporalClient temporalClient,
    AuthenticationStateProvider authStateProvider,
    ICartService cartService)
{
    private string? _workflowId;
    private bool _initialized;
    private UserContext? _userContext;

    /// <summary>Chat messages for UI rendering.</summary>
    public List<ChatHistoryEntry> Messages { get; } = [];

    /// <summary>Whether a message is currently being processed.</summary>
    public bool IsThinking { get; set; }

    /// <summary>
    /// Determines session workflow ID based on auth state.
    /// Authenticated: chat-{userId} (resumable). Anonymous: chat-anon-{guid} (ephemeral).
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        var auth = await authStateProvider.GetAuthenticationStateAsync();
        var userId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        _workflowId = userId is not null
            ? $"chat-{userId}"
            : $"chat-anon-{Guid.NewGuid():N}";

        var name  = auth.User.FindFirst(ClaimTypes.Name)?.Value
                 ?? auth.User.FindFirst(ClaimTypes.GivenName)?.Value;
        var email = auth.User.FindFirst(ClaimTypes.Email)?.Value;
        if (name is not null || email is not null || userId is not null)
            _userContext = new UserContext(name, email, userId);

        _initialized = true;
    }

    /// <summary>
    /// Starts the workflow (or reuses existing) and loads history for reconnect.
    /// </summary>
    public async Task EnsureWorkflowStartedAsync()
    {
        if (_workflowId is null)
            throw new InvalidOperationException("Call InitializeAsync first.");

        await temporalClient.StartWorkflowAsync(
            (ChatAgentWorkflow wf) => wf.RunAsync(null),
            new WorkflowOptions(id: _workflowId, taskQueue: "with-love-tasks")
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting
            });
    }

    /// <summary>
    /// Loads conversation history from Temporal (for UI hydration on reconnect).
    /// </summary>
    public async Task LoadHistoryAsync()
    {
        if (_workflowId is null) return;

        try
        {
            var handle = temporalClient.GetWorkflowHandle<ChatAgentWorkflow>(_workflowId);
            var history = await handle.QueryAsync(wf => wf.GetHistory());

            Messages.Clear();
            Messages.AddRange(history);
        }
        catch (Temporalio.Exceptions.RpcException ex) when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            // Workflow doesn't exist yet — no history to load
        }
    }

    /// <summary>
    /// Sends a message to the chat workflow and returns the assistant response and any navigation actions.
    /// Caller is responsible for adding the user message to Messages and setting
    /// IsThinking before calling this method so the UI can update immediately.
    /// Applies any cart actions locally.
    /// </summary>
    public async Task<ChatMessageResult> SendMessageAsync(string message)
    {
        if (_workflowId is null)
            throw new InvalidOperationException("Call InitializeAsync first.");

        try
        {
            await EnsureWorkflowStartedAsync();

            // Build cart snapshot for the AI to reference (view_cart, remove, clear)
            var cartSnapshot = cartService.Items
                .Select(i => new CartSnapshot(i.ProductId, i.ProductName, i.Price, i.Quantity))
                .ToList();

            var handle = temporalClient.GetWorkflowHandle<ChatAgentWorkflow>(_workflowId);
            var response = await handle.ExecuteUpdateAsync(
                wf => wf.SendMessageAsync(new ChatRequest(message, cartSnapshot, _userContext)));

            // Add assistant response to local list
            Messages.Add(new ChatHistoryEntry(false, response.AssistantMessage, DateTime.UtcNow));

            // Apply cart actions locally
            await ApplyCartActionsAsync(response.CartActions);

            return new ChatMessageResult(response.AssistantMessage, response.NavigationActions);
        }
        finally
        {
            IsThinking = false;
        }
    }

    /// <summary>
    /// Ends the chat session by signaling the workflow.
    /// </summary>
    public async Task EndSessionAsync()
    {
        if (_workflowId is null) return;

        try
        {
            var handle = temporalClient.GetWorkflowHandle<ChatAgentWorkflow>(_workflowId);
            await handle.SignalAsync(wf => wf.EndSessionAsync());
        }
        catch (Temporalio.Exceptions.RpcException)
        {
            // Workflow may already be completed
        }

        Messages.Clear();
        _workflowId = null;
        _initialized = false;
    }

    private async Task ApplyCartActionsAsync(List<CartAction> actions)
    {
        foreach (var action in actions)
        {
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
                        Quantity = action.Quantity
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
