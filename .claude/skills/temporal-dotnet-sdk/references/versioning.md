# .NET SDK Versioning

## Overview

Workflow versioning allows you to safely deploy changes to Workflow code without causing non-deterministic errors in running Workflow Executions. The .NET SDK provides multiple approaches: the Patching API for code-level version management, Workflow Type versioning for incompatible changes, and Worker Versioning for deployment-level control.

## Why Versioning is Needed

When Workers restart after a deployment, they resume open Workflow Executions through History Replay. If the updated Workflow Definition produces a different sequence of Commands than the original code, it causes a non-deterministic error. Versioning ensures backward compatibility by preserving the original execution path for existing workflows while allowing new workflows to use updated code.

## Workflow Versioning with Patching API

### The Patched() Method

The `Workflow.Patched()` method checks whether a Workflow should run new or old code:

```csharp
[Workflow]
public class ShippingWorkflow
{
    [WorkflowRun]
    public async Task RunAsync()
    {
        if (Workflow.Patched("send-email-instead-of-fax"))
        {
            // New code path
            await Workflow.ExecuteActivityAsync(
                (MyActivities a) => a.SendEmail(),
                new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
        }
        else
        {
            // Old code path (for replay of existing workflows)
            await Workflow.ExecuteActivityAsync(
                (MyActivities a) => a.SendFax(),
                new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
        }
    }
}
```

**How it works:**
- For new executions: `Patched()` returns `true` and records a marker in the Workflow history
- For replay with the marker: `Patched()` returns `true` (history includes this patch)
- For replay without the marker: `Patched()` returns `false` (history predates this patch)

### Three-Step Patching Process

**Step 1: Patch in New Code**

Add the patch with both old and new code paths:

```csharp
[Workflow]
public class OrderWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(Order order)
    {
        if (Workflow.Patched("add-fraud-check"))
        {
            // New: Run fraud check before payment
            await Workflow.ExecuteActivityAsync(
                (MyActivities a) => a.CheckFraud(order),
                new() { StartToCloseTimeout = TimeSpan.FromMinutes(2) });
        }

        // Original payment logic runs for both paths
        return await Workflow.ExecuteActivityAsync(
            (MyActivities a) => a.ProcessPayment(order),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
    }
}
```

**Step 2: Deprecate the Patch**

Once all pre-patch Workflow Executions have completed, remove the old code and use `DeprecatePatch()`:

```csharp
[Workflow]
public class OrderWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(Order order)
    {
        Workflow.DeprecatePatch("add-fraud-check");

        // Only new code remains
        await Workflow.ExecuteActivityAsync(
            (MyActivities a) => a.CheckFraud(order),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(2) });

        return await Workflow.ExecuteActivityAsync(
            (MyActivities a) => a.ProcessPayment(order),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
    }
}
```

**Step 3: Remove the Patch**

After all workflows with the deprecated patch marker have completed, remove the `DeprecatePatch()` call entirely:

```csharp
[Workflow]
public class OrderWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(Order order)
    {
        await Workflow.ExecuteActivityAsync(
            (MyActivities a) => a.CheckFraud(order),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(2) });

        return await Workflow.ExecuteActivityAsync(
            (MyActivities a) => a.ProcessPayment(order),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
    }
}
```

### Query Filters for Finding Workflows by Version

Use List Filters to find workflows with specific patch versions:

```bash
# Find running workflows with a specific patch
temporal workflow list --query \
  'WorkflowType = "OrderWorkflow" AND ExecutionStatus = "Running" AND TemporalChangeVersion = "add-fraud-check"'

# Find running workflows without any patch (pre-patch versions)
temporal workflow list --query \
  'WorkflowType = "OrderWorkflow" AND ExecutionStatus = "Running" AND TemporalChangeVersion IS NULL'
```

## Workflow Type Versioning

For incompatible changes, create a new Workflow Type instead of using patches:

```csharp
[Workflow]
public class PizzaWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(PizzaOrder order)
    {
        // Original implementation
        return await ProcessOrderV1(order);
    }
}

[Workflow]
public class PizzaWorkflowV2
{
    [WorkflowRun]
    public async Task<string> RunAsync(PizzaOrder order)
    {
        // New implementation with incompatible changes
        return await ProcessOrderV2(order);
    }
}
```

Register both with the Worker:

```csharp
using var worker = new TemporalWorker(
    client,
    new TemporalWorkerOptions("pizza-task-queue")
        .AddWorkflow<PizzaWorkflow>()
        .AddWorkflow<PizzaWorkflowV2>()
        .AddAllActivities(new PizzaActivities()));
```

Check for open executions before removing the old type:

```bash
temporal workflow list --query 'WorkflowType = "PizzaWorkflow" AND ExecutionStatus = "Running"'
```

## Worker Versioning

Worker Versioning manages versions at the deployment level, allowing multiple Worker versions to run simultaneously.

### Key Concepts

**Worker Deployment**: A logical service grouping similar Workers together (e.g., "order-processor"). All versions of your code live under this umbrella.

**Worker Deployment Version**: A specific snapshot of your code identified by a deployment name and Build ID (e.g., "order-processor:v1.0" or "order-processor:abc123").

### Configuring Workers for Versioning

```csharp
var worker = new TemporalWorker(
    client,
    new TemporalWorkerOptions("my-task-queue")
    {
        DeploymentOptions = new WorkerDeploymentOptions(
            new WorkerDeploymentVersion("my-service", "v1.0.0"),
            useVersioning: true)
        {
            DefaultVersioningBehavior = VersioningBehavior.Unspecified
        }
    }
    .AddWorkflow<MyWorkflow>()
    .AddAllActivities(new MyActivities()));
```

**Configuration parameters:**
- `useVersioning`: Enables Worker Versioning
- `WorkerDeploymentVersion`: Identifies the Worker Deployment Version (deployment name + build ID)
- Build ID: Typically a git commit hash, version number, or timestamp

### PINNED vs AUTO_UPGRADE Behaviors

**PINNED Behavior**

Workflows stay locked to their original Worker version. Mark a workflow as pinned:

```csharp
[Workflow(VersioningBehavior = VersioningBehavior.Pinned)]
public class StableWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        // This workflow will always run on its assigned version
        return await Workflow.ExecuteActivityAsync(
            (MyActivities a) => a.ProcessOrder(),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
    }
}
```

**When to use PINNED:**
- Short-running workflows (minutes to hours)
- Consistency is critical (e.g., financial transactions)
- Building new applications and want simplest development experience

**AUTO_UPGRADE Behavior**

Workflows can move to newer versions:

```csharp
[Workflow(VersioningBehavior = VersioningBehavior.AutoUpgrade)]
public class LongRunningWorkflow
{
    // ...
}
```

**When to use AUTO_UPGRADE:**
- Long-running workflows (weeks or months)
- Workflows need to benefit from bug fixes during execution
- Migrating from traditional rolling deployments

**Important:** AUTO_UPGRADE workflows still need patching to handle version transitions safely.

### Deployment Strategies

**Blue-Green Deployments**

Maintain two environments and switch traffic between them:
1. Deploy new code to idle environment
2. Run tests and validation
3. Switch traffic to new environment
4. Keep old environment for instant rollback

**Rainbow Deployments**

Multiple versions run simultaneously:
- New workflows use latest version
- Existing workflows complete on their original version
- Add new versions alongside existing ones
- Gradually sunset old versions as workflows complete

### Querying Workflows by Worker Version

```bash
# Find workflows on a specific Worker version
temporal workflow list --query \
  'TemporalWorkerDeploymentVersion = "my-service:v1.0.0" AND ExecutionStatus = "Running"'
```

## Testing Replay Compatibility

Use the Replayer to verify code changes are compatible with existing histories:

```csharp
var replayer = new WorkflowReplayer(
    new WorkflowReplayerOptions()
        .AddWorkflow<MyWorkflow>());

// Load history from file or fetch from server
var history = await client.GetWorkflowHistoryAsync("my-workflow-id");

// This will throw if replay detects non-determinism
await replayer.ReplayWorkflowAsync(history);
```

## Best Practices

1. **Check for open executions** before removing old code paths
2. **Use descriptive patch IDs** that explain the change (e.g., "add-fraud-check" not "patch-1")
3. **Deploy patches incrementally**: patch, deprecate, remove
4. **Use PINNED for short workflows** to simplify version management
5. **Use AUTO_UPGRADE with patching** for long-running workflows that need updates
6. **Generate Build IDs from code** (git hash) to ensure changes produce new versions
7. **Avoid rolling deployments** for high-availability services with long-running workflows
8. **Test with replay** before deploying changes to catch non-determinism early
