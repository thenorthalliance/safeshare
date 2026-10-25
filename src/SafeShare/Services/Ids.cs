using System.Security.Cryptography;

namespace SafeShare.Services;

public static class Ids
{
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Generates a random id, retrying with a longer one on the (unlikely) collision.</summary>
    public static async Task<string> NewUniqueAsync(Func<string, Task<bool>> exists)
    {
        for (var attempt = 0; ; attempt++)
        {
            var id = RandomNumberGenerator.GetString(Alphabet, attempt < 5 ? 10 : 14);
            if (!await exists(id))
                return id;
        }
    }

    /// <summary>Constant-time check that SHA-256(<paramref name="token"/>) equals <paramref name="expectedHash"/>.</summary>
    public static bool HashMatches(byte[] token, byte[] expectedHash) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(token), expectedHash);
}
