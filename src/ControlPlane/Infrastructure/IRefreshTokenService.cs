using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Infrastructure;

// Generates and hashes refresh tokens. Mirrors IUserTokenService: a high-entropy random value is
// the secret handed to the client, and only its SHA-256 hash is ever persisted.
public interface IRefreshTokenService
{
    string Generate();
    string Hash(string token);
}

public class RefreshTokenService : IRefreshTokenService
{
    public string Generate()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "rt_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
