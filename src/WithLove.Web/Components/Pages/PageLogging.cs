namespace WithLove.Web.Components.Pages;

internal static partial class PageLogging
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {Email} logged in.")]
    internal static partial void UserLoggedIn(this ILogger logger, string? email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed login attempt for {Email}.")]
    internal static partial void FailedLoginAttempt(this ILogger logger, string? email);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Email} created a new account.")]
    internal static partial void UserCreatedAccount(this ILogger logger, string? email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed registration attempt for {Email}: {Errors}")]
    internal static partial void FailedRegistrationAttempt(this ILogger logger, string? email, string errors);
}
