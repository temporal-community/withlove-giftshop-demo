# .NET SDK Observability

## Overview

The .NET SDK provides comprehensive observability features including OpenTelemetry tracing, metrics, and replay-aware logging.

## Replay-Aware Logging

Workflow logging must use `Workflow.Logger` to avoid duplicate logs during replay.

```csharp
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;

[Workflow]
public class MyWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        // CORRECT - replay-aware, won't duplicate on replay
        Workflow.Logger.LogInformation("Processing workflow {WorkflowId}", Workflow.Info.WorkflowId);

        // WRONG - will log on every replay
        // Console.WriteLine("Processing...");
        // logger.LogInformation("Processing...");  // injected logger

        return "done";
    }
}
```

### Why Replay-Aware Logging Matters

When a workflow resumes (due to worker restart, crash recovery, or continue-as-new), Temporal replays the workflow history to rebuild state. During replay:
- All workflow code executes again
- Normal logging would produce duplicate log entries
- `Workflow.Logger` automatically detects replay and suppresses logs

## OpenTelemetry Tracing

Install the OpenTelemetry extension:

```
dotnet add package Temporalio.Extensions.OpenTelemetry
```

### Basic Setup

```csharp
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Temporalio.Client;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Worker;

// Setup the tracer provider
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(
        TracingInterceptor.ClientSource.Name,
        TracingInterceptor.WorkflowsSource.Name,
        TracingInterceptor.ActivitiesSource.Name)
    .AddConsoleExporter()
    .Build();

// Create client with tracing interceptor
var client = await TemporalClient.ConnectAsync(new("localhost:7233")
{
    Interceptors = new[] { new TracingInterceptor() },
});
```

### With Dependency Injection

```csharp
// In Program.cs
builder.Services
    .AddTemporalClient(opts =>
    {
        opts.TargetHost = "localhost:7233";
        opts.Namespace = "default";
        opts.Interceptors = new[] { new TracingInterceptor() };
    })
    .AddHostedTemporalWorker("my-task-queue");
```

### Workflow Tracing Considerations

**IMPORTANT**: OpenTelemetry spans cannot be resumed across process boundaries. Workflow spans are immediately started and stopped because workflows may resume on different machines.

```csharp
using Temporalio.Extensions.OpenTelemetry;

[Workflow]
public class MyWorkflow
{
    public static readonly ActivitySource CustomSource = new("MyCustomSource");

    [WorkflowRun]
    public async Task RunAsync()
    {
        // Create custom span - completes immediately but provides proper parenting
        using (CustomSource.TrackWorkflowDiagnosticActivity("MyCustomActivity"))
        {
            await Workflow.ExecuteActivityAsync(
                (MyActivities act) => act.DoWork(),
                new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
        }
    }
}
```

**WARNING**: Do not use standard .NET `System.Diagnostics.Activity` API inside workflows - they are non-deterministic and cause unpredictable traces during replay.

## Custom Metrics

The SDK provides replay-aware metrics via `Workflow.MetricMeter`:

```csharp
[WorkflowRun]
public async Task<string> RunAsync()
{
    // Create replay-safe counter
    Workflow.MetricMeter
        .CreateCounter<int>("my-workflow-counter",
            description: "Replay-safe counter for workflow instrumentation")
        .Add(1);

    return "done";
}
```

## Metrics Configuration

### Prometheus

```csharp
using Temporalio.Runtime;

var runtime = new TemporalRuntime(new()
{
    Telemetry = new()
    {
        Metrics = new()
        {
            Prometheus = new() { BindAddress = "0.0.0.0:9090" }
        }
    }
});
```

### OpenTelemetry Metrics

Use `Temporalio.Extensions.DiagnosticSource` for `System.Diagnostics.Metrics` support.

## Best Practices

1. **Always use `Workflow.Logger`** for workflow logging to prevent duplicates
2. **Use `TrackWorkflowDiagnosticActivity`** for custom spans in workflows
3. **Use `Workflow.MetricMeter`** for replay-safe metrics in workflows
4. **Check `Workflow.Unsafe.IsReplaying`** only when absolutely necessary for side effects
5. **Configure tracing early** - set up interceptors before creating workers
