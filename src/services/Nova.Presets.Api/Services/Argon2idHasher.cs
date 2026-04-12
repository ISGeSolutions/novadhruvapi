using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Nova.Presets.Api.Services;

/// <summary>
/// Argon2id password hashing and verification using Konscious.
/// Hash format: <c>argon2id:{memory_kb}:{iterations}:{parallelism}:{base64_salt}:{base64_hash}</c>
/// Used by the change-password flow to hash the new password and verify the current one.
/// </summary>
internal static class Argon2idHasher
{
    private const int SaltLength   = 16;
    private const int HashLength   = 32;
    private const int Iterations   = 3;
    private const int MemorySizeKb = 65536; // 64 MB
    private const int Parallelism  = 1;

    public static string Hash(string plaintext)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] hash = Compute(Encoding.UTF8.GetBytes(plaintext), salt);

        return $"argon2id:{MemorySizeKb}:{Iterations}:{Parallelism}" +
               $":{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string plaintext, string storedHash)
    {
        string[] parts = storedHash.Split(':');
        if (parts.Length < 6 || parts[0] != "argon2id") return false;

        if (!int.TryParse(parts[1], out int memory)   ||
            !int.TryParse(parts[2], out int iters)     ||
            !int.TryParse(parts[3], out int parallel))
            return false;

        byte[] salt         = Convert.FromBase64String(parts[4]);
        byte[] expectedHash = Convert.FromBase64String(parts[5]);

        byte[] actualHash = Compute(Encoding.UTF8.GetBytes(plaintext), salt, memory, iters, parallel);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] Compute(
        byte[] data,
        byte[] salt,
        int    memorySizeKb = MemorySizeKb,
        int    iterations   = Iterations,
        int    parallelism  = Parallelism)
    {
        using var argon2 = new Argon2id(data);
        argon2.Salt                = salt;
        argon2.DegreeOfParallelism = parallelism;
        argon2.Iterations          = iterations;
        argon2.MemorySize          = memorySizeKb;
        return argon2.GetBytes(HashLength);
    }
}
