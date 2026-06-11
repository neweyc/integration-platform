# Design: Message-Triggered Integrations (publish / subscribe)

**Status:** Implemented (v1, database-backed delivery — no external broker).
**Author:** —
**Related:** `docs/agent-capability-tags.md`, `docs/edge-gpio-integrations.md`, the Workflows
feature, the trigger-adapter pipeline (`src/ControlPlane/Features/Triggers`)

## Implementation notes (what shipped vs. this design)

- **`context.Trigger`** is the inbound surface, a `TriggerInfo` discriminated hierarchy
  (`src/Sdk/TriggerInfo.cs`); the message metadata is `MessageTrigger`. The discriminator enum is an
  SDK-local `TriggerKind` (the SDK does not depend on the control plane's `Shared.Domain.TriggerSource`).
- **Publish:** `context.Messages.PublishAsync(...)` → agent `POST /api/agent/messages` →
  `PublishMessageHandler` (`src/ControlPlane/Features/Messages`) persists the `Message` envelope and
  fans out one Queue work item per subscriber via the existing `TriggerWorkItemProducer`. Delivery is
  DB-backed; subscribers are claimed by the existing `ClaimPendingQueueRunsAsync` path.
- **`WorkItem` gained a nullable `MessageId`** FK to the envelope (the one deviation from "WorkItem:
  none" below) so the poll response can surface message metadata to the agent.
- **`context.Trigger` is fully populated for every active source:** Scheduled (time + cron), Webhook
  (delivery id), Workflow (run/node/upstream-execution), Retry (attempt number), and Message. Manual
  is a marker (the platform stores no user attribution for a manual run); the File adapter is not
  active yet, so a File/unknown source falls back to the manual marker.
- **Known v1 limitations:** broker-backed delivery, loop protection, and cross-environment delivery
  remain open (see Open questions).

## Problem

Integrations can be triggered by a schedule, an inbound webhook, a manual run, or a Workflow edge.
What they cannot do today is **react to a fact raised by another integration**.

The motivating case: a Raspberry Pi agent reads a GPIO pin and detects high wind. It should be able
to *raise an event* — "high wind, at this time" — and have a **separate** integration react to it
(write a record, page on-call, open a damper), possibly running on a **different** agent. The
detector should not have to know who, if anyone, is listening.

This is **choreography**, and it is distinct from the Workflows feature we already have:

- **Workflows = orchestration.** A static DAG defined up front; an edge A→B fires B *every time* A
  succeeds. Edges are unconditional and the topology is known at design time.
- **Messages = choreography.** A publisher emits a fact *conditionally* (only when over threshold)
  and stays ignorant of subscribers. Subscribers opt in independently. The topology is emergent.

Both are wanted; this doc adds the second. It is also useful far beyond hardware — any integration
reacting to any other ("order.created" → sync to NetSuite, "invoice.paid" → provision access).

## Goals

- Let an integration **publish** a message (a typed object) from inside `RunAsync`.
- Let an integration **subscribe** to a message subject declaratively, the same way it declares a
  schedule or webhook today.
- Reuse the existing trigger→work-item pipeline: dedup, payload, routing (incl. capability tags),
  execution, history, and **parent/root execution lineage** end to end.
- Ship the first version on **existing infrastructure only** — no external message broker.
- Preserve the "one container + Postgres" self-host story.

## Non-goals

- **An external broker (NATS / Service Bus / RabbitMQ / Kafka).** Explicitly deferred; the
  trigger-adapter seam keeps it a later, integration-code-invisible addition. See *Transport*.
- **Runtime (imperative) subscription** — `bus.Subscribe(...)` at execution time. Subscriptions
  must be known to the control plane to route work; they are declared in code and discovered at
  package upload, not registered at runtime. (This asymmetry is why the SDK surface is a
  publish-only `context.Messages`, not a symmetric "bus" — see Open Questions.)
- **Ordered / exactly-once delivery.** v1 is at-least-once with idempotent dedup. See *Semantics*.
- **A typed contract shared across packages as a compiled type.** The wire contract is the subject
  string + JSON shape. The .NET message type is local ergonomic sugar over that. See design §3.

## Current state (what already exists — this is mostly built)

The dispatch machinery a "message bus" would need is already in place:

- **Trigger adapters.** `ITriggerAdapter` / `TriggerAdapterCatalog` enumerate the sources. A
  `QueueTriggerAdapter` descriptor **already exists**, reserved for "queue and event-bus messages,"
  and `TriggerSource.Queue` / `TriggerType.Queue` are already defined
  (`src/Shared/Domain/ExecutionRecord.cs`, `Integration.cs`).
- **Work-item production.** `TriggerWorkItemProducer.EnqueueAsync` turns any trigger event into a
  `WorkItem` carrying `Payload`, a `DeliveryId` for **idempotency dedup** (the `23505` unique-
  violation path), and lineage (`ParentExecutionId`, `RootExecutionId`, `WorkflowNodeId`).
- **Choreography precedent.** `WorkflowProgressionService` already enqueues a downstream work item
  when an upstream execution completes, writing a `TriggerEvent` with upstream-execution metadata.
  Message delivery is the same move, keyed on a subject instead of a graph edge.
- **Discovery.** `AssemblyScanner` already extracts `[ScheduledIntegration]` / `[WebhookIntegration]`
  into `DiscoveredTrigger(Slug, TriggerType, CronExpression)`, and `UploadPackage` provisions an
  `IntegrationTrigger` per discovered trigger (declared-default + operator-override pattern).
- **Agent channel.** Agents already call `POST /api/agent/executions` and
  `PUT /api/agent/executions/{id}` authenticated by `X-Agent-Token`. Publishing rides the same
  channel — no new connection model.

What's missing is narrow: a **publish verb**, a **subscribe attribute**, and a **delivery step**
that fans a published subject out to subscribers via the producer above.

## Proposed design

### 1. Publish API on the integration context

A new `Messages` property on `IIntegrationContext`, publish-only:

```csharp
public interface IIntegrationContext
{
    // ... existing: Secrets, Logger, Http, Execution, Payload ...

    // Outbound: publish-only capability.
    IMessagePublisher Messages { get; }

    // Inbound: how THIS run was triggered (schedule, webhook, message, ...). Always present;
    // pattern-match the concrete type for source-specific fields (see §3a).
    TriggerInfo Trigger { get; }

    // Deserialize the raw body (Payload) into T. Works for webhook and message bodies alike.
    T? PayloadAs<T>();
}

public interface IMessagePublisher
{
    // Primary: typed message object. Subject derived from the message type (see §3).
    Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : class;

    // Escape hatch: explicit subject + arbitrary payload.
    Task PublishAsync(string subject, object payload, CancellationToken ct = default);
}

// `TriggerInfo` and its per-source records (incl. `MessageTrigger`, the §3a metadata sibling) are
// defined in §3a. The message body stays raw in context.Payload — it is never wrapped.
```

Usage in the wind detector:

```csharp
if (overThreshold)
    await context.Messages.PublishAsync(new HighWindDetected(context.Execution.ScheduledAt), ct);
```

The message type is a plain record — **no base class or marker interface required** (keep it
framework-light):

```csharp
[Message("high-wind")]            // optional; otherwise the subject is derived from the type name
public record HighWindDetected(DateTime ObservedAt);
```

On publish, the SDK resolves the subject (§3), serializes the message to JSON, and `POST`s it to a
new agent endpoint (§4) over the existing agent token channel. Publish is **fire-and-forget from
the integration's perspective**: it enqueues for delivery and returns; it does not block on
subscribers running.

### 2. Subscribe declaratively (attribute, discovered at upload)

A new attribute mirroring `WebhookIntegrationAttribute`:

```csharp
[MessageIntegration("High Wind Job", "high-wind-job", subject: "high-wind")]
public class HighWindJob : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var msg = context.PayloadAs<HighWindDetected>();          // the raw body, deserialized (§3)
        var trigger = (MessageTrigger)context.Trigger;           // a [MessageIntegration] only runs on messages
        context.Logger.LogInformation("via {Subject} from {Src}", trigger.Subject, trigger.SourceExecutionId);
        var db = context.SqlConnector("WIND_DB_CONNECTION_STRING");
        await db.ExecuteAsync(
            "INSERT INTO HighWindEvents (Id, ObservedAt) VALUES (@Id, @ObservedAt)",
            new { Id = context.Execution.ExecutionId, msg!.ObservedAt }, ct);
    }
}
```

- `AssemblyScanner` extracts it as `DiscoveredTrigger(Slug, TriggerType.Queue, Subject: "high-wind")`.
- `UploadPackage` provisions an `IntegrationTrigger { Type = Queue, Subject = "high-wind" }`,
  reusing the declared-default + override + drift pattern (an operator can re-point or disable a
  subscription without a redeploy).
- The published body arrives as the existing `context.Payload`; `context.PayloadAs<T>()` deserializes
  it (purely local — see §3). Subject/source/time arrive via `context.Trigger` (a `MessageTrigger`; §3a).

Subscription is declarative precisely because the **control plane** must know the subject→integration
map to route a published message to work. There is no runtime `Subscribe`.

### 3. The subject is the contract; the type is sugar

This is the load-bearing decision. A publisher and a subscriber may live in **different packages on
different agents**, so they cannot rely on sharing a compiled `HighWindDetected` type. Therefore:

- **Matching is by subject string.** `"high-wind"` is the durable contract, plus the JSON shape of
  the body.
- **The typed object is local convenience on each side.** On publish, `PublishAsync<T>` *derives the
  subject from `T`* (the `[Message]` attribute, else a kebab-case of the type name) and serializes
  `T` to the body. On subscribe, `context.PayloadAs<T>()` *deserializes the body into the consumer's
  own `T`*, which need not be the same CLR type the publisher used — only structurally compatible.
- **Consequence for versioning:** evolve the body additively (tolerant reader). A renamed/removed
  field is a breaking change to the subject contract, same as any wire API. Documented, not enforced
  by the type system.

This gives the .NET-idiomatic developer experience (publish/consume objects, no manual JSON) without
falsely implying cross-package type identity.

### 3a. Delivery shape: raw body + metadata sibling (decided)

A message has an envelope **on the wire** — the agent→CP request carries `{ subject, body,
sourceExecutionId }`, because subject and source must travel with the body. The decision is whether
that envelope **leaks into what the subscriber reads as its payload**. It does not:

- **`context.Payload` is the raw body**, byte-for-byte what the publisher serialized — identical to
  how a webhook body arrives today. `PayloadAs<T>()` deserializes it.
- **Transport metadata is a sibling, not a wrapper:** subject / message id / publish time / source
  execution are exposed via `context.Trigger` (as a `MessageTrigger`), *next to* the body, never
  around it.
- **The full envelope is persisted server-side** (the `Message` table) for observability, replay,
  and passthrough/bridge consumers — *storage format ≠ delivery format*.

`context.Trigger` describes how *any* run was triggered — one record per source — so a process can
know its trigger and pattern-match for the details:

```csharp
// Kind is an SDK-local enum (the SDK does not depend on the control plane's TriggerSource).
public abstract record TriggerInfo(TriggerKind Kind);

public sealed record ScheduledTrigger(DateTime ScheduledAt, string? CronExpression)
    : TriggerInfo(TriggerKind.Scheduled);
public sealed record WebhookTrigger(string? DeliveryId)
    : TriggerInfo(TriggerKind.Webhook);
public sealed record ManualTrigger()                                   // marker — no user attribution
    : TriggerInfo(TriggerKind.Manual);
public sealed record WorkflowTrigger(Guid WorkflowRunId, Guid WorkflowNodeId, Guid? UpstreamExecutionId)
    : TriggerInfo(TriggerKind.Workflow);
public sealed record RetryTrigger(int AttemptNumber)
    : TriggerInfo(TriggerKind.Retry);
public sealed record MessageTrigger(string Subject, Guid MessageId, DateTime PublishedAt, Guid? SourceExecutionId)
    : TriggerInfo(TriggerKind.Message);
```

A message subscriber downcasts safely (it only ever runs on a message); a multi-trigger integration
switches:

```csharp
switch (context.Trigger)
{
    case MessageTrigger m:   /* react to m.Subject */  break;
    case ScheduledTrigger s: /* periodic sweep */      break;
}
```

Rationale:

1. **Consistency** — `context.Payload` keeps one meaning ("the raw inbound body") across webhook and
   message triggers; an enveloped payload would make it mean different things per source.
2. **The subscriber already knows its subject** (it declared it via `[MessageIntegration]`), so
   wrapping the body to redeliver the subject is redundant for the common case.
3. **Metadata is already sibling data** — lineage (`ParentExecutionId`/`RootExecutionId`) arrives via
   the execution record, not the body; the rest of the metadata belongs in the same place.
4. **Business time vs. transport time stay separate** — the domain timestamp (e.g. `ObservedAt`)
   lives in the body; the publish timestamp lives in `IncomingMessage.PublishedAt`. One envelope
   timestamp would blur them.

The one case an envelope-around-body would serve natively is a generic bridge ("forward every
message to Kafka"). That is served instead by the server-side `Message` record + a query/bridge API,
not by reshaping the mainline subscriber payload.

### 4. Delivery pipeline (reuses the existing producer)

```
Agent: context.Messages.PublishAsync(msg)
   └─ POST /api/agent/messages  { subject, body, sourceExecutionId }   (X-Agent-Token)
         └─ CP MessageDeliveryHandler:
              1. Resolve subscribers: IntegrationTrigger where Type=Queue, Subject=subject,
                 Enabled, in the same tenant + environment as the publishing execution.
              2. For each subscriber → TriggerWorkItemProducer.EnqueueAsync(
                     TriggerSource.Queue, Payload = body,
                     DeliveryId = hash(messageId, subscriberIntegrationId),   // idempotency
                     ParentExecutionId = sourceExecutionId,
                     RootExecutionId   = source root)                          // lineage
              3. Record a TriggerEvent per subscriber (ConvertedToWork / Deduplicated),
                 exactly as webhook and workflow delivery already do.
```

No new dispatch, claim, execution, or history code — a `Queue`-sourced `WorkItem` is just another
row the agents already poll, routed by the existing environment + capability-tag rules.

**Fan-out:** N subscribers to a subject ⇒ N work items, one per subscriber, each independently
dedup'd and routed. **Zero subscribers ⇒ the message is recorded and dropped** (a `TriggerEvent`
with no work item), which is correct for choreography — publishing a fact nobody consumes is not an
error, but it *is* observable.

### 5. Transport: database first, broker behind the same seam later

v1 delivery is **synchronous-enough**: the publish `POST` resolves subscribers and inserts work
items in one transaction; agents pick them up on their normal poll. Latency is seconds — fine for a
wind alarm or any business event.

Because delivery sits behind the `ITriggerAdapter` boundary, a low-latency broker can be introduced
later **without touching integration code or the publish/subscribe API** — the `QueueTriggerAdapter`
gains a NATS/Service Bus/RabbitMQ/Kafka-backed source, and the publish endpoint forwards to it
instead of inserting rows. Gate that strictly on a demonstrated need for sub-second fan-out or
cross-cluster delivery; do not build it on spec.

## Data model changes

| Entity | Change |
|--------|--------|
| `IntegrationTrigger` | add `Subject string?` (the subscribed subject; null for non-queue triggers); `DeclaredSubject string?` for the override/drift pattern |
| `DiscoveredTrigger` (scan model) | add `Subject string?` |
| `Message` (new) | the full published envelope, persisted for observability/replay/bridge (§3a): `Id`, `TenantId`, `Environment`, `Subject`, `Body`, `SourceExecutionId`, `PublishedAt`. This is the canonical store of the envelope that delivery deliberately does **not** wrap around the subscriber's body. |
| `WorkItem` | add nullable `MessageId` FK to `Message` (so the poll response can surface message metadata); `Payload`, `DeliveryId`, `ParentExecutionId`, `RootExecutionId` already existed |

One EF migration (nullable `Subject` columns default null → no drift for existing triggers).

## Integration points

- **SDK:** `IIntegrationContext.Messages` + `IMessagePublisher`; `MessageIntegrationAttribute`;
  `[Message]` subject attribute; `context.Message<T>()` helper. (`src/Sdk`)
- **Scan:** `AssemblyScanner` recognizes `Serto.Sdk.MessageIntegrationAttribute` →
  `DiscoveredTrigger(..., TriggerType.Queue, Subject)`. `UploadPackage` provisions/updates the
  subscription `IntegrationTrigger` with override+drift.
- **Agent:** `IMessagePublisher` implementation that `POST`s to `/api/agent/messages` with the
  agent token; inject the source execution id from the current run.
- **Control plane:** `POST /api/agent/messages` endpoint; `MessageDeliveryHandler` (subscriber
  resolution + per-subscriber `EnqueueAsync`); promote `QueueTriggerAdapter` from descriptor-only to
  an active adapter; optional `Message` log + UI surface alongside Trigger Events.

## Delivery semantics

- **At-least-once.** Dedup via `DeliveryId = hash(messageId, subscriberIntegrationId)` against the
  existing work-item unique index — a retried publish does not double-fire a subscriber.
- **No ordering guarantee.** Subjects are independent; within a subject, work items are claimed by
  `AvailableAt` but not strictly serialized. Subscribers must not assume order.
- **Lineage.** `ParentExecutionId`/`RootExecutionId` thread from the publishing execution to each
  subscriber execution, so the UI can show "high-wind-job ran because high-wind-monitor published
  high-wind at 14:03" — the choreography equivalent of the workflow run graph.
- **Failure isolation.** A subscriber that throws fails its own work item (with the normal retry
  policy); it does not affect the publisher or sibling subscribers.
- **Loops.** A subject whose subscriber re-publishes the same subject can cycle. Mitigation options
  (cap propagation depth via the lineage chain, or detect a subject revisiting its root execution)
  — decide in review.

## Backward compatibility

- No new attribute ⇒ no subscriptions; existing integrations unaffected.
- `Subject` columns are nullable and ignored by non-queue triggers.
- The `Queue` adapter activating does not change scheduled/webhook/manual/workflow routing.

## Decisions

- **SDK surface name → `context.Messages`** (publish-only; honest about the publish-imperative /
  subscribe-declarative asymmetry; doesn't overpromise a broker). Rejected `context.MessageBus`.
- **Delivery shape → raw body + metadata sibling** (§3a). `context.Payload` stays the raw body;
  trigger metadata is exposed via `context.Trigger`; the full envelope is persisted in the
  `Message` table. Rejected wrapping the body in an envelope.
- **Inbound trigger surface → `context.Trigger`**, a `TriggerInfo` discriminated hierarchy describing
  how *any* run was triggered (not message-only), so a process can branch on its trigger source.
  `MessageTrigger` carries the §3a message metadata. Rejected the message-only `IncomingMessage`.
- **Publish observability → a dedicated `Message` table** (follows from §3a — it is the canonical
  envelope store), with a `TriggerEvent` per subscriber as today for the delivery/dedup record.
- **Subject naming → `[Message("...")]` if present, else kebab-case of the type name only** (not
  namespace-qualified). Justified by tenant+environment-scoped delivery: subjects need uniqueness
  only within a tenant's own packages, so namespace-coupling the wire contract isn't worth its
  refactor-fragility. `[Message]` is the documented norm for cross-package contracts.

## Open questions

1. **Cross-environment / cross-tenant delivery:** v1 confines delivery to the publishing execution's
   tenant + environment (matches secret/isolation boundaries). Cross-environment fan-out is a
   separate design, like multi-environment agents.
2. **Loop protection** mechanism (depth cap vs. cycle detection).
3. **Workflow vs. message overlap:** should a Workflow edge be expressible as "publishes subject X"?
   Keep them separate for v1; revisit if users conflate them.

## Rollout

1. Data model + migration (`IntegrationTrigger.Subject`); SDK attribute + `AssemblyScanner`
   extraction (subscriptions recorded, no delivery yet).
2. SDK `context.Messages.PublishAsync` + agent `POST /api/agent/messages`; CP `MessageDeliveryHandler`
   + `QueueTriggerAdapter` activation (DB-backed delivery goes live).
3. `context.PayloadAs<T>()` typed-consume helper + `context.Trigger` / `TriggerInfo` hierarchy +
   `[Message]` subject convention.
4. Observability: published-message log + lineage in the UI; loop protection.
5. (Deferred, demand-gated) broker-backed `QueueTriggerAdapter` for low-latency fan-out.
