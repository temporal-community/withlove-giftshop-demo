# Durable AI chat architecture

GiftShop uses the published `TemporalCommunity.Extensions.AI` 0.14.2 package to run LA, the chat
shopping assistant. The application keeps its existing `Microsoft.Extensions.AI` model provider,
but the package owns durable turn serialization, model/tool iteration, activity retries, session
history, shutdown, and continue-as-new.

## Execution model

Web starts the explicitly named `WithLove.GiftShopChatWorkflow`. A message is submitted through the
`SendMessage` Temporal Update. The workflow derives from
`DurableToolWorkflowBase<GiftShopChatRequestData, GiftShopChatTurnState>` and executes:

1. `TemporalCommunity.Extensions.AI.GetChatStep` for one model response.
2. One `TemporalCommunity.Extensions.AI.InvokeFunction` activity for every requested tool.
3. Another model step after tool results, until the model returns a final response, reports an
   incomplete terminal response, or reaches the configured 40-iteration limit.

GiftShop dispatches tools sequentially. Cart and navigation tools replace typed turn state, so a
later tool in the same model response sees the result of an earlier tool. This is required for
sequences such as `add_to_cart` followed by `view_cart`.

The Blazor UI remains non-streaming: it waits for the durable Update to finish, then applies the
returned commands and displays the final assistant text.

Each model step has a 4,000-token output ceiling and requests low reasoning effort with reasoning
output omitted. Package 0.14.2 preserves the provider finish reason and classifies `Length`,
`ContentFilter`, and unknown terminal reasons as `IncompleteResponse` before dispatching any tool
calls from that response. GiftShop does not retry the provider inside the same model activity.

## Split-process registration

Web is declaration-only. `AddGiftShopChatWorkflowClient` registers the durable workflow-input
factory and all 13 model-visible tool declarations. Web does not host tool implementations and does
not put tools in `ChatOptions.Tools`.

workflowServer creates the hosted worker, then calls `ConfigureGiftShopChatWorker` to configure durable
AI, the concrete GiftShop workflow, the same declarations, and scoped implementation factories. The provider `IChatClient`
is intentionally bare; it must not use MEAI `UseFunctionInvocation()` because the durable package
owns function invocation.

Both registrations consume `GiftShopChatToolCatalog`, which freezes each tool name, description,
argument schema, and return schema. `AITool.AdditionalProperties` remains empty as required by
package 0.14.2.

### How each process acquires the data converter

`AddDurableAI` applies `DurableAIDataConverter` to the hosted worker client. Because GiftShop runs
chat and ordinary workflows on that shared worker, the converter applies to every workflow and
activity payload handled by it, not only `WithLove.GiftShopChatWorkflow`. Any independently created
Temporal client that reads those payloads must also use `DurableAIDataConverter.Instance`.
`DatabaseSetupHostedService` configures its direct startup client accordingly.

**Web gets the same converter, but as a side effect of a call that does not mention converters.**
This is the least obvious wiring in the feature, so it is spelled out here:

```
AddGiftShopChatWorkflowClient()
  → AddDurableChatWorkflowInputFactory(taskQueue, ConfigureDurableExecution)
    → DurableAIRegistrar.RegisterWorkflowInputServices(services, options)
      → RegisterClientDataConverterServices(services)
        → services.TryAddEnumerable(
              IConfigureOptions<TemporalClientConnectOptions> → DurableAIClientOptionsConfigurator)
```

`DurableAIClientOptionsConfigurator` runs `DurableAIDataConverterPlugin.ApplyToConnectOptions`
against the `TemporalClientConnectOptions` produced by `AddTemporalClient`. Web registers exactly
one DI `ITemporalClient`, so the durable-chat registration **reconfigures the client that
`StripeEventHandler` and `TemporalLoyaltyService` also resolve**. Nothing in
`AddGiftShopChatWorkflowClient`'s name signals this. If you ever split Web's Temporal clients, or
stop calling `AddGiftShopChatWorkflowClient`, those two consumers change converter silently.

### The converter is applied conditionally, and a skip is log-only

Both application paths — the plugin on the worker client and the `IConfigureOptions` on Web's
connect options — apply the converter **only while `DataConverter` is still `DataConverter.Default`**:

```csharp
if (options.DataConverter == DataConverter.Default)
{
    options.DataConverter = DurableAIDataConverter.Instance;   // applied
}
else
{
    _logger?.LogConverterSkippedForConnectOptions(...);        // skipped — log only, no throw
}
```

`DataConverter` is a record, so any non-default value fails the equality check. Setting a custom
converter **or merely attaching a `PayloadCodec`** (encryption, compression) therefore silently
opts the process out of `DurableAIDataConverter` — with a log line and no exception. Startup
succeeds, the worker connects, and the mismatch first appears as successful workflows returning
null or default-valued typed members.

If GiftShop ever needs a codec, do not choose between the codec and the AI converter: compose them
(`DurableAIDataConverter.Instance with { PayloadCodec = codec }`) or fail startup loudly on an
incompatible converter. Assigning `DurableAIDataConverter.Instance` unconditionally would discard
the codec, which is the same class of silent-data bug in the other direction.

## Turn contracts and identity

`GiftShopChatRequestData` contains the logical operation ID and trusted Web-created user context.
It is available to activities but absent from the model-visible tool schema.

`GiftShopChatTurnState` contains:

- the cart snapshot supplied at the start of the turn;
- accumulated cart commands;
- accumulated navigation commands.

`ChatService` creates one operation ID for a logical send and uses it as request data and
`CorrelationId`. It is **correlation and telemetry only** — it is deliberately *not* set as
`WorkflowUpdateOptions.Id`.

The operation ID is a fresh GUID minted per call and never persisted or replayed, so using it as an
Update ID would provide no deduplication at all — not across continue-as-new boundaries, and not
even within a single run. Setting it would imply an idempotency guarantee that does not exist. The
client instead asserts that the operation ID, `CorrelationId`, and `RequestData.OperationId` agree,
turning a malformed send into a local throw rather than a Temporal round-trip.

No durable tool performs an external *mutating* side effect — every outbound call is a read — so
there is nothing for an idempotency key to protect today. If a tool with an external effect is ever
added, it needs a real business idempotency key that is stable per logical send and survives
continue-as-new — not the Update ID. Current tools return commands or perform reads; Web applies
returned cart commands once after a final response.

The Web workflow client rejects null turn options and caller-supplied tools before Temporal
serialization, because MEAI's durable wire shape cannot preserve those invalid values. The Update
validator rejects malformed identity, request, message, state, or dispatch data before any activity
is scheduled, repeats the option/tool checks as defense in depth, and rejects requests after
shutdown.

## Frozen execution settings

Every workflow starts from a factory-created input with these settings:

| Setting | Value |
|---|---|
| Default package workflow | Disabled |
| Workflow ID prefix | `giftshop-chat-` |
| Workflow-run lifetime | 24 hours |
| Model/tool activity timeout | 2 minutes |
| Heartbeat timeout | 2 minutes |
| Retry policy | 2s initial, 2.0 backoff, 30s maximum, 3 attempts |
| Maximum tool iterations per turn | 40 |
| Maximum output tokens per model step | 4000 |
| Reasoning effort | Low |
| Consecutive errors per request | 3 |
| Maximum history entries before continue-as-new | 1000 |
| Package search attributes | Disabled |
| Detailed activity errors | Disabled |

The 24-hour value is a workflow-run lifetime, not an inactivity timeout; a successful turn does not
reset it. The application explicitly sets 40 because package 0.14.2 defaults to 20 while the prior
MEAI function-invocation path defaulted to 40.

`MaxEntryCount` is not just the continue-as-new trigger — **it is also the trim divisor.** GiftShop
configures no `HistoryReducer` and no `HistoryReducerKey`, so the package's `DefaultBoundedTrim`
runs on **every** continue-as-new, whichever condition triggered it (`ContinueAsNewSuggested` or
`history.Count >= MaxEntryCount`). It carries `min(history.Count, max(1, MaxEntryCount / 2))`
entries into the next run. At `MaxEntryCount = 1000` that is up to 500 entries carried as a single
continue-as-new input payload. Lowering `MaxEntryCount` lowers both the CAN frequency threshold and
the carried-payload size together; there is no separate knob for the latter.

If the model reaches the limit, the workflow returns `IterationLimitReached` and this exact visible
message:

```text
Maximum tool-call iterations (40) exceeded; the conversation did not converge on a final answer.
```

Web displays the message but applies no cart or navigation commands from that non-final turn.
Normal completed turns retain their model/tool protocol. Iteration-limited and provider-incomplete
turns retain only a sentinel in durable conversation history, so typed cart or navigation commands
from a non-final turn are neither applied by Web nor presented to the model on the next turn.

## History and payload disclosure

For completed turns, the package stores the complete per-turn MEAI response and includes prior
assistant function calls and matching tool results in later model requests. If a turn reaches the
iteration limit, 0.14.2 returns the complete attempted protocol and state to Web for diagnostics but
persists only the terminal assistant sentinel. For `IncompleteResponse`, it likewise returns the
diagnostic model response and provisional state while persisting only an incomplete-response
sentinel. GiftShop discards state from both non-final outcomes, and later model requests do not
inherit their tool calls or results. The UI history projector renders only user text plus
customer-safe assistant text; UI filtering by itself is not a data-removal boundary.

Temporal payload/history can contain:

- customer name in model instructions;
- user messages and operation identity;
- user ID and cart snapshot in request/state data;
- tool names, call IDs, arguments, product/cart/navigation results, loyalty balances, and errors;
- final cart and navigation commands — a cart command carries the product name, price,
  Stripe price ID and image URL, none of which the model itself ever sees.

Model instructions, user messages, and retained historical tool protocol are disclosed to the
configured model provider. Never place secrets, credentials, authorization tokens, or unnecessary
claims in these fields.

`GiftShopChatRequestData.User` is created only at the authenticated Web boundary. Model-hidden data
is not automatically authenticated, authorized, secret, or tamper-proof. Tools may use the user ID
to locate current authoritative data, but must not treat request data or turn state as authorization
evidence. A future tool with an external effect must obtain a current authorization decision inside
its activity immediately before the effect.

## History projection and state application

Immediate responses and reconnect history use the same projection rule and omit system/tool
protocol. For `FinalResponse`, Web displays the last non-empty assistant text and applies typed cart
and navigation commands. For `IterationLimitReached`, it displays the package limit message and
applies no commands. For `IncompleteResponse`, both immediate and reconnect paths display the same
customer-safe fallback and apply no commands; the package's model-facing sentinel remains in
durable history without being shown verbatim in the UI.

`search_products` asks ProductsAPI for four matches and independently caps its model-facing summary
at four entries. The local cap prevents an unexpectedly oversized provider response from consuming
the model context even if the API ignores its `top=4` request.

Authenticated workflow IDs use `giftshop-chat-{userId}`. Anonymous sessions use
`giftshop-chat-anon-{chatId}`, where `chatId` is the value of the `wl-chat-id` cookie rather than a
value minted per circuit — which is what lets an anonymous conversation survive a refresh, a second
tab, or a dropped SignalR circuit. `AnonymousChatMiddleware` mints the cookie when it is absent, and
login and logout rotate it; `ChatIdentityCookie` owns its name, options and validation. A cookie
that is not exactly the shape this application mints is treated as absent, so an attacker-controlled
string is never spliced into a workflow ID.

The workflow is started lazily by the first message, not by opening the chat panel, so a visitor who
opens the panel and never types creates nothing in Temporal. Starts use
`WorkflowIdConflictPolicy.UseExisting` for an active session and
`WorkflowIdReusePolicy.AllowDuplicate` after a closed session — so a cookie that outlives its
workflow simply starts a new run under the same ID. This is a sample, so old `ChatAgentWorkflow`
executions are not migrated.

## Wire-format compatibility and the rollback one-way door

`DurableAIDataConverter` changes the JSON wire shape of **every** payload the configured client
touches — not only chat payloads — because Web and workflowServer each use one client for all
workflows. That makes deploy-direction compatibility a property worth stating explicitly.

**Forward (deploy) is safe.** History written before the change uses PascalCase property names and
numeric enum values. `DurableAIDataConverter` deserializes with `PropertyNameCaseInsensitive = true`
and a `JsonStringEnumConverter` configured to allow integer values, so old payloads round-trip
correctly under the new converter. In-flight `StripeCheckoutOrderWorkflow`,
`LoyaltyAccountWorkflow`, and `CustomerOnboardingWorkflow` executions survive the deploy.

**Reverse (rollback) is silently destructive.** The new converter writes camelCase property names
and string enum values. The stock converter has `PropertyNameCaseInsensitive = false`. A rolled-back
build therefore fails to bind almost every property written by the new build: a resumed
`StripeCheckoutOrderWorkflow` comes back with `CheckoutSessionId = null`, a resumed
`LoyaltyAccountWorkflow` with `Balance = 0`. **No exception is thrown and nothing is logged.** The
workflow reports success while operating on defaulted state.

The transferable lesson, which is the reason this section exists in a sample repo:

> **A data-converter swap is a one-way door unless the rollback build pins the new converter.**
> Case-insensitive deserialization makes the *forward* direction look safe and is easy to verify;
> it says nothing about the reverse direction, because the property that saves you going forward
> (tolerant reads) is exactly the property the old build lacks. Whenever you change a serialization
> format on durable state, test `new → old` explicitly, not only `old → new`. A test that only
> covers the safe direction produces false confidence.

Two corollaries worth copying:

- Compatibility tests for a converter change must assert **both** directions. `old → new` passing
  is the expected result and proves little.
- If you cannot pin the converter in the rollback build, the only safe rollback is to drain
  in-flight executions of every affected workflow type first — which means the change is
  operationally irreversible for the duration of your longest-running workflow.

## Observability

workflowServer exports its application source, Temporal SDK sources, and
`DurableChatTelemetry.ActivitySourceName`. A durable turn therefore emits package spans named
`chat ...` and `execute_tool ...`, in addition to Temporal workflow/activity spans.

Web records `chat.turn.duration_ms` around the end-to-end Update and tags the `chat.turn` span with
the operation ID and completion reason. Successful final and capped turns use `FinalResponse` and
`IterationLimitReached`; failed workflow/client calls use `Failed` and set the span error status.
The histogram and span use the same completion reason. `chat.message.cart_actions` is recorded only
when Web applies returned commands.

## Tests and replay

Run the fast unit and server-free replay lane:

```bash
just test-unit
```

Run the real-Temporal chat integration lane. It uses a scripted `IChatClient`, fake product HTTP
responses, and local Temporal dev-server release 1.7.2; it does not call OpenAI, the real Products API,
SQL Server, Redis, or Docker:

```bash
just test-chat-integration
```

The checked-in replay fixture is
`tests/WithLove.Workflows.Tests/Replay/Histories/giftshop-chat-v1.json`. To regenerate it after an
intentional deterministic workflow change:

```bash
GIFT_SHOP_CHAT_HISTORY_OUTPUT=/tmp/giftshop-chat-v1.json \
dotnet test tests/WithLove.Workflows.Tests/WithLove.Workflows.Tests.csproj \
  --filter "FullyQualifiedName~SequentialCartTurn_UsesSeparateActivitiesAndCompletedState"
cp /tmp/giftshop-chat-v1.json \
  tests/WithLove.Workflows.Tests/Replay/Histories/giftshop-chat-v1.json
dotnet test tests/WithLove.Workflows.Tests/WithLove.Workflows.Tests.csproj \
  --filter "FullyQualifiedName~GiftShopChatWorkflowReplayTests"
```

CI reads the checked-in fixture and never rewrites it.

## Troubleshooting

- Mixed managed-tool/function-invocation error: remove `UseFunctionInvocation()` from the
  workflowServer chat client. Tool execution belongs to the durable package.
- Tool configuration failure: confirm Web and workflowServer both use
  `GiftShopChatToolCatalog` and that `AdditionalProperties` is empty.
- Unsupported server error: use Temporal Server 1.31.0 or newer. Local development pins
  `temporalio/temporal:1.7.2`, which contains Server 1.31.1.
- Missing chat search attributes: expected. GiftShop disables the package's optional search
  attributes; only the application's existing Stripe/customer attributes need registration.
- Successful workflow with null/default typed result members: verify that every manually created
  Temporal client uses `DurableAIDataConverter.Instance`. Do not hide a converter mismatch with
  null guards or by ignoring hosted-service failures.
- Old local chat executions conflict: use a clean local namespace/volume. Migration from the old
  demo workflow is intentionally out of scope.
