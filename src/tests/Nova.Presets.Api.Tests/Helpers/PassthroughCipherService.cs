using Nova.Shared.Security;

namespace Nova.Presets.Api.Tests.Helpers;

/// <summary>
/// Test-only <see cref="ICipherService"/> that returns its input unchanged.
/// Registered in <see cref="TestHost"/> so the test host can start without the
/// ENCRYPTION_KEY environment variable. appsettings.json in this project supplies
/// all secrets as plaintext — no encryption or decryption is performed.
/// </summary>
internal sealed class PassthroughCipherService : ICipherService
{
    public string Encrypt(string plainText) => plainText;
    public string Decrypt(string cipherText) => cipherText;
}
