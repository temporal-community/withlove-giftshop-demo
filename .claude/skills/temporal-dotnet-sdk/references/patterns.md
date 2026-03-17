# .NET SDK Patterns

## Signals

### WHY: Use signals to send data or commands to a running workflow from external sources
### WHEN:
- **Order approval workflows** - Wait for human approval before proceeding
- **Live configuration updates** - Change workflow behavior without restarting
- **Fire-and-forget communication** - Notify workflow of external events
- **Workflow coordination** - Allow workflows to communicate with each other

**Signals vs Queries vs Updates:**
- Signals: Fire-and-forget, no response, can modify state
- Queries: Read-only, returns data, cannot modify state
- Updates: Synchronous, returns response, can modify state

```csharp
[Workflow]
public class ApprovalWorkflow
{
    private bool _approved;
    private readonly List<string> _items = new();

    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await Workflow.WaitConditionAsync(() => _approved);
        return $"Processed {_items.Count} items";
    }

    [WorkflowSignal]
    public async Task ApproveAsync()
    {
        _approved = true;
    }

    [WorkflowSignal]
    public async Task AddItemAsync(string item)
    {
        _items.Add(item);
    }
}
```

### Signal-with-Start

Atomically start a workflow and send it a signal. Essential for accumulator patterns.

```csharp
var workflowOptions = new WorkflowOptions(
    id: "order-workflow-123",
    taskQueue: "my-queue")
{
    IdReusePolicy = WorkflowIdReusePolicy.TerminateIfRunning,
};

// Configure signal to be sent atomically with start
workflowOptions.SignalWithStart((OrderWorkflow wf) => wf.AddItemAsync("first-item"));

var handle = await client.StartWorkflowAsync(
    (OrderWorkflow wf) => wf.RunAsync(),
    workflowOptions);
```

## Queries

### WHY: Read workflow state without affecting execution - queries are read-only
### WHEN:
- **Progress tracking dashboards** - Display workflow progress to users
- **Status endpoints** - Check workflow state for API responses
- **Debugging** - Inspect internal workflow state
- **Health checks** - Verify workflow is functioning correctly

**Important:** Queries must NOT modify workflow state or have side effects.

```csharp
[Workflow]
public class StatusWorkflow
{
    private string _status = "pending";
    private int _progress;

    [WorkflowRun]
    public async Task RunAsync()
    {
        _status = "running";
        for (int i = 0; i < 100; i++)
        {
            _progress = i;
            await Workflow.ExecuteActivityAsync(
                (MyActivities act) => act.ProcessItem(i),
                new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
        }
        _status = "completed";
    }

    [WorkflowQuery]
    public string GetStatus() => _status;

    [WorkflowQuery]
    public int GetProgress() => _progress;
}
```

## Child Workflows

### WHY: Break complex workflows into smaller, manageable units with independent failure domains
### WHEN:
- **Failure domain isolation** - Child failures don't automatically fail parent
- **Different retry policies** - Each child can have its own retry configuration
- **Reusability** - Share workflow logic across multiple parent workflows
- **Independent scaling** - Child workflows can run on different task queues
- **History size management** - Each child has its own event history

**Use activities instead when:** Operation is short-lived, doesn't need its own failure domain, or doesn't need independent retry policies.

```csharp
[WorkflowRun]
public async Task<List<string>> RunAsync(List<Order> orders)
{
    var results = new List<string>();

    foreach (var order in orders)
    {
        var result = await Workflow.ExecuteChildWorkflowAsync(
            (ProcessOrderWorkflow wf) => wf.RunAsync(order),
            new()
            {
                Id = $"order-{order.Id}",
                // Control what happens to child when parent completes
                ParentClosePolicy = ParentClosePolicy.Abandon
            });
        results.Add(result);
    }

    return results;
}
```

## Parallel Execution

### WHY: Execute multiple independent operations concurrently for better throughput
### WHEN:
- **Batch processing** - Process multiple items simultaneously
- **Fan-out patterns** - Distribute work across multiple activities
- **Independent operations** - Operations that don't depend on each other's results

```csharp
[WorkflowRun]
public async Task<string[]> RunAsync(string[] items)
{
    var tasks = items.Select(item =>
        Workflow.ExecuteActivityAsync(
            (MyActivities act) => act.ProcessItem(item),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) }));

    return await Workflow.WhenAllAsync(tasks);
}
```

## Continue-as-New

### WHY: Prevent unbounded event history growth in long-running or infinite workflows
### WHEN:
- **Event history approaching 10,000+ events** - Temporal recommends continue-as-new before hitting limits
- **Infinite/long-running workflows** - Polling, subscription, or daemon-style workflows
- **Memory optimization** - Reset workflow state to reduce memory footprint

**Recommendation:** Check history length periodically and continue-as-new around 10,000 events.

```csharp
[WorkflowRun]
public async Task RunAsync(State state)
{
    while (true)
    {
        state = await ProcessNextBatchAsync(state);

        if (state.IsComplete)
        {
            return;
        }

        if (Workflow.Info.HistoryLength > 10000)
        {
            throw Workflow.CreateContinueAsNewException(
                (MyWorkflow wf) => wf.RunAsync(state));
        }
    }
}
```

## Saga Pattern

### WHY: Implement distributed transactions with compensating actions for rollback
### WHEN:
- **Multi-step transactions** - Operations that span multiple services
- **Eventual consistency** - When you can't use traditional ACID transactions
- **Rollback requirements** - When partial failures require undoing previous steps

**Important:** Compensation activities should be idempotent - they may be retried.

```csharp
[WorkflowRun]
public async Task<string> RunAsync(Order order)
{
    var compensations = new List<Func<Task>>();
    var options = new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) };

    try
    {
        await Workflow.ExecuteActivityAsync(
            (OrderActivities act) => act.ReserveInventory(order), options);
        compensations.Add(() => Workflow.ExecuteActivityAsync(
            (OrderActivities act) => act.ReleaseInventory(order), options));

        await Workflow.ExecuteActivityAsync(
            (OrderActivities act) => act.ChargePayment(order), options);
        compensations.Add(() => Workflow.ExecuteActivityAsync(
            (OrderActivities act) => act.RefundPayment(order), options));

        await Workflow.ExecuteActivityAsync(
            (OrderActivities act) => act.ShipOrder(order), options);

        return "Order completed";
    }
    catch (Exception)
    {
        compensations.Reverse();
        foreach (var compensate in compensations)
        {
            try { await compensate(); }
            catch { /* Log and continue */ }
        }
        throw;
    }
}
```

## Local Activities

### WHY: Reduce latency for short, lightweight operations by skipping the task queue
### WHEN:
- **Short operations** - Activities completing in milliseconds/seconds
- **High-frequency calls** - When task queue overhead is significant
- **Low-latency requirements** - When you can't afford task queue round-trip
- **Same-worker execution** - Operations that must run on the same worker

**Tradeoffs:** Local activities don't appear in history until the workflow task completes, and don't benefit from task queue load balancing.

```csharp
[WorkflowRun]
public async Task<string> RunAsync(string key)
{
    // Use local activity for short, local operations
    return await Workflow.ExecuteLocalActivityAsync(
        (MyActivities act) => act.QuickLookup(key),
        new LocalActivityOptions { ScheduleToCloseTimeout = TimeSpan.FromSeconds(5) });
}
```

## Workflow Cancellation Handling

### WHY: Gracefully handle workflow cancellation requests and perform cleanup
### WHEN:
- **Graceful shutdown** - Clean up resources when workflow is cancelled
- **External cancellation** - Respond to cancellation requests from clients
- **Cleanup activities** - Run cleanup logic even after cancellation

```csharp
[WorkflowRun]
public async Task<string> RunAsync()
{
    try
    {
        await Workflow.ExecuteActivityAsync(
            (MyActivities act) => act.LongRunningActivity(),
            new() { StartToCloseTimeout = TimeSpan.FromHours(1) });
        return "completed";
    }
    catch (CancelledException)
    {
        // Workflow was cancelled - run cleanup in non-cancellable scope
        await Workflow.ExecuteActivityAsync(
            (MyActivities act) => act.CleanupActivity(),
            new()
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                // Activity runs even if workflow is cancelled
                CancellationToken = CancellationToken.None
            });
        throw;
    }
}
```

## Dependency Injection (Generic Host)

### WHY: Integrate Temporal workers with ASP.NET Core and .NET dependency injection
### WHEN:
- **ASP.NET Core applications** - Run workers alongside web APIs
- **Shared services** - Inject database contexts, HTTP clients, etc. into activities
- **Lifecycle management** - Use standard .NET hosting patterns

```csharp
// In Program.cs
services.AddTemporalClient(options =>
{
    options.Address = "localhost:7233";
});

services.AddHostedTemporalWorker("my-task-queue")
    .AddScopedActivities<MyActivities>()
    .AddWorkflow<MyWorkflow>();
```

## Timers

### WHY: Schedule delays or deadlines within workflows in a durable way
### WHEN:
- **Scheduled delays** - Wait for a specific duration before continuing
- **Deadlines** - Set timeouts for operations
- **Reminder patterns** - Schedule future notifications

```csharp
[WorkflowRun]
public async Task<string> RunAsync()
{
    // Simple delay
    await Workflow.DelayAsync(TimeSpan.FromHours(1));

    // Wait with timeout
    var completed = await Workflow.WaitConditionAsync(
        () => _someCondition,
        TimeSpan.FromMinutes(30));

    if (!completed)
    {
        return "timed out";
    }
    return "completed";
}
```

## Lambda Expressions for Type Safety

### WHY: The .NET SDK uses lambda expressions for compile-time type safety
### WHEN:
- **All workflow/activity invocations** - This is the primary invocation pattern

```csharp
// Activity invocation - type-safe with parameter verification
var result = await Workflow.ExecuteActivityAsync(
    (MyActivities act) => act.MyActivity(arg1, arg2),
    new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });

// Workflow start - type-safe with parameter verification
var handle = await client.StartWorkflowAsync(
    (MyWorkflow wf) => wf.RunAsync(input),
    new() { Id = "my-workflow", TaskQueue = "my-queue" });

// Child workflow - type-safe with parameter verification
var childResult = await Workflow.ExecuteChildWorkflowAsync(
    (ChildWorkflow wf) => wf.RunAsync(data),
    new() { Id = "child-id" });

// Signal
await handle.SignalAsync((MyWorkflow wf) => wf.ApproveAsync());

// Query
var status = await handle.QueryAsync((MyWorkflow wf) => wf.GetStatus());
```
