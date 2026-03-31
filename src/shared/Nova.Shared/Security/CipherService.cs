using DhruvUtil.Security;

namespace Nova.Shared.Security;

/// <summary>
/// Thin wrapper around <see cref="Cipher"/> that validates <c>ENCRYPTION_KEY</c>
/// is present at construction time and delegates all cryptographic work to cipher.cs.
/// </summary>
public sealed class CipherService : ICipherService
{
    private const string EncryptionKeyVariable = "ENCRYPTION_KEY";

    /// <summary>
    /// Initialises a new instance of <see cref="CipherService"/>.
    /// Throws if <c>ENCRYPTION_KEY</c> environment variable is not set.
    /// </summary>
    public CipherService()
    {
        if (!Cipher.ValidateEnvironmentKey(EncryptionKeyVariable))
            throw new InvalidOperationException(
                "ENCRYPTION_KEY environment variable is not set. " +
                "Set it before starting the application.");
    }

    /// <inheritdoc />
    public string Encrypt(string plainText) =>
        Cipher.Encrypt(plainText, EncryptionKeyVariable);

    /// <inheritdoc />
    public string Decrypt(string cipherText) =>
        Cipher.Decrypt(cipherText, EncryptionKeyVariable);
}
