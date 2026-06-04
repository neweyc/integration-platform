using System.Security.Cryptography;

namespace ControlPlane.Features.Invitations;

public static class InvitationTokenGenerator
{
    public static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
