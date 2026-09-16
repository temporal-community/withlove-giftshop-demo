using System.Text.Json;
using TemporalCommunity.Extensions.AI;
using Temporalio.Common;

namespace WithLove.Workflows.Tests.Replay;

public class GiftShopChatWorkflowReplayTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task CheckedInHistory_ReplaysWithoutNondeterminism()
    {
        var historyPath = Path.Combine(
            AppContext.BaseDirectory,
            "Replay",
            "Histories",
            "giftshop-chat-v1.json");
        var historyJson = await File.ReadAllTextAsync(historyPath);
        AssertRegressionFixture(historyJson);
        var history = WorkflowHistory.FromJson("giftshop-chat-replay-v1", historyJson);
        var options = new WorkflowReplayerOptions
        {
            DataConverter = DurableAIDataConverter.Instance,
        };
        options.AddWorkflow<GiftShopChatWorkflow>();
        var replayer = new WorkflowReplayer(options);

        var result = await replayer.ReplayWorkflowAsync(
            history,
            throwOnReplayFailure: false);

        result.ReplayFailure.Should().BeNull();
    }

    private static void AssertRegressionFixture(string historyJson)
    {
        using var document = JsonDocument.Parse(historyJson);
        var events = document.RootElement.GetProperty("events").EnumerateArray().ToList();

        events[0]
            .GetProperty("workflowExecutionStartedEventAttributes")
            .GetProperty("workflowType")
            .GetProperty("name")
            .GetString()
            .Should().Be("WithLove.GiftShopChatWorkflow");

        var activityTypes = events
            .Where(historyEvent => historyEvent.TryGetProperty(
                "activityTaskScheduledEventAttributes",
                out _))
            .Select(historyEvent => historyEvent
                .GetProperty("activityTaskScheduledEventAttributes")
                .GetProperty("activityType")
                .GetProperty("name")
                .GetString())
            .ToList();
        activityTypes.Should().ContainInOrder(
            "TemporalCommunity.Extensions.AI.GetChatStep",
            "TemporalCommunity.Extensions.AI.InvokeFunction",
            "TemporalCommunity.Extensions.AI.GetChatStep");

        var accepted = events.Single(historyEvent => historyEvent.TryGetProperty(
            "workflowExecutionUpdateAcceptedEventAttributes",
            out _));
        var acceptedAttributes = accepted.GetProperty(
            "workflowExecutionUpdateAcceptedEventAttributes");
        acceptedAttributes
            .GetProperty("acceptedRequest")
            .GetProperty("input")
            .GetProperty("name")
            .GetString()
            .Should().Be("SendMessage");
        var updateId = acceptedAttributes
            .GetProperty("acceptedRequest")
            .GetProperty("meta")
            .GetProperty("updateId")
            .GetString();

        var acceptedEventId = long.Parse(accepted.GetProperty("eventId").GetString()!);
        var hasCompletedSendMessage = events.Any(historyEvent =>
        {
            if (!historyEvent.TryGetProperty(
                    "workflowExecutionUpdateCompletedEventAttributes",
                    out var completed))
            {
                return false;
            }

            return completed.GetProperty("meta").GetProperty("updateId").GetString() == updateId
                   && long.Parse(historyEvent.GetProperty("eventId").GetString()!)
                   > acceptedEventId;
        });
        hasCompletedSendMessage.Should().BeTrue();
    }
}
