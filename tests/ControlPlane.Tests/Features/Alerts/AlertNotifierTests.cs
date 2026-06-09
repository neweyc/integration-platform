using ControlPlane.Features.Alerts;
using ControlPlane.Features.Alerts.Email;
using ControlPlane.Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Alerts;

public class AlertNotifierTests
{
    private readonly IAlertSettingsReadRepository _repository = Substitute.For<IAlertSettingsReadRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IEmailSender _zepto = Substitute.For<IEmailSender>();
    private readonly IEmailSender _smtp = Substitute.For<IEmailSender>();
    private readonly IWebhookAlertSender _webhook = Substitute.For<IWebhookAlertSender>();
    private readonly ZeptoOptions _zeptoOptions = new() { Token = "tok", FromAddress = "platform@serto.io" };
    private readonly Guid _tenantId = Guid.NewGuid();

    public AlertNotifierTests()
    {
        _zepto.Provider.Returns(EmailProvider.Zepto);
        _smtp.Provider.Returns(EmailProvider.Smtp);
        _encryption.Decrypt(Arg.Any<string>()).Returns(c => $"decrypted:{c.Arg<string>()}");
    }

    private AlertNotifier CreateNotifier() =>
        new(_repository, _encryption, [_zepto, _smtp], _webhook, _zeptoOptions,
            Substitute.For<ILogger<AlertNotifier>>());

    private static FailedExecutionAlert Alert(Guid tenantId) =>
        new(tenantId, Guid.NewGuid(), "Sync Orders", "production", Guid.NewGuid(),
            ExecutionStatus.Failed, "boom", 1, null, null, DateTime.UtcNow);

    [Fact]
    public async Task NoConfiguredChannels_AttemptsNothing()
    {
        _repository.GetTenantSettingsAsync(_tenantId).Returns((TenantAlertSettings?)null);

        var outcome = await CreateNotifier().SendAsync(Alert(_tenantId));

        Assert.False(outcome.AnyAttempted);
        await _zepto.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _webhook.DidNotReceive().SendAsync(Arg.Any<ResolvedWebhookTarget>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailAndWebhookConfigured_SendsBothAndReportsSuccess()
    {
        _repository.GetTenantSettingsAsync(_tenantId).Returns(new TenantAlertSettings
        {
            TenantId = _tenantId,
            EmailEnabled = true,
            EmailRecipients = "ops@acme.com",
            WebhookEnabled = true,
            WebhookUrl = "https://hooks.example.com/x"
        });

        var outcome = await CreateNotifier().SendAsync(Alert(_tenantId));

        Assert.True(outcome.EmailSucceeded);
        Assert.True(outcome.WebhookSucceeded);
        // No tenant SMTP server → email goes through ZeptoMail, not the SMTP sender.
        await _zepto.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _smtp.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _webhook.Received(1).SendAsync(Arg.Any<ResolvedWebhookTarget>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TenantSmtpConfigured_SelectsSmtpSender()
    {
        _repository.GetTenantSettingsAsync(_tenantId).Returns(new TenantAlertSettings
        {
            TenantId = _tenantId,
            EmailEnabled = true,
            EmailRecipients = "ops@acme.com",
            SmtpHost = "smtp.acme.com",
            SmtpFromAddress = "alerts@acme.com"
        });

        var outcome = await CreateNotifier().SendAsync(Alert(_tenantId));

        Assert.True(outcome.EmailSucceeded);
        await _smtp.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _zepto.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailFailure_DoesNotPreventWebhook_AndIsReported()
    {
        _repository.GetTenantSettingsAsync(_tenantId).Returns(new TenantAlertSettings
        {
            TenantId = _tenantId,
            EmailEnabled = true,
            EmailRecipients = "ops@acme.com",
            WebhookEnabled = true,
            WebhookUrl = "https://hooks.example.com/x"
        });
        _zepto.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("smtp down")));

        var outcome = await CreateNotifier().SendAsync(Alert(_tenantId));

        Assert.True(outcome.EmailAttempted);
        Assert.False(outcome.EmailSucceeded);
        Assert.Equal("smtp down", outcome.EmailError);
        Assert.True(outcome.WebhookSucceeded);
        await _webhook.Received(1).SendAsync(Arg.Any<ResolvedWebhookTarget>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
