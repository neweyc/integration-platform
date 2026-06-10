using ControlPlane.Infrastructure;

namespace ControlPlane.Features.Onboarding;

public record GetOnboardingStatusCommand(Guid TenantId) : ICommand<OnboardingStatusResult>;

public record OnboardingStep(string Key, string Title, string Description, bool Complete, string ActionPath);

public record OnboardingStatusResult(IReadOnlyList<OnboardingStep> Steps, bool Complete);

public class GetOnboardingStatusHandler(IOnboardingRepository repository)
    : ICommandHandler<GetOnboardingStatusCommand, OnboardingStatusResult>
{
    public async Task<OnboardingStatusResult> HandleAsync(GetOnboardingStatusCommand command, CancellationToken ct = default)
    {
        var progress = await repository.GetProgressAsync(command.TenantId, ct);

        var steps = new List<OnboardingStep>
        {
            new(
                "agent-token",
                "Connect a runtime agent",
                "Create an agent token so a runtime agent can pick up and run your integrations.",
                progress.HasAgentToken,
                "/agent-tokens"),
            new(
                "integration",
                "Deploy your first integration",
                "Author an integration and deploy it with the serto CLI, or create one in the UI.",
                progress.HasIntegration,
                "/integrations"),
            new(
                "execution",
                "See a successful run",
                "Trigger a run — scheduled, manual, or via webhook — and watch it complete.",
                progress.HasSuccessfulExecution,
                "/integrations"),
        };

        return new OnboardingStatusResult(steps, steps.All(s => s.Complete));
    }
}
