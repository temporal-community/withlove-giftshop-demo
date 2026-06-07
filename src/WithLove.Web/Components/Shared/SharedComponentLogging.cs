using Microsoft.Extensions.Logging;

namespace WithLove.Web.Components.Shared;

internal static partial class SharedComponentLogging
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to load account dropdown Love Tokens balance for user {UserId}")]
    internal static partial void UnableToLoadAccountDropdownBalance(this ILogger logger, Exception exception, string userId);
}
