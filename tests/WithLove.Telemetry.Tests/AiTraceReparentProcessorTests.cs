using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using WithLove.OpenInference;
using WithLove.ServiceDefaults.Telemetry;

namespace WithLove.Telemetry.Tests;

public class AiTraceReparentProcessorTests
{
    [Fact]
    public void ExportPipeline_ReparentsInfrastructureChildrenToScopedChatTurnAndPreservesAiChildren()
    {
        const string sourceName = "WithLove.AiTraceReparentProcessorTests";
        using var source = new ActivitySource(sourceName);
        var exporter = new CapturingActivityExporter();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new AiTraceReparentProcessor())
            .AddProcessor(new AiOnlyTraceExportProcessor(exporter))
            .Build();

        Activity root;
        ActivityTraceFlags modelFlags;
        using (root = source.StartActivity("chat.turn", ActivityKind.Internal)!)
        {
            root.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "CHAIN");
            root.SetTag("chat.operation_id", "operation-123");
            using var anchor = AiTraceAnchorScope.Push(root);
            using (source.StartActivity("temporal.update"))
            {
                using (var modelActivity = source.StartActivity("openai.chat"))
                {
                    modelActivity!.SetTag("gen_ai.operation.name", "chat");
                    modelFlags = modelActivity.ActivityTraceFlags;
                }

                using (var toolActivity = source.StartActivity("execute_tool search_products"))
                {
                    toolActivity!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "TOOL");
                    using (var retrieverActivity = source.StartActivity("product.search"))
                        retrieverActivity!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "RETRIEVER");
                }
            }
        }

        provider.ForceFlush().Should().BeTrue();
        var model = exporter.Exported.Should().ContainSingle(span => span.Name == "openai.chat").Subject;
        var tool = exporter.Exported.Should().ContainSingle(span => span.Name == "execute_tool search_products").Subject;
        var retriever = exporter.Exported.Should().ContainSingle(span => span.Name == "product.search").Subject;

        model.ParentSpanId.Should().Be(root.SpanId);
        tool.ParentSpanId.Should().Be(root.SpanId);
        retriever.ParentSpanId.Should().Be(tool.SpanId);
        model.Flags.Should().Be(modelFlags);
    }

    [Fact]
    public void ExportPipeline_LeavesParentUnchangedWhenAnchorIsMissing()
    {
        const string sourceName = "WithLove.AiTraceReparentProcessorTests.NoAnchor";
        using var source = new ActivitySource(sourceName);
        var exporter = new CapturingActivityExporter();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new AiTraceReparentProcessor())
            .AddProcessor(new AiOnlyTraceExportProcessor(exporter))
            .Build();

        Activity originalParent;
        using (source.StartActivity("chat.turn")!)
        using (originalParent = source.StartActivity("temporal.update")!)
        using (var model = source.StartActivity("openai.chat"))
        {
            model!.SetTag("gen_ai.operation.name", "chat");
        }

        provider.ForceFlush().Should().BeTrue();
        exporter.Exported.Should().ContainSingle(span => span.Name == "openai.chat")
            .Which.ParentSpanId.Should().Be(originalParent.SpanId);
    }

    [Fact]
    public void ExportPipeline_LeavesParentUnchangedWhenAnchorIsFromAnotherTrace()
    {
        const string sourceName = "WithLove.AiTraceReparentProcessorTests.MismatchedAnchor";
        using var source = new ActivitySource(sourceName);
        var exporter = new CapturingActivityExporter();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new AiTraceReparentProcessor())
            .AddProcessor(new AiOnlyTraceExportProcessor(exporter))
            .Build();

        var originalBaggage = Baggage.Current;
        try
        {
            string otherAnchor;
            using (var otherRoot = new Activity("other.chat.turn").Start())
            {
                otherAnchor = $"{otherRoot.TraceId.ToHexString()}:{otherRoot.SpanId.ToHexString()}";
            }

            Baggage.SetBaggage(
                AiTraceAnchorScope.BaggageKey,
                otherAnchor);

            Activity originalParent;
            using (source.StartActivity("chat.turn")!)
            using (originalParent = source.StartActivity("temporal.update")!)
            using (var model = source.StartActivity("openai.chat"))
            {
                model!.SetTag("gen_ai.operation.name", "chat");
            }

            provider.ForceFlush().Should().BeTrue();
            exporter.Exported.Should().ContainSingle(span => span.Name == "openai.chat")
                .Which.ParentSpanId.Should().Be(originalParent.SpanId);
        }
        finally
        {
            Baggage.Current = originalBaggage;
        }
    }

    [Fact]
    public void Push_RestoresPriorBaggageAfterChatTurnCompletes()
    {
        var original = Baggage.Current;
        try
        {
            Baggage.SetBaggage("existing", "value");
            var expected = Baggage.Current;
            using (var root = new Activity("chat.turn").Start())
            using (AiTraceAnchorScope.Push(root))
            {
                Baggage.GetBaggage(AiTraceAnchorScope.BaggageKey).Should().NotBeNull();
            }

            Baggage.Current.Should().Be(expected);
            Baggage.GetBaggage(AiTraceAnchorScope.BaggageKey).Should().BeNull();
            Baggage.GetBaggage("existing").Should().Be("value");
        }
        finally
        {
            Baggage.Current = original;
        }
    }

    private sealed class CapturingActivityExporter : BaseExporter<Activity>
    {
        internal List<ExportedSpan> Exported { get; } = [];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                Exported.Add(new(
                    activity.OperationName,
                    activity.SpanId,
                    activity.ParentSpanId,
                    activity.ActivityTraceFlags));
            }

            return ExportResult.Success;
        }
    }

    private sealed record ExportedSpan(
        string Name,
        ActivitySpanId SpanId,
        ActivitySpanId ParentSpanId,
        ActivityTraceFlags Flags);
}
