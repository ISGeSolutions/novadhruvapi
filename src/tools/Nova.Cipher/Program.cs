using System.Security.Cryptography;
using Nova.Shared.Security;

if (args.Length < 2 || args[0] is not ("encrypt" or "decrypt"))
{
    Console.Error.WriteLine("Nova Cipher — encrypt and decrypt config values");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  nova-cipher encrypt <plaintext>");
    Console.Error.WriteLine("  nova-cipher decrypt <ciphertext>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("The ENCRYPTION_KEY environment variable must be set.");
    return 1;
}

string command = args[0];
string value   = args[1];

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
