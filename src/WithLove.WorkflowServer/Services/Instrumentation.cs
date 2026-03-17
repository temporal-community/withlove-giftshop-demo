using System.Diagnostics;

namespace WithLove.WorkflowServer.Services;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "productsApi";
    internal const string ActivitySourceVersion = "1.0.0";

    public ActivitySource ActivitySource { get; } = new(ActivitySourceName, ActivitySourceVersion);

    public void Dispose()
    {
        ActivitySource.Dispose();
    }
}