# .NET SDK Testing

## Overview

The .NET SDK provides `WorkflowEnvironment` for testing with time-skipping support.

## Time-Skipping Test Environment

```csharp
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

public class WorkflowTests
{
    [Fact]
    public async Task TestWorkflow()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions($"task-queue-{Guid.NewGuid()}")
                .AddWorkflow<MyWorkflow>()
                .AddAllActivities(new MyActivities()));

        await worker.ExecuteAsync(async () =>
        {
            var result = await env.Client.ExecuteWorkflowAsync(
                (MyWorkflow wf) => wf.RunAsync(),
                new(id: $"wf-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            Assert.Equal("expected", result);
        });
    }
}
```

## Local Test Environment

```csharp
await using var env = await WorkflowEnvironment.StartLocalAsync();
// Real-time execution (no time skipping)
```

## Activity Testing

```csharp
[Fact]
public async Task TestActivity()
{
    var env = new ActivityEnvironment();
    var activities = new MyActivities();

    var result = await env.RunAsync(() => activities.MyActivity("arg"));

    Assert.Equal("expected", result);
}
```

## Testing Signals and Queries

```csharp
[Fact]
public async Task TestSignalsAndQueries()
{
    await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

    using var worker = new TemporalWorker(...);

    await worker.ExecuteAsync(async () =>
    {
        var handle = await env.Client.StartWorkflowAsync(
            (ApprovalWorkflow wf) => wf.RunAsync(),
            new(id: "approval-test", taskQueue: worker.Options.TaskQueue!));

        // Query state
        var status = await handle.QueryAsync(wf => wf.GetStatus());
        Assert.Equal("pending", status);

        // Send signal
        await handle.SignalAsync(wf => wf.ApproveAsync());

        // Wait for result
        var result = await handle.GetResultAsync();
        Assert.Equal("Approved!", result);
    });
}
```

## Workflow Replay Testing

```csharp
[Fact]
public async Task TestReplay()
{
    var replayer = new WorkflowReplayer(
        new WorkflowReplayerOptions().AddWorkflow<MyWorkflow>());

    var history = await FetchWorkflowHistoryAsync("workflow-id");

    await replayer.ReplayWorkflowAsync(
        WorkflowHistory.FromJson("my-workflow-id", historyJson));
}
```

## Mocking Activities

```csharp
public class MockActivities
{
    [Activity]
    public string MyActivity(string input) => "mocked result";
}

[Fact]
public async Task TestWithMockedActivity()
{
    await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

    using var worker = new TemporalWorker(
        env.Client,
        new TemporalWorkerOptions(taskQueue)
            .AddWorkflow<MyWorkflow>()
            .AddAllActivities(new MockActivities()));  // Use mock

    // Run test
}
```

## Best Practices

1. Use time-skipping for workflows with timers
2. Use unique task queue per test
3. Mock activities for isolated testing
4. Test signal/query handlers explicitly
5. Test replay compatibility when changing workflow code
