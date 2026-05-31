using System.Security.Cryptography;
using System.Text;
using Shared.Domain;

namespace ControlPlane.Features.AgentTokens;

public interface IAgentTokenService
{
    // Generates a new plaintext token in the format agt_<base64url>
    string Generate();

    // Returns the SHA-256 hex digest of a plaintext token
    string Hash(string plaintext);
}

public interface IAgentTokenLookupRepository
{
    Task<AgentToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);
}

public class AgentTokenService : IAgentTokenService
{
    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var encoded = Base64UrlEncode(bytes);
        return $"agt_{encoded}";
    }

    public string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexStringLower(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
