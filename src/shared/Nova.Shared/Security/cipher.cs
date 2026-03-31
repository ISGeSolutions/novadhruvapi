using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DhruvUtil.Security;

/// <summary>
/// Unified cipher utility for encrypting and decrypting connection strings and other sensitive data.
/// Uses environment variable based key generation for consistent encryption across projects.
/// </summary>
public static class Cipher
{
    private const string DefaultEnvironmentKeyVariable = "DB_ENCRYPTION_KEY";

    /// <summary>
    /// Gets or sets the environment variable name to use for the encryption key.
    /// Defaults to "DB_ENCRYPTION_KEY".
    /// </summary>
    public static string EnvironmentKeyVariable { get; set; } = DefaultEnvironmentKeyVariable;

    /// <summary>
    /// Encrypts a plain text string using AES encryption with a key derived from environment variable.
    /// </summary>
    /// <param name="plainText">The text to encrypt</param>
    /// <param name="environmentKeyVariable">Optional: Custom environment variable name. Uses default if not provided.</param>
    /// <returns>Base64 encoded encrypted string</returns>
    /// <exception cref="InvalidOperationException">Thrown when environment variable is not set</exception>
    /// <exception cref="ArgumentException">Thrown when plainText is null or empty</exception>
    public static string Encrypt(string plainText, string? environmentKeyVariable = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        byte[] keyBytes = GetKeyFromEnvironment(environmentKeyVariable);
        return EncryptWithKey(plainText, keyBytes);
    }

    /// <summary>
    /// Decrypts a Base64 encoded encrypted string using AES decryption with a key derived from environment variable.
    /// </summary>
    /// <param name="encryptedText">The Base64 encoded encrypted text</param>
    /// <param name="environmentKeyVariable">Optional: Custom environment variable name. Uses default if not provided.</param>
    /// <returns>Decrypted plain text string</returns>
    /// <exception cref="InvalidOperationException">Thrown when environment variable is not set</exception>
    /// <exception cref="ArgumentException">Thrown when encryptedText is null or empty</exception>
    /// <exception cref="CryptographicException">Thrown when decryption fails (wrong key or corrupted data)</exception>
    public static string Decrypt(string encryptedText, string? environmentKeyVariable = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(encryptedText);

        byte[] keyBytes = GetKeyFromEnvironment(environmentKeyVariable);
        return DecryptWithKey(encryptedText, keyBytes);
    }

    /// <summary>
    /// Validates that the environment variable is properly set and accessible.
    /// </summary>
    /// <param name="environmentKeyVariable">Optional: Custom environment variable name. Uses default if not provided.</param>
    /// <returns>True if the environment key is available and valid</returns>
    public static bool ValidateEnvironmentKey(string? environmentKeyVariable = null)
    {
        try
        {
            GetKeyFromEnvironment(environmentKeyVariable);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the first few characters of the generated key for debugging/verification purposes.
    /// </summary>
    /// <param name="environmentKeyVariable">Optional: Custom environment variable name. Uses default if not provided.</param>
    /// <returns>First 20 characters of the Base64 encoded key</returns>
    /// <exception cref="InvalidOperationException">Thrown when environment variable is not set</exception>
    public static string GetKeyPreview(string? environmentKeyVariable = null)
    {
        byte[] keyBytes = GetKeyFromEnvironment(environmentKeyVariable);
        string base64Key = Convert.ToBase64String(keyBytes);
        return $"{base64Key[..Math.Min(20, base64Key.Length)]}...";
    }

    #region Private Methods

    private static byte[] GetKeyFromEnvironment(string? environmentKeyVariable = null)
    {
        string keyVariableName = environmentKeyVariable ?? EnvironmentKeyVariable;
        string keyValue = Environment.GetEnvironmentVariable(keyVariableName) ?? string.Empty;

        if (string.IsNullOrEmpty(keyValue))
        {
            throw new InvalidOperationException(
                $"Environment variable '{keyVariableName}' is not set. " +
                "Please configure this variable with your encryption key.");
        }

        // Generate a consistent 32-byte key using SHA-256
        return SHA256.HashData(Encoding.UTF8.GetBytes(keyValue));
    }

    private static string EncryptWithKey(string plainText, byte[] keyBytes)
    {
      using Aes aes = Aes.Create();
      aes.Key = keyBytes;
      aes.GenerateIV(); // Generate a random IV for each encryption

      ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

      using var ms = new MemoryStream();
      // Write the IV to the beginning of the memory stream
      ms.Write(aes.IV, 0, aes.IV.Length);

      using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
      using (var sw = new StreamWriter(cs))
      {
        sw.Write(plainText);
      }

      return Convert.ToBase64String(ms.ToArray());
    }

    private static string DecryptWithKey(string encryptedText, byte[] keyBytes)
    {
        try
        {
            byte[] cipherBytes = Convert.FromBase64String(encryptedText);

            using Aes aes = Aes.Create();
            aes.Key = keyBytes;

            // Extract the IV from the beginning of the cipher text (first 16 bytes)
            byte[] iv = new byte[16];
            Array.Copy(cipherBytes, 0, iv, 0, iv.Length);
            aes.IV = iv;

            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            using var ms = new MemoryStream(cipherBytes, iv.Length, cipherBytes.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException(
                "Decryption failed. This usually indicates the wrong key was used or the encrypted data is corrupted.",
                ex);
        }
    }

    #endregion
}
