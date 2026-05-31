using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Infrastructure;

// Encrypts and decrypts secret values using AES-256.
// Each encrypted value includes a random IV so the same plaintext
// produces a different ciphertext every time it is encrypted.
public class AesEncryptionService(IConfiguration configuration) : IEncryptionService
{
    private const int IvLengthBytes = 16;

    public string Encrypt(string plaintext)
    {
        var key = DeriveEncryptionKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV(); // Random IV per encryption

        using var encryptor = aes.CreateEncryptor();
        var ciphertextBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Store IV + ciphertext together so we can decrypt later without storing IV separately
        var ivPlusCiphertext = new byte[aes.IV.Length + ciphertextBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, ivPlusCiphertext, 0, aes.IV.Length);
        Buffer.BlockCopy(ciphertextBytes, 0, ivPlusCiphertext, aes.IV.Length, ciphertextBytes.Length);

        return Convert.ToBase64String(ivPlusCiphertext);
    }

    public string Decrypt(string ciphertext)
    {
        var key = DeriveEncryptionKey();
        var ivPlusCiphertext = Convert.FromBase64String(ciphertext);

        // Split the stored bytes back into IV and ciphertext
        var iv = ivPlusCiphertext[..IvLengthBytes];
        var encryptedBytes = ivPlusCiphertext[IvLengthBytes..];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    // Derives a 32-byte AES key from the configured master key using PBKDF2.
    // This ensures the key is always the right length regardless of what the user configured.
    private byte[] DeriveEncryptionKey()
    {
        var masterKey = configuration["Encryption:MasterKey"]
            ?? throw new InvalidOperationException("Encryption:MasterKey is not configured.");

        var salt = Encoding.UTF8.GetBytes("integration-platform-v1");

        return Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(masterKey),
            salt: salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }
}
