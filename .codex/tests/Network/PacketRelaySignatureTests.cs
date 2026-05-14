using System.Reflection;
using System.Security.Cryptography;
using Emberglass.API.Shared;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for handshake signature computation and verification in packet relay.
/// </summary>
[Collection("Assembly setup")]
public sealed class PacketRelaySignatureTests
{
    static readonly byte[] DeterministicPublicKey;
    static readonly byte[] DeterministicPrivateKey;

    static PacketRelaySignatureTests()
    {
        SignaturesP256.CreateKey(out DeterministicPublicKey, out DeterministicPrivateKey);
    }

    /// <summary>
    /// Ensures server hello signatures are produced and verified with the configured keys.
    /// </summary>
    [Fact]
    public void ComputeServerHelloSignature_VerifiesWithMatchingPayload()
    {
        Type PacketRelayType = GetPacketRelayType();
        using SignatureKeyScope Scope = new(PacketRelayType, DeterministicPrivateKey, DeterministicPublicKey);

        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x1A);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x5F);

        byte[] Signature = GetComputeServerHelloSignature(PacketRelayType)(ServerPublicKey, Nonce, Registry.Const.PROTOCOL_VERSION);

        Assert.Equal(Registry.Const.HANDSHAKE_SIGNATURE_BYTES, Signature.Length);
        Assert.True(GetVerifyServerHelloSignature(PacketRelayType)(ServerPublicKey, Nonce, Signature));
    }

    /// <summary>
    /// Ensures server hello signature verification fails when payload or signature is modified.
    /// </summary>
    [Fact]
    public void VerifyServerHelloSignature_FailsForModifiedPayloadOrSignature()
    {
        Type PacketRelayType = GetPacketRelayType();
        using SignatureKeyScope Scope = new(PacketRelayType, DeterministicPrivateKey, DeterministicPublicKey);

        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x10);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x20);

        byte[] Signature = GetComputeServerHelloSignature(PacketRelayType)(ServerPublicKey, Nonce, Registry.Const.PROTOCOL_VERSION);

        byte[] ModifiedNonce = Nonce.ToArray();
        ModifiedNonce[^1] ^= 0xFF;

        byte[] ModifiedSignature = Signature.ToArray();
        ModifiedSignature[^1] ^= 0xFF;

        Assert.False(GetVerifyServerHelloSignature(PacketRelayType)(ServerPublicKey, ModifiedNonce, Signature));
        Assert.False(GetVerifyServerHelloSignature(PacketRelayType)(ServerPublicKey, Nonce, ModifiedSignature));
    }

    /// <summary>
    /// Ensures handshake acknowledgement signatures verify and fail when data is modified.
    /// </summary>
    [Fact]
    public void VerifyHandshakeSignature_FailsForModifiedPayloadOrSignature()
    {
        Type PacketRelayType = GetPacketRelayType();
        using SignatureKeyScope Scope = new(PacketRelayType, DeterministicPrivateKey, DeterministicPublicKey);

        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x2A);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x9C);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x11);

        byte[] Signature = GetComputeHandshakeSignature(PacketRelayType)(ServerPublicKey, ClientPublicKey, Nonce, Registry.Const.PROTOCOL_VERSION);

        Assert.True(GetVerifyHandshakeSignature(PacketRelayType)(ServerPublicKey, ClientPublicKey, Nonce, Signature));

        byte[] ModifiedClientKey = ClientPublicKey.ToArray();
        ModifiedClientKey[^1] ^= 0x01;

        byte[] ModifiedSignature = Signature.ToArray();
        ModifiedSignature[^1] ^= 0xFF;

        Assert.False(GetVerifyHandshakeSignature(PacketRelayType)(ServerPublicKey, ModifiedClientKey, Nonce, Signature));
        Assert.False(GetVerifyHandshakeSignature(PacketRelayType)(ServerPublicKey, ClientPublicKey, Nonce, ModifiedSignature));
    }

    /// <summary>
    /// Ensures verification is disabled when the server signature public key is missing.
    /// </summary>
    [Fact]
    public void Verification_DisabledWhenPublicKeyMissing()
    {
        Type PacketRelayType = GetPacketRelayType();
        using SignatureKeyScope Scope = new(PacketRelayType, DeterministicPrivateKey, DeterministicPublicKey);

        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x44);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x12);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x77);

        byte[] ServerHelloSignature = GetComputeServerHelloSignature(PacketRelayType)(ServerPublicKey, Nonce, Registry.Const.PROTOCOL_VERSION);
        byte[] HandshakeSignature = GetComputeHandshakeSignature(PacketRelayType)(ServerPublicKey, ClientPublicKey, Nonce, Registry.Const.PROTOCOL_VERSION);

        Scope.SetPublicKey(Array.Empty<byte>());

        Assert.False(GetVerifyServerHelloSignature(PacketRelayType)(ServerPublicKey, Nonce, ServerHelloSignature));
        Assert.False(GetVerifyHandshakeSignature(PacketRelayType)(ServerPublicKey, ClientPublicKey, Nonce, HandshakeSignature));
    }

    /// <summary>
    /// Ensures invalid signatures are rejected when resolving the handshake protocol.
    /// </summary>
    [Fact]
    public void TryResolveHandshakeProtocol_RejectsInvalidSignature()
    {
        Type PacketRelayType = GetPacketRelayType();
        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x21);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x3A);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x4B);
        byte[] InvalidSignature = CreateSequence(Registry.Const.HANDSHAKE_SIGNATURE_BYTES, 0x90);

        bool Result = GetTryResolveHandshakeProtocol(PacketRelayType)(
            ServerPublicKey,
            ClientPublicKey,
            Nonce,
            InvalidSignature,
            out int ProtocolVersion);

        Assert.False(Result);
        Assert.Equal(0, ProtocolVersion);
    }

    /// <summary>
    /// Ensures empty signatures select the authenticated protocol when keys are configured.
    /// </summary>
    [Fact]
    public void TryResolveHandshakeProtocol_SelectsAuthenticatedProtocolWhenKeysPresent()
    {
        Type PacketRelayType = GetPacketRelayType();
        using SignatureKeyScope Scope = new(PacketRelayType, DeterministicPrivateKey, DeterministicPublicKey);

        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x11);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x22);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x33);
        byte[] EmptySignature = new byte[Registry.Const.HANDSHAKE_SIGNATURE_BYTES];

        bool Result = GetTryResolveHandshakeProtocol(PacketRelayType)(
            ServerPublicKey,
            ClientPublicKey,
            Nonce,
            EmptySignature,
            out int ProtocolVersion);

        Assert.True(Result);
        Assert.Equal(Registry.Const.PROTOCOL_VERSION, ProtocolVersion);
    }

    /// <summary>
    /// Ensures missing server keys cause authenticated handshake resolution to fail.
    /// </summary>
    [Fact]
    public void TryResolveHandshakeProtocol_RejectsWhenServerKeysMissing()
    {
        Type PacketRelayType = GetPacketRelayType();
        using SignatureKeyScope Scope = new(PacketRelayType, Array.Empty<byte>(), Array.Empty<byte>());

        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x41);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x52);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x63);
        byte[] EmptySignature = new byte[Registry.Const.HANDSHAKE_SIGNATURE_BYTES];

        bool Result = GetTryResolveHandshakeProtocol(PacketRelayType)(
            ServerPublicKey,
            ClientPublicKey,
            Nonce,
            EmptySignature,
            out int ProtocolVersion);

        Assert.False(Result);
        Assert.Equal(0, ProtocolVersion);
    }

    /// <summary>
    /// Ensures the legacy handshake MAC selects the legacy protocol when allowed.
    /// </summary>
    [Fact]
    public void TryResolveHandshakeProtocol_SelectsLegacyProtocolWhenLegacyMacMatches()
    {
        Type PacketRelayType = GetPacketRelayType();
        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x14);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x28);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x3C);

        byte[] Mac = GetComputeLegacyHandshakeMac(PacketRelayType)(ServerPublicKey, ClientPublicKey, Nonce);
        byte[] SignatureField = BuildLegacySignatureField(Mac);

        bool Result = GetTryResolveHandshakeProtocol(PacketRelayType)(
            ServerPublicKey,
            ClientPublicKey,
            Nonce,
            SignatureField,
            out int ProtocolVersion);

        Assert.True(Result);
        Assert.Equal(Registry.Const.LEGACY_PROTOCOL_VERSION, ProtocolVersion);
    }

    /// <summary>
    /// Ensures legacy MAC verification enforces padding rules for the signature field.
    /// </summary>
    [Fact]
    public void VerifyLegacyHandshakeMac_RejectsNonZeroPadding()
    {
        Type PacketRelayType = GetPacketRelayType();
        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x05);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x15);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x25);

        byte[] Mac = GetComputeLegacyHandshakeMac(PacketRelayType)(ServerPublicKey, ClientPublicKey, Nonce);
        byte[] ValidSignatureField = BuildLegacySignatureField(Mac);
        byte[] InvalidSignatureField = ValidSignatureField.ToArray();
        InvalidSignatureField[^1] = 0x01;

        VerifyLegacyHandshakeMacDelegate VerifyLegacyHandshakeMac = GetVerifyLegacyHandshakeMac(PacketRelayType);

        Assert.True(VerifyLegacyHandshakeMac(ValidSignatureField, Mac));
        Assert.False(VerifyLegacyHandshakeMac(InvalidSignatureField, Mac));
        Assert.False(VerifyLegacyHandshakeMac(Mac[..^1], Mac));
    }

    /// <summary>
    /// Ensures imported handshake public keys use the decoded byte count rather than fixed packet padding.
    /// </summary>
    [Fact]
    public void ExtractImportedPublicKeyBytes_RemovesFixedFieldPadding()
    {
        Type PacketRelayType = GetPacketRelayType();
        ExtractImportedPublicKeyBytesDelegate ExtractImportedPublicKeyBytes = GetExtractImportedPublicKeyBytes(PacketRelayType);

        using ECDiffieHellman Ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] PublicKey = Ecdh.ExportSubjectPublicKeyInfo();
        byte[] PaddedField = new byte[Registry.Const.EXCHANGE_LENGTH];
        PublicKey.CopyTo(PaddedField, 0);

        byte[] ExtractedPublicKey = ExtractImportedPublicKeyBytes(PaddedField);

        Assert.Equal(PublicKey, ExtractedPublicKey);
        Assert.True(ExtractedPublicKey.Length < PaddedField.Length);
    }

    /// <summary>
    /// Resolves the packet relay type from the Emberglass assembly.
    /// </summary>
    /// <returns>The packet relay type.</returns>
    static Type GetPacketRelayType()
    {
        Assembly EmberglassAssembly = typeof(VBehaviour).Assembly;
        Type? PacketRelayType = EmberglassAssembly.GetType("Emberglass.Network.PacketRelay");
        return PacketRelayType ?? throw new InvalidOperationException("PacketRelay type not found.");
    }

    /// <summary>
    /// Creates a deterministic sequence of bytes.
    /// </summary>
    /// <param name="length">Number of bytes to generate.</param>
    /// <param name="start">Starting byte value.</param>
    /// <returns>Deterministic byte sequence.</returns>
    static byte[] CreateSequence(int length, byte start)
    {
        byte[] Buffer = new byte[length];
        for (int i = 0; i < Buffer.Length; i++)
        {
            Buffer[i] = (byte)(start + i);
        }

        return Buffer;
    }

    /// <summary>
    /// Retrieves a delegate for computing server hello signatures.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for signature computation.</returns>
    static ComputeServerHelloSignatureDelegate GetComputeServerHelloSignature(Type packetRelayType)
        => CreateDelegate<ComputeServerHelloSignatureDelegate>(packetRelayType, "ComputeServerHelloSignature");

    /// <summary>
    /// Retrieves a delegate for verifying server hello signatures.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for signature verification.</returns>
    static VerifyServerHelloSignatureDelegate GetVerifyServerHelloSignature(Type packetRelayType)
        => CreateDelegate<VerifyServerHelloSignatureDelegate>(packetRelayType, "VerifyServerHelloSignature");

    /// <summary>
    /// Retrieves a delegate for computing handshake signatures.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for handshake signature computation.</returns>
    static ComputeHandshakeSignatureDelegate GetComputeHandshakeSignature(Type packetRelayType)
        => CreateDelegate<ComputeHandshakeSignatureDelegate>(packetRelayType, "ComputeHandshakeSignature");

    /// <summary>
    /// Retrieves a delegate for verifying handshake signatures.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for handshake signature verification.</returns>
    static VerifyHandshakeSignatureDelegate GetVerifyHandshakeSignature(Type packetRelayType)
        => CreateDelegate<VerifyHandshakeSignatureDelegate>(packetRelayType, "VerifyHandshakeSignature");

    /// <summary>
    /// Retrieves a delegate for computing legacy handshake MACs.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for MAC computation.</returns>
    static ComputeLegacyHandshakeMacDelegate GetComputeLegacyHandshakeMac(Type packetRelayType)
        => CreateDelegate<ComputeLegacyHandshakeMacDelegate>(packetRelayType, "ComputeLegacyHandshakeMac");

    /// <summary>
    /// Retrieves a delegate for resolving handshake protocol versions.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for protocol resolution.</returns>
    static TryResolveHandshakeProtocolDelegate GetTryResolveHandshakeProtocol(Type packetRelayType)
        => CreateDelegate<TryResolveHandshakeProtocolDelegate>(packetRelayType, "TryResolveHandshakeProtocol");

    /// <summary>
    /// Retrieves a delegate for verifying legacy handshake MACs.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for MAC verification.</returns>
    static VerifyLegacyHandshakeMacDelegate GetVerifyLegacyHandshakeMac(Type packetRelayType)
        => CreateDelegate<VerifyLegacyHandshakeMacDelegate>(packetRelayType, "VerifyLegacyHandshakeMac");

    /// <summary>
    /// Retrieves a delegate for extracting canonical public key bytes from a fixed packet field.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for public key extraction.</returns>
    static ExtractImportedPublicKeyBytesDelegate GetExtractImportedPublicKeyBytes(Type packetRelayType)
        => CreateDelegate<ExtractImportedPublicKeyBytesDelegate>(packetRelayType, "ExtractImportedPublicKeyBytes");

    /// <summary>
    /// Builds a fixed-length signature field for legacy handshake MACs.
    /// </summary>
    /// <param name="mac">MAC bytes to embed.</param>
    /// <returns>Signature field with zero padding.</returns>
    static byte[] BuildLegacySignatureField(ReadOnlySpan<byte> mac)
    {
        byte[] SignatureField = new byte[Registry.Const.HANDSHAKE_SIGNATURE_BYTES];
        mac.CopyTo(SignatureField);
        return SignatureField;
    }

    /// <summary>
    /// Creates a delegate for a private static method.
    /// </summary>
    /// <typeparam name="TDelegate">Delegate type.</typeparam>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <param name="methodName">Method name to locate.</param>
    /// <returns>Delegate instance.</returns>
    static TDelegate CreateDelegate<TDelegate>(Type packetRelayType, string methodName)
        where TDelegate : Delegate
    {
        MethodInfo Method = packetRelayType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return (TDelegate)Method.CreateDelegate(typeof(TDelegate));
    }

    /// <summary>
    /// Delegate for server hello signature computation.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Signature bytes.</returns>
    delegate byte[] ComputeServerHelloSignatureDelegate(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> nonce, int protocolVersion);

    /// <summary>
    /// Delegate for server hello signature verification.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="signature">Signature bytes.</param>
    /// <returns>True when signature is valid.</returns>
    delegate bool VerifyServerHelloSignatureDelegate(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature);

    /// <summary>
    /// Delegate for handshake signature computation.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Signature bytes.</returns>
    delegate byte[] ComputeHandshakeSignatureDelegate(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce, int protocolVersion);

    /// <summary>
    /// Delegate for handshake signature verification.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="signature">Signature bytes.</param>
    /// <returns>True when signature is valid.</returns>
    delegate bool VerifyHandshakeSignatureDelegate(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature);

    /// <summary>
    /// Delegate for legacy handshake MAC computation.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <returns>Legacy MAC bytes.</returns>
    delegate byte[] ComputeLegacyHandshakeMacDelegate(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce);

    /// <summary>
    /// Delegate for resolving handshake protocol versions.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="signature">Signature bytes.</param>
    /// <param name="protocolVersion">Resolved protocol version.</param>
    /// <returns>True when a protocol version is resolved.</returns>
    delegate bool TryResolveHandshakeProtocolDelegate(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature, out int protocolVersion);

    /// <summary>
    /// Delegate for verifying legacy handshake MACs.
    /// </summary>
    /// <param name="signatureField">Signature field bytes.</param>
    /// <param name="expectedMac">Expected MAC bytes.</param>
    /// <returns>True when the MAC and padding are valid.</returns>
    delegate bool VerifyLegacyHandshakeMacDelegate(ReadOnlySpan<byte> signatureField, ReadOnlySpan<byte> expectedMac);

    /// <summary>
    /// Delegate for extracting canonical imported public key bytes.
    /// </summary>
    /// <param name="keyField">Fixed packet key field bytes.</param>
    /// <returns>Canonical imported public key bytes.</returns>
    delegate byte[] ExtractImportedPublicKeyBytesDelegate(ReadOnlySpan<byte> keyField);

    /// <summary>
    /// Provides temporary overrides for the packet relay signature keys.
    /// </summary>
    sealed class SignatureKeyScope : IDisposable
    {
        readonly FieldInfo privateKeyField;
        readonly FieldInfo publicKeyField;
        readonly byte[] originalPrivateKey;
        readonly byte[] originalPublicKey;

        /// <summary>
        /// Initializes the scope and applies the provided key material.
        /// </summary>
        /// <param name="packetRelayType">Packet relay type.</param>
        /// <param name="privateKey">Private key bytes.</param>
        /// <param name="publicKey">Public key bytes.</param>
        public SignatureKeyScope(Type packetRelayType, byte[] privateKey, byte[] publicKey)
        {
            privateKeyField = packetRelayType.GetField("_serverSignaturePrivateKey", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Private key field not found.");
            publicKeyField = packetRelayType.GetField("_serverSignaturePublicKey", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Public key field not found.");

            originalPrivateKey = (byte[])(privateKeyField.GetValue(null) ?? Array.Empty<byte>());
            originalPublicKey = (byte[])(publicKeyField.GetValue(null) ?? Array.Empty<byte>());

            privateKeyField.SetValue(null, privateKey);
            publicKeyField.SetValue(null, publicKey);
        }

        /// <summary>
        /// Updates the public key value while the scope is active.
        /// </summary>
        /// <param name="publicKey">Public key bytes.</param>
        public void SetPublicKey(byte[] publicKey)
        {
            publicKeyField.SetValue(null, publicKey);
        }

        /// <summary>
        /// Restores the original key material when the scope ends.
        /// </summary>
        public void Dispose()
        {
            privateKeyField.SetValue(null, originalPrivateKey);
            publicKeyField.SetValue(null, originalPublicKey);
        }
    }
}
