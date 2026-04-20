using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Nova.Shared.Security;

if (args.Length < 2 || args[0] is not ("encrypt" or "decrypt" or "argon2" or "verify"))
{
    Console.Error.WriteLine("Nova Cipher — encrypt, decrypt and hash config values");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  nova-cipher encrypt <plaintext>");
    Console.Error.WriteLine("  nova-cipher decrypt <ciphertext>");
    Console.Error.WriteLine("  nova-cipher argon2  <plaintext>");
    Console.Error.WriteLine("  nova-cipher verify  <plaintext> <stored-hash>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("encrypt/decrypt require the ENCRYPTION_KEY environment variable.");
    Console.Error.WriteLine("argon2  — produces an Argon2id hash suitable for tenant_secrets.client_secret_hash.");
    Console.Error.WriteLine("verify  — checks whether <plaintext> matches a stored Argon2id hash. Exits 0 = match, 1 = no match.");
    return 1;
}

string command = args[0];
string value   = args[1];

if (command == "verify")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: nova-cipher verify <plaintext> <stored-hash>");
        return 1;
    }

    string storedHash = args[2];
    string[] parts    = storedHash.Split(':');

    if (parts.Length < 6 || parts[0] != "argon2id")
    {
        Console.Error.WriteLine("Error: stored-hash is not a valid argon2id hash string.");
        Console.Error.WriteLine("Expected format: argon2id:<memory>:<iterations>:<parallelism>:<base64-salt>:<base64-hash>");
        return 1;
    }

    if (!int.TryParse(parts[1], out int memory)    ||
        !int.TryParse(parts[2], out int iterations) ||
        !int.TryParse(parts[3], out int parallelism))
    {
        Console.Error.WriteLine("Error: could not parse argon2id parameters from hash string.");
        return 1;
    }

    byte[] salt         = Convert.FromBase64String(parts[4]);
    byte[] expectedHash = Convert.FromBase64String(parts[5]);
    byte[] data         = Encoding.UTF8.GetBytes(value);

    using var argon2 = new Argon2id(data);
    argon2.Salt                = salt;
    argon2.DegreeOfParallelism = parallelism;
    argon2.Iterations          = iterations;
    argon2.MemorySize          = memory;

    byte[] actualHash = argon2.GetBytes(expectedHash.Length);

    bool match = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    Console.WriteLine(match ? "MATCH — plaintext is correct." : "NO MATCH — plaintext does not match the stored hash.");
    return match ? 0 : 1;
}

if (command == "argon2")
{
    byte[] salt = RandomNumberGenerator.GetBytes(16);
    byte[] data = Encoding.UTF8.GetBytes(value);

    using var argon2 = new Argon2id(data);
    argon2.Salt                = salt;
    argon2.DegreeOfParallelism = 1;
    argon2.Iterations          = 3;
    argon2.MemorySize          = 65536; // 64 MB — matches Argon2idHasher in CommonUX.Api

    byte[] hash = argon2.GetBytes(32);

    Console.WriteLine($"argon2id:65536:3:1:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}");
    return 0;
}

try
{
    ICipherService cipher = new CipherService();
    string result = command == "encrypt"
        ? cipher.Encrypt(value)
        : cipher.Decrypt(value);

    Console.WriteLine(result);
    return 0;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
catch (CryptographicException ex)
{
    Console.Error.WriteLine($"Decryption failed: {ex.Message}");
    return 1;
}
