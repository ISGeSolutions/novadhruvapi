# Nova.Cipher — Usage Guide

A console utility for encrypting and decrypting configuration values used in Nova `appsettings.json` files (connection strings, JWT secrets, passwords).

Uses the same AES-256 key derivation as the Nova services — set `ENCRYPTION_KEY` once and all encrypted values are interoperable.

## Prerequisites

The `ENCRYPTION_KEY` environment variable must match the value used by the running Nova services.

```bash
export ENCRYPTION_KEY=your-secret-passphrase
```

## Encrypt a value

```bash
dotnet run --project src/tools/Nova.Cipher -- encrypt "Server=localhost;Database=nova_dev;User Id=sa;Password=p@ss"
```

Output (Base64, paste directly into `appsettings.json`):

```
AY6QZ+7Fy5kM12c2PkKOziTzVGzNTpYi+z9tdWKC3N+5...==
```

## Decrypt a value

```bash
dotnet run --project src/tools/Nova.Cipher -- decrypt "AY6QZ+7Fy5kM12c2PkKOziTzVGzNTpYi+z9tdWKC3N+5...=="
```

Output:

```
Server=localhost;Database=nova_dev;User Id=sa;Password=p@ss
```

## Round-trip verification

```bash
export ENCRYPTION_KEY=test-key

ENCRYPTED=$(dotnet run --project src/tools/Nova.Cipher -- encrypt "hello world")
dotnet run --project src/tools/Nova.Cipher -- decrypt "$ENCRYPTED"
# → hello world
```

## Notes

- Each `encrypt` call produces a different ciphertext (random IV per call) — this is expected and correct.
- Wrong key on decrypt produces: `Decryption failed: Decryption failed. This usually indicates the wrong key was used or the encrypted data is corrupted.`
- Exit code `0` on success, `1` on any error.
