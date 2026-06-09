using ControlPlane.Features.Alerts;
using ControlPlane.Features.Alerts.Email;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Alerts;

public class AlertConfigResolverTests
{
    // Marks decrypted values so tests can assert the decrypt delegate was actually applied.
    private static string Decrypt(string value) => $"decrypted:{value}";

    private static readonly ZeptoDefaults Zepto = new("platform@serto.io", "Serto Alerts");

    private static TenantAlertSettings Tenant(Action<TenantAlertSettings>? configure = null)
    {
        var settings = new TenantAlertSettings { TenantId = Guid.NewGuid() };
        configure?.Invoke(settings);
        return settings;
    }

    [Fact]
    public void NoTenantSettings_ResolvesNothing()
    {
        var targets = AlertConfigResolver.Resolve(null, null, Decrypt, Zepto);

        Assert.False(targets.HasAny);
    }

    [Fact]
    public void IntegrationModeOff_SuppressesEverything()
    {
        var tenant = Tenant(t =>
        {
            t.EmailEnabled = true;
            t.EmailRecipients = "ops@acme.com";
            t.WebhookEnabled = true;
            t.WebhookUrl = "https://hooks.example.com/x";
        });
        var integration = new IntegrationAlertSettings { Mode = AlertMode.Off };

        var targets = AlertConfigResolver.Resolve(tenant, integration, Decrypt, Zepto);

        Assert.False(targets.HasAny);
    }

    [Fact]
    public void EmailEnabledWithSmtpServer_SelectsSmtpProviderAndDecryptsPassword()
    {
        var tenant = Tenant(t =>
        {
            t.EmailEnabled = true;
            t.EmailRecipients = "ops@acme.com, oncall@acme.com";
            t.SmtpHost = "smtp.acme.com";
            t.SmtpPort = 2525;
            t.SmtpUseStartTls = true;
            t.SmtpUsername = "mailer";
            t.SmtpEncryptedPassword = "enc-pw";
            t.SmtpFromAddress = "alerts@acme.com";
            t.SmtpFromName = "Acme Alerts";
        });

        var targets = AlertConfigResolver.Resolve(tenant, null, Decrypt, Zepto);

        Assert.NotNull(targets.Email);
        Assert.Equal(EmailProvider.Smtp, targets.Email!.Provider);
        Assert.Equal("alerts@acme.com", targets.Email.FromAddress);
        Assert.Equal(2, targets.Email.Recipients.Count);
        Assert.NotNull(targets.Email.Smtp);
        Assert.Equal("smtp.acme.com", targets.Email.Smtp!.Host);
        Assert.Equal(2525, targets.Email.Smtp.Port);
        Assert.Equal("decrypted:enc-pw", targets.Email.Smtp.Password);
    }

    [Fact]
    public void EmailEnabledWithoutSmtp_FallsBackToZepto()
    {
        var tenant = Tenant(t =>
        {
            t.EmailEnabled = true;
            t.EmailRecipients = "ops@acme.com";
        });

        var targets = AlertConfigResolver.Resolve(tenant, null, Decrypt, Zepto);

        Assert.NotNull(targets.Email);
        Assert.Equal(EmailProvider.Zepto, targets.Email!.Provider);
        Assert.Equal("platform@serto.io", targets.Email.FromAddress);
        Assert.Null(targets.Email.Smtp);
    }

    [Fact]
    public void EmailEnabledWithoutSmtpOrZepto_ResolvesNoEmail()
    {
        var tenant = Tenant(t =>
        {
            t.EmailEnabled = true;
            t.EmailRecipients = "ops@acme.com";
        });

        var targets = AlertConfigResolver.Resolve(tenant, null, Decrypt, zepto: null);

        Assert.Null(targets.Email);
    }

    [Fact]
    public void EmailEnabledWithoutRecipients_ResolvesNoEmail()
    {
        var tenant = Tenant(t =>
        {
            t.EmailEnabled = true;
            t.EmailRecipients = "   ";
        });

        var targets = AlertConfigResolver.Resolve(tenant, null, Decrypt, Zepto);

        Assert.Null(targets.Email);
    }

    [Fact]
    public void WebhookEnabled_ResolvesWebhookAndDecryptsSecret()
    {
        var tenant = Tenant(t =>
        {
            t.WebhookEnabled = true;
            t.WebhookUrl = "https://hooks.example.com/x";
            t.WebhookEncryptedSecret = "enc-secret";
        });

        var targets = AlertConfigResolver.Resolve(tenant, null, Decrypt, Zepto);

        Assert.NotNull(targets.Webhook);
        Assert.Equal("https://hooks.example.com/x", targets.Webhook!.Url);
        Assert.Equal("decrypted:enc-secret", targets.Webhook.Secret);
    }

    [Fact]
    public void CustomOverride_UsesIntegrationDestinationsButTenantSmtpServer()
    {
        var tenant = Tenant(t =>
        {
            // Tenant default points elsewhere; the override should win.
            t.EmailEnabled = true;
            t.EmailRecipients = "tenant-default@acme.com";
            t.SmtpHost = "smtp.acme.com";
            t.SmtpFromAddress = "alerts@acme.com";
        });
        var integration = new IntegrationAlertSettings
        {
            Mode = AlertMode.Custom,
            EmailEnabled = true,
            EmailRecipients = "team-specific@acme.com",
            WebhookEnabled = true,
            WebhookUrl = "https://hooks.example.com/team"
        };

        var targets = AlertConfigResolver.Resolve(tenant, integration, Decrypt, Zepto);

        Assert.NotNull(targets.Email);
        Assert.Equal(EmailProvider.Smtp, targets.Email!.Provider); // still the tenant SMTP server
        Assert.Single(targets.Email.Recipients);
        Assert.Equal("team-specific@acme.com", targets.Email.Recipients[0]);
        Assert.NotNull(targets.Webhook);
        Assert.Equal("https://hooks.example.com/team", targets.Webhook!.Url);
    }

    [Fact]
    public void CustomOverride_CanDisableEmailWhileTenantDefaultEnablesIt()
    {
        var tenant = Tenant(t =>
        {
            t.EmailEnabled = true;
            t.EmailRecipients = "tenant-default@acme.com";
            t.SmtpHost = "smtp.acme.com";
            t.SmtpFromAddress = "alerts@acme.com";
        });
        var integration = new IntegrationAlertSettings
        {
            Mode = AlertMode.Custom,
            EmailEnabled = false,
            WebhookEnabled = false
        };

        var targets = AlertConfigResolver.Resolve(tenant, integration, Decrypt, Zepto);

        Assert.False(targets.HasAny);
    }

    [Fact]
    public void CustomOverride_ResolvesWebhookEvenWithNoTenantDefaultsRow()
    {
        var integration = new IntegrationAlertSettings
        {
            Mode = AlertMode.Custom,
            WebhookEnabled = true,
            WebhookUrl = "https://hooks.example.com/team"
        };

        var targets = AlertConfigResolver.Resolve(tenant: null, integration, Decrypt, Zepto);

        Assert.NotNull(targets.Webhook);
        Assert.Equal("https://hooks.example.com/team", targets.Webhook!.Url);
    }

    [Fact]
    public void CustomOverride_ResolvesZeptoEmailWithNoTenantDefaultsRow()
    {
        var integration = new IntegrationAlertSettings
        {
            Mode = AlertMode.Custom,
            EmailEnabled = true,
            EmailRecipients = "team@acme.com"
        };

        var targets = AlertConfigResolver.Resolve(tenant: null, integration, Decrypt, Zepto);

        Assert.NotNull(targets.Email);
        Assert.Equal(EmailProvider.Zepto, targets.Email!.Provider);
    }

    [Fact]
    public void Inheriting_WithNoTenantDefaultsRow_ResolvesNothing()
    {
        var targets = AlertConfigResolver.Resolve(tenant: null, integration: null, Decrypt, Zepto);

        Assert.False(targets.HasAny);
    }

    [Theory]
    [InlineData("a@x.com, b@x.com;c@x.com", 3)]
    [InlineData("solo@x.com", 1)]
    [InlineData("  spaced@x.com  ", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseRecipients_SplitsOnCommonSeparators(string? raw, int expectedCount)
    {
        Assert.Equal(expectedCount, AlertConfigResolver.ParseRecipients(raw).Count);
    }
}
