using System.Device.Gpio;
using Serto.Sdk;
using Microsoft.Extensions.Logging;

namespace Examples.WindMonitor;

/// <summary>
/// The message published when the wind crosses the threshold. The subject is the wire contract
/// between publisher and subscriber — declared explicitly here so it stays stable independent of
/// this type's name or namespace.
/// </summary>
[Message("high-wind")]
public record HighWindDetected(DateTime ObservedAt);

/// <summary>
/// Reads a digital GPIO pin on a Raspberry Pi every minute and, when the wind is over threshold,
/// PUBLISHES a "high-wind" message. The wiring assumed here is an anemometer feeding a comparator
/// (or a relay) that drives the pin HIGH while wind speed is above a set threshold — so this
/// integration only has to read a clean digital level, not sample an analog voltage.
///
/// This is the "edge agent as a networked I/O board" pattern: a Serto runtime agent running in
/// Docker on a Pi, wired to real hardware, executing ordinary C#. It deliberately does NOT know or
/// care who reacts to high wind — that is the subscriber's job (see HighWindJobIntegration).
///
/// REQUIREMENTS to run for real:
///   1. A 64-bit Raspberry Pi OS (the runtime agent image ships a linux/arm64 variant).
///   2. The agent container must be granted the GPIO device:
///        docker run --device /dev/gpiochip0:/dev/gpiochip0 ...
///      Without that mapping, GpioController cannot open the pin. See docs/edge-gpio-integrations.md.
///   3. The agent host advertises the "rpi-gpio" capability tag so the control plane routes this
///      integration to the Pi and nowhere else (see [RequiresAgentCapabilities] below).
///
/// On a developer machine with no GPIO, GpioController throws when it cannot find a driver — that
/// is expected. This example is meant to be deployed to a Pi, not run on a laptop.
/// </summary>
[ScheduledIntegration(
    "High Wind Monitor",
    "high-wind-monitor",
    "*/1 * * * *",
    Description = "Reads a wind-threshold GPIO pin on a Raspberry Pi and publishes a high-wind message.")]
[RequiresAgentCapabilities("rpi-gpio")]
public class HighWindMonitorIntegration : IIntegration
{
    // BCM pin numbering. BCM 17 is physical header pin 11.
    private const int WindThresholdPin = 17;

    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        // Open the pin fresh each run. This per-execution open/read/close shape fits the scheduled
        // (polling) model: the control plane invokes RunAsync on a cadence, we sample, we return.
        // It is deliberately NOT a long-lived interrupt loop — see the "real-time" note in the docs.
        using var controller = new GpioController();
        controller.OpenPin(WindThresholdPin, PinMode.InputPullDown);

        PinValue level = controller.Read(WindThresholdPin);
        bool overThreshold = level == PinValue.High;

        context.Logger.LogInformation(
            "Wind threshold pin {Pin} read as {Level} (overThreshold={Over}).",
            WindThresholdPin, level, overThreshold);

        if (!overThreshold)
            return;

        // Over threshold: publish the fact and return. Whoever subscribes to "high-wind" reacts.
        // This run stays ignorant of subscribers (choreography, not a hard-wired pipeline).
        var observedAt = context.Execution.ScheduledAt;
        await context.Messages.PublishAsync(new HighWindDetected(observedAt), ct);

        context.Logger.LogWarning("High wind detected at {ObservedAt:o} — published 'high-wind'.", observedAt);
    }
}
