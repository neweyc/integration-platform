# Edge & Hardware I/O Integrations (Raspberry Pi GPIO)

**Status:** Working example shipped (`src/Examples/WindMonitor`). Message-triggered
choreography (the "Composing with messages" section) is implemented — see `docs/message-triggers.md`.

A Serto runtime agent is just .NET in a container, and the agent image ships a `linux/arm64`
variant — so it runs on a 64-bit Raspberry Pi. That turns a cheap ARM box into a **networked I/O
board that runs arbitrary C#**: read a sensor, log a value on a schedule, raise an alarm when a
threshold trips — all managed centrally from the control plane like any other integration.

This page shows the smallest real example (reading a GPIO pin) and how it composes into larger
event-driven patterns.

> **Scope — monitoring, not safety.** Everything here runs on Linux + Docker + a garbage-collected
> runtime, dispatched on a polling cadence. That is fine for *monitoring* ("tell me when wind is
> high", "log the temperature"). It is **soft real-time at best** and must **not** be used as a
> safety instrumented system or a hard control loop where a missed or late reading causes harm.
> If lives or equipment depend on a deterministic response, use a dedicated controller/PLC and let
> Serto observe it, not drive it.

## What you need

1. **A 64-bit Raspberry Pi OS** (Pi 3/4/5 or Zero 2 W). The agent pulls its `linux/arm64` image
   automatically on a 64-bit host. A 32-bit install will not select the right image.
2. **The agent container must be granted the GPIO device.** A container cannot see the Pi's GPIO
   unless you map the device in:

   ```bash
   docker run \
     --device /dev/gpiochip0:/dev/gpiochip0 \
     -e Agent__Environment=production \
     -e 'Agent__Tags__0=rpi-gpio' \
     serto/runtime-agent:latest
   ```

   `System.Device.Gpio` uses the libgpiod character-device driver against `/dev/gpiochip0`. The
   single `--device` mapping is all you need — **do not use `--privileged`**. (If you later switch
   to the memory-mapped driver for speed, map `/dev/gpiomem` instead.)
3. **The integration declares the hardware it needs**, and the agent advertises that it has it, so
   the control plane only schedules the integration onto the wired Pi — see
   [Capability routing](#capability-routing) below.

## The integration

Add Microsoft's cross-platform GPIO package to your integration project:

```xml
<PackageReference Include="System.Device.Gpio" Version="3.2.0" />
```

Then read a pin from inside `RunAsync`. The full example lives in
`src/Examples/WindMonitor/HighWindMonitorIntegration.cs`; the core is just:

```csharp
[ScheduledIntegration("High Wind Monitor", "high-wind-monitor", "*/1 * * * *")]
[RequiresAgentCapabilities("rpi-gpio")]
public class HighWindMonitorIntegration : IIntegration
{
    private const int WindThresholdPin = 17; // BCM numbering; BCM 17 = physical pin 11

    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        using var controller = new GpioController();
        controller.OpenPin(WindThresholdPin, PinMode.InputPullDown);

        if (controller.Read(WindThresholdPin) == PinValue.High)
        {
            var db = context.SqlConnector("WIND_DB_CONNECTION_STRING");
            await db.ExecuteAsync(
                "INSERT INTO HighWindEvents (Id, ObservedAt) VALUES (@Id, @ObservedAt)",
                new { Id = context.Execution.ExecutionId, ObservedAt = context.Execution.ScheduledAt },
                ct);
        }
    }
}
```

The wiring assumed here is an anemometer feeding a comparator (or relay) that drives the pin HIGH
while wind speed is over a threshold, so the code only reads a clean digital level. **The Pi has no
native analog input** — to read an analog sensor value (a real wind *speed*, a temperature voltage)
you wire an ADC (e.g. an MCP3008 over SPI) and read it with the SPI APIs in the same
`System.Device.*` family.

### Lifecycle: this is polling, not interrupts

`RunAsync` is invoked per execution and is expected to return. The example opens the pin, reads,
and closes every minute. That is the right shape for "check a sensor on a cadence."

It is deliberately **not** a long-lived loop blocking on a hardware interrupt waiting for an edge.
True interrupt-driven reads need a persistent execution, which fights the scheduled timeout/retry
model. If you genuinely need edge-triggered hardware events, that is the same gap as a long-lived /
event-driven trigger type — tracked as a design spike, not available today.

## Capability routing

`[RequiresAgentCapabilities("rpi-gpio")]` is what keeps this integration from being scheduled onto
a cloud agent (or a different Pi) that has no anemometer wired to pin 17. You tag the Pi's agent
with `rpi-gpio` (`Agent__Tags__0=rpi-gpio`), and the control plane routes the work only there.

> The attribute exists in the SDK today; full routing enforcement and the "no eligible agent"
> visibility are rolling out per `docs/agent-capability-tags.md`. Declare the requirement now — it
> is the code-declared default an operator can override later.

## Composing with messages

The example above both *detects* high wind and *writes the record* in one integration. The more
powerful pattern your fleet enables is to **separate the two**: the sensor publishes a fact, and a
separate job reacts. The publisher does not know or care who is listening (choreography), which is
different from a predefined Workflow DAG where edges fire unconditionally (orchestration).

Intended shape once message triggers land:

```csharp
// Publisher — only emits when the condition is met:
await context.Messages.PublishAsync(new HighWindDetected(observedAt), ct);

// Subscriber — a separate integration, possibly on a different agent:
[MessageIntegration("High Wind Job", "high-wind-job", subject: "high-wind")]
public class HighWindJob : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var msg = context.PayloadAs<HighWindDetected>(); // raw body; context.Trigger has the metadata
        // Write to the database, page on-call, open a damper, ...
    }
}
```

This is a small feature, not "adding a message bus": the control plane already turns any trigger
event into a work item with payload, idempotency dedup, and parent/root execution lineage
(`TriggerWorkItemProducer`), and `TriggerSource.Queue` is already reserved as a trigger adapter
slot. The full design — the publish API shape, why the subject (not the .NET type) is the contract,
and DB-backed delivery before any broker — is written up in **`docs/message-triggers.md`**.

## Related

- `docs/agent-capability-tags.md` — routing work to agents wired to specific hardware.
- `docs/writing-integrations.md` — the integration model, triggers, and connectors.
- `docs/docker-images.md` — multi-arch images and how the agent is deployed.
