namespace ControlPlane.Infrastructure.Auditing;

/// <summary>
/// Wraps the dispatcher and writes an audit entry after any <see cref="IAuditableCommand"/>
/// completes successfully. Commands that fail are not audited (the action did not happen).
/// </summary>
public class AuditingDispatcher(Dispatcher inner, IAuditRecorder recorder) : IDispatcher
{
    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        var result = await inner.SendAsync(command, ct);

        if (command is IAuditableCommand auditable)
        {
            var descriptor = auditable.Describe(result);
            if (descriptor is not null)
                await recorder.RecordAsync(descriptor, ct);
        }

        return result;
    }
}
