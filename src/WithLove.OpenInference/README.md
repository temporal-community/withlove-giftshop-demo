# WithLove.OpenInference

`WithLove.OpenInference` is an internal project reference used by the WithLove sample. It is not a
NuGet package and is not intended to be published independently.

The project provides the OpenInference helpers that this sample uses on top of
`System.Diagnostics.Activity`:

- typed CHAIN and RETRIEVER scopes;
- generated attribute constants;
- resource configuration for `openinference.project.name`;
- session and user context propagation; and
- application-controlled AI content protection.

The durable-chat integration owns model spans and Microsoft.Extensions.AI owns embedding spans in
WithLove. Application code uses this project for higher-level spans such as the chat `CHAIN` and
product-search `RETRIEVER`; it does not wrap either provider call in another OpenInference span.

## Application usage

Register the shared project name through ServiceDefaults:

```csharp
builder.AddOpenInferenceDefaults("withlove-giftshop");
```

Create application scopes from the service's existing `ActivitySource`:

```csharp
using var chain = activitySource.StartChain("chat.turn", userMessage, traceConfig);
// Run the durable chat turn.
chain.Complete(assistantMessage);
using var retrieval = activitySource.StartRetriever("product.search");
```

Use `OpenInferenceContextScope` for pseudonymous correlation values that should flow to child
application spans. Raw authenticated-user and Temporal workflow identifiers must never be assigned
to `session.id`, `user.id`, or `conversation.id`.

Sensitive inputs and outputs are disabled by default. The AppHost-level
`Telemetry:CaptureAiContent=true` setting explicitly authorizes both directions for the chat CHAIN,
the existing durable model span, TOOL payloads, and the product-search RETRIEVER input. It is passed to services as
`Telemetry__CaptureAiContent`. A missing or `false` value keeps content hidden. Any content-capture
change must preserve the trace privacy contract documented in `docs/telemetry.md`.

## Generated source

One checked-in source file is generated:

- `OpenInferenceAttributes.g.cs`

Its source of truth is `Conventions/openinference-conventions.json`. Regenerate it with:

```shell
dotnet run --project tools/WithLove.OpenInference.Generator -- generate
```

Verify that the checked-in source is current without writing files:

```shell
dotnet run --project tools/WithLove.OpenInference.Generator -- verify
```

The normal application build does not run the generator.

## Source provenance

The implementation began as a copy from the sibling `arize-demos` repository at commit
`0411753ac6d43be74a8bb7c617c8c6432ad70102`.

Copied Git tree identities:

- `src/Arize.OpenInference`: `6d2e18c914ca4078ba1b6e1cde470eb30a12ce4d`
- `tools/Arize.OpenInference.Generator`: `5c9b18dbdd0c726de36dac8e7a329d83d02b2e3a`
- `tests/Arize.OpenInference.Tests`: `3819d2b6493dd055f87d985e062a02903d82dd2e`

The local copy now retains only the CHAIN, RETRIEVER, context, content-privacy, project-resource,
and generated-attribute behavior used by WithLove. It is intentionally not a complete reusable
OpenInference SDK. The durable-chat package owns LLM spans, MEAI owns embedding spans, and
WithLove-specific telemetry identity handling belongs to `WithLove.Web`.
