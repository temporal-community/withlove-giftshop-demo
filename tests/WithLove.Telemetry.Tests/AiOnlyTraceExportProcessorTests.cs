using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using WithLove.OpenInference;

namespace WithLove.Telemetry.Tests;

public class AiOnlyTraceExportProcessorTests
{
    [Fact]
    public void ExportPipeline_KeepsTheConnectedAiSpineAndDropsUnrelatedActivities()
    {
        const string sourceName = "WithLove.AiOnlyTraceExportProcessorTests";
        using var source = new ActivitySource(sourceName);
        var exporter = new CapturingActivityExporter();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new AiOnlyTraceExportProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();

        using (var chain = source.StartActivity("chat.turn", ActivityKind.Internal))
        {
            chain!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "CHAIN");
            chain.SetTag("chat.operation_id", "operation-123");
            using (source.StartActivity("durable.turn"))
            {
                using (var model = source.StartActivity("openai.chat"))
                    model!.SetTag("gen_ai.operation.name", "chat");
                using (var tool = source.StartActivity("execute_tool search_products"))
                {
                    tool!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "TOOL");
                    using (var retriever = source.StartActivity("product.search"))
                        retriever!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "RETRIEVER");
                }
            }
        }

        using (source.StartActivity("HTTP GET /collections")) { }
        using (source.StartActivity("SELECT products")) { }
        using (var embeddings = source.StartActivity("openai.embeddings"))
            embeddings!.SetTag("gen_ai.operation.name", "embeddings");

        exporter.ExportedOperationNames.Should().BeEquivalentTo(
        [
            "chat.turn",
            "durable.turn",
            "openai.chat",
            "execute_tool search_products",
            "product.search",
        ]);
    }

    private sealed class CapturingActivityExporter : BaseExporter<Activity>
    {
        internal List<string> ExportedOperationNames { get; } = [];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                ExportedOperationNames.Add(activity.OperationName);
            return ExportResult.Success;
        }
    }
}
