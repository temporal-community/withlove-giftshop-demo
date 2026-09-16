# Telemetry architecture

WithLove uses two complementary semantic layers on one OpenTelemetry trace:

- The durable-chat integration owns provider/model spans and token usage; an app-local chat-client
  decorator adds the standard MEAI GenAI message attributes to that same span.
- `WithLove.OpenInference` owns application spans and context: `CHAIN` for a complete chat turn,
  `RETRIEVER` for an actual product-search backend retrieval, plus pseudonymous session/user IDs.

Both direct embedding pipelines (ProductsAPI query embeddings and WorkflowServer seed embeddings)
are wrapped with MEAI OpenTelemetry instrumentation. They use each service's already-registered
activity source and force sensitive-content capture off, so embedding operations are visible without
exporting input strings or vectors.

The application does not create another OpenInference or MEAI `LLM` span around a model call. The
decorator only enriches the durable package's existing model span, which keeps one model latency,
token, and cost record.

AI payload content is disabled by default. `Telemetry:CaptureAiContent=true` is the application-level
authorization that enables `input.value`/`output.value` on the application-owned `chat.turn` CHAIN,
`gen_ai.input.messages`, `gen_ai.output.messages`, and `gen_ai.system_instructions` on the existing
durable model span, plus TOOL arguments and results. AppHost sends the resolved value explicitly to
Web and WorkflowServer through `Telemetry__CaptureAiContent`. This is the only setting that can
authorize capture; a missing or `false` value keeps all payloads hidden.
The setting does not affect span structure, model/tool names, token counts, status, timing, routing,
logs, metrics, error policy, or correlation IDs. Product retrieval/embedding payloads
remain unconditionally hidden by their component-specific policy.
The custom Temporal update-context interceptor preserves the physical hierarchy from `chat.turn`
through `UpdateWorkflow` to model, tool, and retriever spans. The per-turn `chat.operation_id` is
also carried onto application and model spans as a secondary search and verification key.

`QueryWorkflow:GetHistory` is intentionally excluded from trace export in `WithLove.Web`. History hydration probes a
lazily created workflow, and Temporal represents the ordinary "not started" result as an exception
whose message contains the raw workflow ID. Dropping only that query avoids a misleading error span
and keeps the identifier out of the backend; other Temporal operations retain the configured
sampling and export behavior.

## Destinations

| Environment | Traces | Metrics | Logs |
|---|---|---|---|
| Local AppHost with Phoenix | Arize Phoenix | Aspire dashboard | Aspire dashboard and console |
| Local AppHost with AX selected | Arize AX | Aspire dashboard | Aspire dashboard and console |
| Local process without an Arize backend | Aspire dashboard when its OTLP endpoint is present | Aspire dashboard | Aspire dashboard and console |
| Azure deployment | Arize AX by default | Aspire dashboard | Aspire dashboard, console, and the platform log pipeline |

The Aspire integration exposes two explicit backends:

- `AddArize` runs the open-source Phoenix container and gates consumers on `/readyz`.
- `AddArizeAx` models AX as an external parameter-backed resource. Consumers reference it but do
  not `WaitFor` it because AX is not an AppHost-managed process.

Phoenix uses the container filesystem by default and does not attach persistent storage. Locally it
is available through the AppHost dashboard; when selected for Azure it is deployed as an internal
Container App and its UI is not publicly exposed. That transient, internal setup is intentional and
supported for this sample application. An AppHost can explicitly opt in to persistence when needed:

```csharp
var arize = builder.AddArize("arize")
    .WithVolume("arize-data", PhoenixResource.DataMountPath);
```

The AppHost reads `Trace:Destination`. Local runs default to `Phoenix`; publish mode defaults
to `Ax`. Set it to `Aspire`, `Phoenix`, or `Ax`. Choosing Phoenix during Azure deployment creates
the same ephemeral Phoenix container in the Container Apps environment and routes service traces to
its internal endpoint. To use AX locally, store the values in the AppHost's secret store and select
it when the AppHost starts:

```bash
aspire secret set ARIZE_OTLP_ENDPOINT "<endpoint-from-the-AX-connect-page>"
aspire secret set ARIZE_API_KEY "<your-AX-api-key>"
aspire secret set ARIZE_SPACE_ID "<your-AX-space-id>"
Trace__Destination=Ax aspire start
```

The root `justfile` exposes the same backend choice with content capture disabled by default. The
`--capture` flag is an explicit privacy opt-in:

```bash
just run-phoenix
just run-phoenix --capture
just run-phoenix --all-traces
just run-ax
just run-ax --capture
just run-ax --all-traces

# Send all traces to the Aspire dashboard
just run-aspire

# Azure deployment with AI payload capture explicitly enabled
just deploy --capture

# Azure deployment with Aspire traces instead of the published AX default
just deploy --trace-destination Aspire

# Azure deployment: default AX sends all trace spans rather than only the AI trajectory
just deploy --all-traces
```

`--all-traces` sets `Trace__AiOnly=false`; it does not enable payload capture. Combine it with
`--capture` only when both broader span export and AI payload content are appropriate. The local
destination recipes are `just run-phoenix`, `just run-ax`, and `just run-aspire`; deployment uses
`--trace-destination <Aspire|Phoenix|Ax>` to override its published `Ax` default.

Captured prompts, responses, system instructions, and tool payloads can contain customer or
business-sensitive data. ProductsAPI is explicitly forced to capture-disabled even when the flag is
enabled, preserving its component-specific retrieval/embedding policy and overriding inherited
process environment. Changing this setting affects new telemetry only; it does not redact or
delete traces already retained by Phoenix or AX. Phoenix is ephemeral in this AppHost because no
volume is mounted, while AX retention and deletion must be handled separately through AX controls.

No collector region is assumed. `ARIZE_OTLP_ENDPOINT` must be the endpoint supplied for the AX
space. OTLP/HTTP is the default protocol; an endpoint ending in `/v1` is normalized to the
signal-specific `/v1/traces` path.

ServiceDefaults registers one trace exporter. `Trace:Destination` selects Aspire, Phoenix, or AX;
the two Arize backends are mutually exclusive. When either Arize backend is referenced, it receives
the connected AI trajectory by default (`Trace:AiOnly=true`) while
`OTEL_EXPORTER_OTLP_ENDPOINT` still carries logs and metrics to Aspire. Set `Trace:AiOnly=false`
to send all traces to the selected Arize backend. The Aspire destination always receives all three
signals. With no endpoint, no telemetry is exported and a startup warning explains the missing
trace destination. AX authentication headers are configured only on the named AX trace exporter,
so they cannot leak to Aspire's logs or metrics exporters.

The AI-only trajectory preserves the spans Phoenix and AX need to reconstruct a usable chat turn:
the `CHAIN` `chat.turn` root, an emitted `durable.turn` bridge, LLM chat spans, `TOOL` spans, and
`RETRIEVER` spans. It intentionally excludes unrelated ASP.NET Core, HTTP client, EF Core, and
standalone embedding spans. The filter is not used for the Aspire destination.

All three services use `openinference.project.name=withlove-giftshop`. Their distinct
`service.name` values remain intact for filtering inside that project.

## Trace privacy contract

No exported trace, whether routed to Phoenix or the Aspire dashboard, may contain the raw
authenticated-user claim ID or raw Temporal workflow ID. Logs are explicitly outside this
guarantee and require their own privacy audit.

The Web app derives versioned, domain-separated HMAC-SHA256 pseudonyms. The helper is owned by
`WithLove.Web`; it remains outside the reusable `WithLove.OpenInference` conventions project.

The sample uses one committed key and the `demo-v1` key version in every environment. This removes
deployment setup friction, prevents raw identifiers from appearing directly in traces, and keeps
demo trace grouping stable. It is not production-grade pseudonymization: the key is public, so
someone with candidate identifiers can reproduce their HMAC values. Do not reuse this design for
an application that handles real customers or production data.

WorkflowServer receives the safe session value as durable `ConversationId`; the raw workflow ID
is used only as the Temporal routing key. Temporal OpenTelemetry interceptors set
`TagNameWorkflowId=null` so they cannot emit `temporalWorkflowID`.
