using System.Security.Cryptography;

namespace Emberglass.Network;

/// <summary>
/// Provides SignaturesP256 key generation and signature helpers.
/// </summary>
public static class SignaturesP256
{
    public const int PrivateKeySize = 32;           // D
    public const int PublicKeySize = 64;            // X||Y
    public const int SignatureSize = 64;            // r||s (P1363)
    public const int CoordinateSize = PublicKeySize / 2; // X or Y

    /// <summary>
    /// Creates a new SignaturesP256 keypair.
    /// </summary>
    /// <remarks>
    /// The public key is returned as 64 bytes in uncompressed X||Y format (each coordinate is 32 bytes).
    /// The private key is the 32-byte D value. Store and transport keys in these raw byte formats.
    /// </remarks>
    /// <param name="publicKey">The generated public key bytes in uncompressed X||Y format (64 bytes).</param>
    /// <param name="privateKey">The generated private key bytes (32-byte D value).</param>
    public static void CreateKey(out byte[] publicKey, out byte[] privateKey)
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);

        privateKey = RequireLen(p.D, PrivateKeySize);
        publicKey = new byte[PublicKeySize];
        Buffer.BlockCopy(RequireLen(p.Q.X, CoordinateSize), 0, publicKey, 0, CoordinateSize);
        Buffer.BlockCopy(RequireLen(p.Q.Y, CoordinateSize), 0, publicKey, CoordinateSize, CoordinateSize);
    }

    /// <summary>
    /// Signs a payload with the provided SignaturesP256 private key.
    /// </summary>
    /// <remarks>
    /// The private key must be the 32-byte D value. Signatures are returned using IEEE P1363
    /// fixed-field concatenation (r||s, 64 bytes). Store and transport the signature in this format.
    /// </remarks>
    /// <param name="payload">Payload bytes to sign.</param>
    /// <param name="privateKey">Private key bytes (32-byte D value).</param>
    /// <param name="signature">Destination buffer for the signature (64-byte r||s).</param>
    public static void Sign(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> privateKey, Span<byte> signature)
    {
        if (signature.Length != SignatureSize)
            throw new ArgumentException($"Signature buffer must be {SignatureSize} bytes.", nameof(signature));
        if (privateKey.Length != PrivateKeySize)
            throw new ArgumentException($"Private key must be {PrivateKeySize} bytes.", nameof(privateKey));

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey.ToArray()
        });

        if (!ecdsa.TrySignData(payload, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation, out int written) ||
            written != SignatureSize)
        {
            throw new CryptographicException("Failed to sign payload.");
        }
    }

    /// <summary>
    /// Verifies a payload signature with the provided SignaturesP256 public key.
    /// </summary>
    /// <remarks>
    /// The public key must be 64 bytes in uncompressed X||Y format (each coordinate is 32 bytes).
    /// Signatures must use IEEE P1363 fixed-field concatenation (r||s, 64 bytes).
    /// </remarks>
    /// <param name="payload">Payload bytes to verify.</param>
    /// <param name="publicKey">Public key bytes in uncompressed X||Y format (64 bytes).</param>
    /// <param name="signature">Signature bytes in IEEE P1363 r||s format (64 bytes).</param>
    /// <returns>True when the signature is valid; otherwise, false. Returns false when the public key or signature length is invalid instead of throwing.</returns>
    public static bool Verify(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != PublicKeySize || signature.Length != SignatureSize)
            return false;

        byte[] x = [..publicKey[..CoordinateSize]];
        byte[] y = [..publicKey.Slice(CoordinateSize, CoordinateSize)];

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        });

        return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
    static byte[] RequireLen(byte[] v, int len)
        => (v is { Length: var l } && l == len) ? v : throw new CryptographicException($"Expected {len} bytes, got {v?.Length ?? 0}.");
}
