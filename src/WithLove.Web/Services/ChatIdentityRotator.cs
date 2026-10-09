using Temporalio.Exceptions;
using WithLove.Web.Middleware;
using WithLove.Workflows.Workflows;

namespace WithLove.Web.Services;

/// <summary>
/// Rotates the anonymous chat identity at an authentication boundary and shuts down the session it
/// replaces.
/// </summary>
/// <remarks>
/// <para>
/// The invariant this exists to hold is one sentence: <em>an anonymous chat identity never spans an
/// authentication boundary.</em> Logout alone would prevent leakage across a shared browser, but
/// rotating at login too means a reader does not have to work out which boundary is load-bearing.
/// </para>
/// <para>
/// Called from exactly two places, both of which are already full HTTP round-trips and can
/// therefore write a <c>Set-Cookie</c> header: <c>Login.razor</c> on a successful sign-in, and the
/// <c>/logout</c> endpoint. End Chat deliberately does <em>not</em> rotate — it runs inside a
/// SignalR circuit where there is no <c>HttpResponse</c> to write an <c>HttpOnly</c> cookie to, and
/// it does not need to: shutting the run down is enough once the history query refuses to read a
/// closed run.
/// </para>
/// </remarks>
public sealed class ChatIdentityRotator(
    IGiftShopChatWorkflowClient workflowClient,
    ILogger<ChatIdentityRotator> logger)
{
    /// <summary>
    /// Upper bound on how long shutting down the replaced session may delay the user.
    /// </summary>
    /// <remarks>
    /// A logout must never fail, or noticeably stall, because the AI chat backend is unavailable.
    /// An orphaned workflow that reaps itself within 24 hours is a far better outcome than a
    /// customer who cannot sign out.
    /// </remarks>
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Issues a fresh <c>wl-chat-id</c> and asks the session it replaced to shut down.
    /// </summary>
    public async Task RotateAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A cookie can only be written while the response headers are still ours to change. Both
        // call sites satisfy that today — a minimal API handler and a static-SSR form post — but
        // adding [StreamRendering] to the login page would quietly stop satisfying it, and the
        // resulting failure would be an anonymous chat identity surviving an authentication
        // boundary. Loud, and still not fatal to the sign-in or sign-out the user asked for.
        if (context.Response.HasStarted)
        {
            logger.ChatIdentityRotationSkipped();
            return;
        }

        // Read before minting. The ordering looks load-bearing and is not: TryRead reads
        // context.Request.Cookies, parsed once from the inbound header, while Mint only appends a
        // Set-Cookie header to the response — neither collection can disturb the other. A mutation
        // test that swapped these two lines passed the suite unchanged. The order is kept because
        // read-then-replace is what a reader expects and it costs nothing, not because reversing it
        // would lose the handle to the workflow being shut down.
        //
        // Mint is idempotent per request, and this is the call site that needs it. When the request
        // arrived without a well-formed cookie, AnonymousChatMiddleware has already minted one and
        // handed it to AnonymousChatSession; this call then returns that same value and writes
        // nothing further, so the response carries exactly one Set-Cookie: wl-chat-id and the
        // circuit's identity is the one the browser keeps. When the request did carry an identity
        // the middleware minted nothing, so this is the first Mint of the request and issues a new
        // value — rotation still replaces, it just no longer duplicates.
        var previousChatId = ChatIdentityCookie.TryRead(context);
        ChatIdentityCookie.Mint(context);

        // No inbound cookie means no pointer to a previous run, so there is nothing to shut down —
        // not a shutdown that is being skipped. A visitor whose cookie expired or was cleared mid
        // session leaves their workflow running, and it is unreachable: the ID was derived from a
        // value now lost on both sides. Those runs are reaped by the workflow's own 24-hour
        // lifetime. Recovering them would mean holding a server-side chatId→session map, which is
        // the state this cookie-only design exists to avoid.
        if (previousChatId is null)
            return;

        var workflowId = GiftShopChatWorkflow.WorkflowIdFor($"anon-{previousChatId}");

        try
        {
            using var budget = new CancellationTokenSource(ShutdownBudget);
            await workflowClient.ShutdownAsync(workflowId, budget.Token);
        }
        catch (RpcException exception) when (exception.Code == RpcException.StatusCode.NotFound)
        {
            // Expected and common: the workflow starts lazily on the first message, so any visitor
            // who never chatted has no run to shut down. Not a warning.
            logger.ChatSessionNotStartedAtRotation(workflowId);
        }
        catch (Exception exception)
        {
            // Deliberately broad. Everything reachable here — Temporal unreachable, the shutdown
            // budget expiring, a transport fault — is a reason to log and carry on, never a reason
            // to fail the authentication transition the user actually asked for.
            logger.ChatSessionShutdownFailedAtRotation(exception, workflowId);
        }
    }
}

internal static partial class ChatIdentityRotatorLogging
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The response had already started, so the anonymous chat identity was not "
                  + "rotated. It will survive this authentication boundary.")]
    internal static partial void ChatIdentityRotationSkipped(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No chat session to shut down for {WorkflowId} while rotating the chat identity.")]
    internal static partial void ChatSessionNotStartedAtRotation(
        this ILogger logger,
        string workflowId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not shut down chat session {WorkflowId} while rotating the chat identity. "
                  + "The session is orphaned and will reap itself; continuing.")]
    internal static partial void ChatSessionShutdownFailedAtRotation(
        this ILogger logger,
        Exception exception,
        string workflowId);
}
