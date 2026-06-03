using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Infrastructure;

public interface IUserTokenService
{
    string Generate();
    string Hash(string token);
}

public class UserTokenService : IUserTokenService
{
    public string Generate()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "pat_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
