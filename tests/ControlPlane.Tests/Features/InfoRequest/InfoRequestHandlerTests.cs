using ControlPlane.Features.Alerts.Email;
using ControlPlane.Features.InfoRequest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ControlPlane.Tests.Features.InfoRequest;

public class InfoRequestHandlerTests
{
    private readonly IEmailSender _zepto = Substitute.For<IEmailSender>();
    private readonly IEmailSender _smtp = Substitute.For<IEmailSender>();

    public InfoRequestHandlerTests()
    {
        _zepto.Provider.Returns(EmailProvider.Zepto);
        _zepto.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _smtp.Provider.Returns(EmailProvider.Smtp);
    }

    private InfoRequestHandler Build(ZeptoOptions? zepto = null)
    {
        var options = Options.Create(new InfoRequestOptions { Recipient = "info@craytech-solutions.com" });
        return new InfoRequestHandler(
            [_smtp, _zepto],
            zepto ?? new ZeptoOptions { Token = "t", FromAddress = "noreply@craytech-solutions.com", FromName = "Serto" },
            options,
            NullLogger<InfoRequestHandler>.Instance);
    }

    [Fact]
    public async Task ValidForm_SendsEmailToRecipientViaZepto_AndReturnsSent()
    {
        var outcome = await Build().SubmitAsync(
            new InfoRequestForm("Ada", "ada@example.com", "Babbage Co", "Tell me more"), CancellationToken.None);

        Assert.Equal(InfoRequestStatus.Sent, outcome.Status);
        await _zepto.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m =>
                m.Recipients.Count == 1 && m.Recipients[0] == "info@craytech-solutions.com"
                && m.FromAddress == "noreply@craytech-solutions.com"
                && m.Subject.Contains("Ada")
                && m.TextBody.Contains("ada@example.com")
                && m.TextBody.Contains("Babbage Co")
                && m.TextBody.Contains("Tell me more")),
            Arg.Any<CancellationToken>());
        await _smtp.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "ada@example.com", "msg", "name")]
    [InlineData("Ada", "", "msg", "email")]
    [InlineData("Ada", "not-an-email", "msg", "email")]
    [InlineData("Ada", "ada@example.com", "", "message")]
    public async Task InvalidForm_ReturnsInvalid_WithoutSending(string name, string email, string message, string field)
    {
        var outcome = await Build().SubmitAsync(new InfoRequestForm(name, email, null, message), CancellationToken.None);

        Assert.Equal(InfoRequestStatus.Invalid, outcome.Status);
        Assert.Equal(field, outcome.Field);
        await _zepto.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailNotConfigured_ReturnsNotConfigured_WithoutSending()
    {
        var outcome = await Build(zepto: new ZeptoOptions()).SubmitAsync(
            new InfoRequestForm("Ada", "ada@example.com", null, "hi"), CancellationToken.None);

        Assert.Equal(InfoRequestStatus.NotConfigured, outcome.Status);
        await _zepto.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendThrows_ReturnsSendFailed()
    {
        _zepto.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var outcome = await Build().SubmitAsync(
            new InfoRequestForm("Ada", "ada@example.com", null, "hi"), CancellationToken.None);

        Assert.Equal(InfoRequestStatus.SendFailed, outcome.Status);
    }
}
