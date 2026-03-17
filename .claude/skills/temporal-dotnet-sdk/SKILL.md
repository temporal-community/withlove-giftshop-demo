---
name: dotnet-sdk
description: This skill should be used when the user asks to 'create a Temporal workflow in C#', 'write a .NET activity', 'use Temporalio', 'fix .NET workflow determinism', 'debug workflow replay', '.NET workflow logging', or mentions 'Temporal .NET SDK' or 'Temporal C#'. Provides .NET-specific patterns, CRITICAL Task scheduling guidance, and observability patterns.
version: 0.1.0
---

# Temporal .NET SDK Best Practices

## Overview

The Temporal .NET SDK provides strongly-typed async/await workflows for .NET applications.

**CRITICAL**: Workflows use a custom `TaskScheduler`. Many standard Task patterns break determinism.

## How Temporal Works: History Replay

Understanding how Temporal achieves durable execution is essential for writing correct workflows.

### The Replay Mechanism

When a Worker executes workflow code, it creates **Commands** (requests for operations like starting an Activity or Timer) and sends them to the Temporal Cluster. The Cluster maintains an **Event History** - a durable log of everything that happened during the workflow execution.

**Key insight**: During replay, the Worker re-executes your workflow code but uses the Event History to restore state instead of re-executing Activities. When it encounters an Activity call that has a corresponding `ActivityTaskCompleted` event in history, it returns the stored result instead of scheduling a new execution.

This is why **determinism matters**: The Worker validates that Commands generated during replay match the Events in history. A mismatch causes a non-deterministic error because the Worker cannot reliably restore state.

**.NET SDK has no sandbox** - you must ensure determinism through code review and using the safe alternatives documented below. The custom `TaskScheduler` helps enforce some constraints but cannot catch all non-deterministic code.

## Quick Start

```csharp
// Activity
public class GreetingActivities
{
    [Activity]
    public string Greet(string name) => $"Hello, {name}!";
}

// Workflow
[Workflow]
public class GreetingWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string name)
    {
        return await Workflow.ExecuteActivityAsync(
            (GreetingActivities act) => act.Greet(name),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
    }
}

// Worker
var client = await TemporalClient.ConnectAsync(new("localhost:7233"));
using var worker = new TemporalWorker(
    client,
    new TemporalWorkerOptions("greeting-queue")
        .AddWorkflow<GreetingWorkflow>()
        .AddAllActivities(new GreetingActivities()));
await worker.ExecuteAsync(cancellationToken);
```

## Key Concepts

### Workflow Definition
- Use `[Workflow]` attribute on class
- Use `[WorkflowRun]` on entry method
- Use `[WorkflowSignal]`, `[WorkflowQuery]` for handlers
- Use lambda expressions for type-safe activity calls

### Activity Definition
- Use `[Activity]` attribute on methods
- Can be sync or async
- Use `ActivityExecutionContext.Current` for context

### Worker Setup
- Connect client, create TemporalWorker
- Add workflows and activities to options

## Determinism Rules (CRITICAL)

.NET workflows use a custom `TaskScheduler`. Standard Task patterns break determinism.

**DO NOT USE:**
- `Task.Run()` - uses default scheduler
- `ConfigureAwait(false)` - bypasses context
- `Task.Delay()` - uses system timer
- `Task.WhenAny/WhenAll()` - use Workflow versions

**USE INSTEAD:**
- `Workflow.RunTaskAsync()`
- No ConfigureAwait needed
- `Workflow.DelayAsync()`
- `Workflow.WhenAnyAsync/WhenAllAsync()`

See `references/determinism.md` for detailed rules.

## Replay-Aware Logging

Use `Workflow.Logger` inside Workflows for replay-safe logging that avoids duplicate messages:

```csharp
[Workflow]
public class MyWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string input)
    {
        // These logs are automatically suppressed during replay
        Workflow.Logger.LogInformation("Workflow started with input: {Input}", input);

        var result = await Workflow.ExecuteActivityAsync(
            (MyActivities a) => a.Process(input),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });

        Workflow.Logger.LogInformation("Workflow completed with result: {Result}", result);
        return result;
    }
}
```

For activities, use standard `ILogger` injection or `ActivityExecutionContext.Logger`:

```csharp
public class MyActivities
{
    [Activity]
    public string Process(string input)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        logger.LogInformation("Processing: {Input}", input);
        return $"Processed: {input}";
    }
}
```

## Common Pitfalls

1. **Using `Task.Run()`** - Use `Workflow.RunTaskAsync()` instead
2. **Using `ConfigureAwait(false)`** - Don't use in workflows
3. **Using `Task.Delay()`** - Use `Workflow.DelayAsync()` instead
4. **Using `Task.WhenAny/All`** - Use `Workflow.WhenAny/AllAsync()`
5. **Using system semaphores** - Use `Temporalio.Workflows.Semaphore`
6. **Using `Console.WriteLine()` in workflows** - Use `Workflow.Logger` instead

## Additional Resources

### Reference Files
- **`references/determinism.md`** - CRITICAL Task gotchas, history replay, safe alternatives
- **`references/error-handling.md`** - ApplicationFailureException, retry policies, idempotency patterns
- **`references/testing.md`** - WorkflowEnvironment, time-skipping, replay testing
- **`references/patterns.md`** - Signals, queries, DI integration
- **`references/observability.md`** - OpenTelemetry tracing, replay-aware logging, metrics
- **`references/advanced-features.md`** - Updates, interceptors, search attributes, schedules
- **`references/data-handling.md`** - Data converters, encryption, payload codecs
- **`references/versioning.md`** - Patching API, workflow type versioning, Worker Versioning
