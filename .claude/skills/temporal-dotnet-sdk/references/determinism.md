# .NET SDK Determinism (CRITICAL)

## Overview

.NET workflows use a custom `TaskScheduler`. Standard Task patterns break determinism.

## Why Determinism Matters: History Replay

When a workflow resumes after being suspended (worker restart, crash, continue-as-new), Temporal **replays** the workflow's event history to rebuild the workflow's state. During replay:

1. Temporal loads the workflow's complete event history
2. The workflow code re-executes from the beginning
3. Instead of actually performing operations (activities, timers), Temporal matches them against history
4. The workflow state is reconstructed to where it left off

**If workflow code produces different commands during replay than it did originally, the workflow fails with a non-determinism error.**

Example of what happens during replay:
```
Original execution:       Replay:
1. Start workflow         1. Start workflow (match history)
2. Execute Activity A     2. Execute Activity A (match - return cached result)
3. Execute Activity B     3. Execute Activity B (match - return cached result)
4. Timer 5 min           4. Timer 5 min (match - skip)
5. (worker crashes)      5. (state rebuilt, continue from here)
```

## DO NOT USE in Workflows

```csharp
// WRONG - uses default scheduler/thread pool
await Task.Run(() => DoWork());

// WRONG - bypasses workflow context
await SomeAsync().ConfigureAwait(false);

// WRONG - uses system timer, not workflow timer
await Task.Delay(TimeSpan.FromMinutes(5));

// WRONG - uses system Wait
Task.Wait();
task.Result;  // Blocks

// WRONG - uses default scheduler
await Task.WhenAny(task1, task2);
await Task.WhenAll(task1, task2);

// WRONG - system threading primitives
var semaphore = new SemaphoreSlim(1);
var mutex = new Mutex();

// WRONG - uses CancelAsync
cts.CancelAsync();
```

## USE INSTEAD

```csharp
// CORRECT - uses workflow scheduler
await Workflow.RunTaskAsync(() => DoWork());

// CORRECT - no ConfigureAwait needed in workflows
await SomeAsync();

// CORRECT - uses workflow timer
await Workflow.DelayAsync(TimeSpan.FromMinutes(5));

// CORRECT - workflow-aware
await Workflow.WhenAnyAsync(task1, task2);
await Workflow.WhenAllAsync(task1, task2);

// CORRECT - workflow primitives
var semaphore = new Temporalio.Workflows.Semaphore(1);
var mutex = new Temporalio.Workflows.Mutex();

// CORRECT - synchronous cancel
cts.Cancel();
```

## Safe Workflow Utilities

```csharp
// Deterministic time
var now = Workflow.UtcNow;

// Deterministic random
var random = Workflow.Random;
var value = random.Next(100);

// Deterministic UUID
var guid = Workflow.NewGuid();

// Check if replaying
if (!Workflow.Unsafe.IsReplaying)
{
    // Only during live execution
}
```

## Versioning with Patching

```csharp
[WorkflowRun]
public async Task<string> RunAsync()
{
    if (Workflow.Patched("my-change"))
    {
        return await NewImplementationAsync();
    }
    else
    {
        return await OldImplementationAsync();
    }
}

// After old workflows complete:
[WorkflowRun]
public async Task<string> RunAsync()
{
    Workflow.DeprecatePatch("my-change");
    return await NewImplementationAsync();
}
```

## Recommended .editorconfig

Use `.workflow.cs` extension for workflow files:

```ini
[*.workflow.cs]
dotnet_diagnostic.CA1024.severity = none  # Properties vs getters
dotnet_diagnostic.CA1822.severity = none  # Static methods
dotnet_diagnostic.CA2007.severity = none  # ConfigureAwait
dotnet_diagnostic.CA2008.severity = none  # TaskScheduler
dotnet_diagnostic.CA5394.severity = none  # Non-crypto random
dotnet_diagnostic.CS1998.severity = none  # Async without await
dotnet_diagnostic.VSTHRD105.severity = none  # TaskScheduler.Current
```

## Best Practices

1. NEVER use `Task.Run()` in workflows
2. NEVER use `ConfigureAwait(false)` in workflows
3. Use `Workflow.DelayAsync()` instead of `Task.Delay()`
4. Use `Workflow.WhenAnyAsync/WhenAllAsync()` instead of Task versions
5. Use `.workflow.cs` extension with custom .editorconfig
6. Test with replay to catch determinism issues
