namespace WithLove.Workflows.Chat;

/// <summary>Sent from client to workflow update.</summary>
public record ChatRequest(string UserMessage, List<CartSnapshot>? Cart = null, UserContext? User = null);

/// <summary>Authenticated user identity passed to the AI for personalisation.</summary>
public record UserContext(string? Name, string? Email);

/// <summary>Returned from workflow update to client.</summary>
public record ChatResponse(string AssistantMessage, List<CartAction> CartActions, List<NavigationAction> NavigationActions);

/// <summary>Cart mutations collected by tools, applied client-side.</summary>
public record CartAction(
    CartActionType Type,
    int ProductId = 0,
    string ProductName = "",
    string ImageUrl = "",
    decimal Price = 0,
    string StripePriceId = "",
    int Quantity = 1);

public enum CartActionType { Add, Remove, Clear }

/// <summary>Lightweight snapshot of a cart item, passed from Web to workflow/activity.</summary>
public record CartSnapshot(int ProductId, string ProductName, decimal Price, int Quantity);

/// <summary>Single entry in conversation history (for query and activity input).</summary>
public record ChatHistoryEntry(bool IsUser, string Text, DateTime Timestamp);

/// <summary>Input for the AI inference activity.</summary>
public record ChatInferenceInput(
    List<ChatHistoryEntry> History,
    string UserMessage,
    List<CartSnapshot>? Cart = null,
    UserContext? User = null);

/// <summary>Output from the AI inference activity.</summary>
public record ChatInferenceResult(string AssistantMessage, List<CartAction> CartActions, List<NavigationAction> NavigationActions);

/// <summary>Navigation request emitted by tools, executed client-side.</summary>
public record NavigationAction(NavigationTarget Target, string Url, string? Label = null);

public enum NavigationTarget { Product, Collection, Cart, Checkout }
