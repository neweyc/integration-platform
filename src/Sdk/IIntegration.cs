namespace IntegrationPlatform.Sdk;

/// <summary>
/// Implement this interface to define an integration.
/// The platform will instantiate and call RunAsync when the integration is due to execute.
/// </summary>
public interface IIntegration
{
    Task RunAsync(IIntegrationContext context, CancellationToken ct);
}
