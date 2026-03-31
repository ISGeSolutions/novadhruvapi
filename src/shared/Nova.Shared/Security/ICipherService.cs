namespace Nova.Shared.Security;

/// <summary>
/// Provides encryption and decryption of sensitive configuration values.
/// </summary>
public interface ICipherService
{
    /// <summary>Encrypts a plain-text string.</summary>
    string Encrypt(string plainText);

    /// <summary>Decrypts a previously encrypted string.</summary>
    string Decrypt(string cipherText);
}
