# .NET SDK Error Handling

## Overview

The .NET SDK uses `ApplicationFailureException` for application errors.

## Application Failures

```csharp
using Temporalio.Exceptions;

[Activity]
public void ValidateActivity(string input)
{
    if (!IsValid(input))
    {
        throw new ApplicationFailureException(
            "Invalid input",
            "ValidationError",
            nonRetryable: true
        );
    }
}
```

## Handling Errors in Workflows

```csharp
[WorkflowRun]
public async Task<string> RunAsync()
{
    try
    {
        return await Workflow.ExecuteActivityAsync(
            (MyActivities act) => act.RiskyActivity(),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
    }
    catch (ActivityFailureException e)
    {
        Workflow.Logger.LogError(e, "Activity failed");

        if (e.InnerException is ApplicationFailureException appEx)
        {
            if (appEx.ErrorType == "ValidationError")
            {
                return await HandleValidationError();
            }
        }

        throw new ApplicationFailureException(
            "Workflow failed",
            "WorkflowError"
        );
    }
}
```

## Retry Policy Configuration

```csharp
var result = await Workflow.ExecuteActivityAsync(
    (MyActivities act) => act.MyActivity(),
    new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(10),
        RetryPolicy = new()
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2.0f,
            MaximumInterval = TimeSpan.FromMinutes(1),
            MaximumAttempts = 5,
            NonRetryableErrorTypes = new[] { "ValidationError", "PaymentError" },
        },
    });
```

## Timeout Configuration

```csharp
var result = await Workflow.ExecuteActivityAsync(
    (MyActivities act) => act.MyActivity(),
    new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),      // Single attempt
        ScheduleToCloseTimeout = TimeSpan.FromMinutes(30),  // Including retries
        HeartbeatTimeout = TimeSpan.FromSeconds(30),        // Between heartbeats
    });
```

## Workflow Failure

```csharp
[WorkflowRun]
public async Task<string> RunAsync()
{
    if (someCondition)
    {
        throw new ApplicationFailureException(
            "Cannot process",
            "BusinessError",
            nonRetryable: true
        );
    }
    return "success";
}
```

## Idempotency Patterns

Activities may be retried due to failures or timeouts. Design activities to be idempotent - safe to execute multiple times with the same result.

### Use Idempotency Keys

```csharp
[Activity]
public async Task<string> CreateOrderAsync(string orderId, OrderData data)
{
    // Use orderId as idempotency key - if order exists, return existing
    var existing = await _db.FindOrderAsync(orderId);
    if (existing != null)
    {
        return existing.Id;  // Already created, return same result
    }

    // Create new order
    return await _db.CreateOrderAsync(orderId, data);
}
```

### Workflow-Level Idempotency

Workflow IDs are natural idempotency keys:

```csharp
// Use deterministic workflow ID based on business entity
var handle = await client.StartWorkflowAsync(
    (OrderWorkflow wf) => wf.RunAsync(order),
    new()
    {
        Id = $"order-{order.CustomerId}-{order.OrderNumber}",  // Deterministic
        TaskQueue = "orders",
        IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate
    });
```

### Heartbeat for Progress Tracking

Use heartbeat details to resume from last successful point:

```csharp
[Activity]
public async Task ProcessItemsAsync(List<Item> items)
{
    // Get last processed index from heartbeat
    var startIndex = ActivityExecutionContext.Current.Info.HeartbeatDetails.Count > 0
        ? await ActivityExecutionContext.Current.Info.HeartbeatDetailAtAsync<int>(0)
        : 0;

    for (int i = startIndex; i < items.Count; i++)
    {
        await ProcessItemAsync(items[i]);
        ActivityExecutionContext.Current.Heartbeat(i + 1);  // Record progress
    }
}
```

## Best Practices

1. Use specific error types for different failure modes
2. Set `nonRetryable: true` for permanent failures
3. Configure `NonRetryableErrorTypes` in retry policy
4. Log errors before re-throwing
5. Check `InnerException` for wrapped errors
6. **Design activities to be idempotent** - safe to retry
7. **Use workflow IDs as idempotency keys** for deduplication
8. **Use heartbeat details** to track progress and resume from failures
