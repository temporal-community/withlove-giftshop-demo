using Microsoft.AspNetCore.Components;

namespace WithLove.Web.Services;

/// <summary>
/// Scoped service that bridges the HTTP phase (where the wl-chat-id cookie is read or minted)
/// to the Blazor SignalR circuit (where the chat session workflow ID is derived).
/// Populated by AnonymousChatMiddleware during the initial HTTP request.
/// [PersistentState] ensures ChatId survives the prerender→circuit transition.
/// </summary>
/// <remarks>
/// Separate from <see cref="AnonymousCartSession"/> on purpose. A cart must <em>survive</em> login
/// so the anonymous cart can be merged into the account; a chat identity must <em>rotate</em> at
/// login and logout so an anonymous transcript never spans an authentication boundary. One cookie
/// cannot do both without becoming two cookies with extra parsing.
/// </remarks>
public sealed class AnonymousChatSession
{
    [PersistentState]
    public string ChatId { get; set; } = string.Empty;

    public void Initialize(string chatId) => ChatId = chatId;
}
