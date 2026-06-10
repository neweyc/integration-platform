using ControlPlane.Features.Auth;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.Auth;

public class PasswordResetHandlerTests
{
    private readonly IPasswordResetRepository _repository = Substitute.For<IPasswordResetRepository>();
    private readonly IPasswordResetNotifier _notifier = Substitute.For<IPasswordResetNotifier>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();

    [Fact]
    public async Task ForgotPassword_KnownEmail_StoresHashedTokenAndSendsLink()
    {
        var user = new User { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Email = "user@acme.com" };
        _repository.GetUserByEmailAsync("user@acme.com").Returns(user);

        var handler = new ForgotPasswordHandler(_repository, _notifier);
        var result = await handler.HandleAsync(new ForgotPasswordCommand("User@Acme.com"));

        Assert.True(result);
        // A token is stored for the user, and only its hash (never the plaintext) is persisted.
        await _repository.Received(1).AddAsync(
            Arg.Is<PasswordResetToken>(t => t.UserId == user.Id && t.TokenHash.Length == 64 && t.ExpiresAt > DateTime.UtcNow),
            Arg.Any<CancellationToken>());
        await _notifier.Received(1).SendResetLinkAsync(user.Email, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReportsSuccessButDoesNothing()
    {
        _repository.GetUserByEmailAsync(Arg.Any<string>()).Returns((User?)null);

        var handler = new ForgotPasswordHandler(_repository, _notifier);
        var result = await handler.HandleAsync(new ForgotPasswordCommand("nobody@acme.com"));

        Assert.True(result);
        await _repository.DidNotReceive().AddAsync(Arg.Any<PasswordResetToken>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().SendResetLinkAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_ValidToken_SetsNewPasswordAndRevokesSessions()
    {
        const string plaintext = "reset-token-123";
        var user = new User { Id = Guid.NewGuid(), Email = "user@acme.com", PasswordHash = "old" };
        var token = new PasswordResetToken
        {
            UserId = user.Id,
            User = user,
            TokenHash = PasswordResetTokens.Hash(plaintext),
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        _repository.FindUnusedByHashAsync(PasswordResetTokens.Hash(plaintext)).Returns(token);

        var handler = new ResetPasswordHandler(_repository, _refreshTokens);
        var result = await handler.HandleAsync(new ResetPasswordCommand(plaintext, "brand-new-pass"));

        Assert.True(result);
        await _repository.Received(1).ConsumeAndSetPasswordAsync(
            token,
            Arg.Is<string>(hash => BCrypt.Net.BCrypt.Verify("brand-new-pass", hash)),
            Arg.Any<CancellationToken>());
        await _refreshTokens.Received(1).RevokeAllForUserAsync(user.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_Throws()
    {
        const string plaintext = "expired-token";
        var token = new PasswordResetToken
        {
            User = new User { Id = Guid.NewGuid() },
            TokenHash = PasswordResetTokens.Hash(plaintext),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        _repository.FindUnusedByHashAsync(Arg.Any<string>()).Returns(token);

        var handler = new ResetPasswordHandler(_repository, _refreshTokens);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new ResetPasswordCommand(plaintext, "brand-new-pass")));
        Assert.Equal("This password reset link is invalid or has expired.", ex.Message);
        await _repository.DidNotReceive().ConsumeAndSetPasswordAsync(Arg.Any<PasswordResetToken>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_UnknownToken_Throws()
    {
        _repository.FindUnusedByHashAsync(Arg.Any<string>()).Returns((PasswordResetToken?)null);

        var handler = new ResetPasswordHandler(_repository, _refreshTokens);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new ResetPasswordCommand("whatever", "brand-new-pass")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public async Task ResetPassword_WeakPassword_Throws(string password)
    {
        var handler = new ResetPasswordHandler(_repository, _refreshTokens);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(new ResetPasswordCommand("some-token", password)));
    }
}
