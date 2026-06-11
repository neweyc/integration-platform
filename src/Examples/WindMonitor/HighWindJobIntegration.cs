using Serto.Sdk;
using Serto.Connectors.Sql;
using Microsoft.Extensions.Logging;

namespace Examples.WindMonitor;

/// <summary>
/// Reacts to a published "high-wind" message by recording it to the database. This is the other half
/// of the choreography: it subscribes to the subject and owns what happens next, while the publisher
/// (HighWindMonitorIntegration) only raises the fact. It can run on a different agent than the Pi —
/// it needs no GPIO, just database access — which is the point of decoupling detection from reaction.
///
/// The message body arrives on context.Payload (deserialize with PayloadAs); the subject, message id,
/// publish time, and the publishing execution (for lineage) arrive on context.Trigger as a
/// MessageTrigger. To page on-call, open a damper, or call an API instead, swap the body of RunAsync.
/// </summary>
[MessageIntegration("High Wind Job", "high-wind-job", subject: "high-wind")]
public class HighWindJobIntegration : IIntegration
{
    public async Task RunAsync(IIntegrationContext context, CancellationToken ct)
    {
        var detected = context.PayloadAs<HighWindDetected>();
        if (detected is null)
        {
            context.Logger.LogWarning("High wind job ran with no message body — nothing to record.");
            return;
        }

        if (context.Trigger is MessageTrigger trigger)
            context.Logger.LogInformation(
                "Reacting to '{Subject}' (message {MessageId}) published by execution {Source}.",
                trigger.Subject, trigger.MessageId, trigger.SourceExecutionId);

        var db = context.SqlConnector("WIND_DB_CONNECTION_STRING");
        await db.ExecuteAsync(
            """
            INSERT INTO HighWindEvents (Id, ObservedAt, ExecutionId)
            VALUES (@Id, @ObservedAt, @ExecutionId)
            """,
            new
            {
                Id = context.Execution.ExecutionId,
                detected.ObservedAt,
                ExecutionId = context.Execution.ExecutionId
            },
            ct);

        context.Logger.LogWarning("High wind recorded at {ObservedAt:o}.", detected.ObservedAt);
    }
}
