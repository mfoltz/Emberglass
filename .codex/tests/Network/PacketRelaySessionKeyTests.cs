using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for session key derivation and HKDF helpers in packet relay.
/// </summary>
[Collection("Assembly setup")]
public sealed class PacketRelaySessionKeyTests
{
    /// <summary>
    /// Ensures session keys remain consistent across client/server ordering with identical inputs.
    /// </summary>
    [Fact]
    public void DeriveSessionKey_ProducesSameOutputForMatchingInputs()
    {
        Type PacketRelayType = GetPacketRelayType();
        DeriveSessionKeyDelegate DeriveSessionKey = GetDeriveSessionKey(PacketRelayType);

        byte[] SharedSecret = CreateSequence(Registry.Const.STANDARD_LENGTH, 0x11);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x22);
        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x33);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x44);

        byte[] ServerDerivedKey = DeriveSessionKey(SharedSecret, Nonce, ServerPublicKey, ClientPublicKey, Registry.Const.PROTOCOL_VERSION);
        byte[] ClientDerivedKey = DeriveSessionKey(SharedSecret, Nonce, ServerPublicKey, ClientPublicKey, Registry.Const.PROTOCOL_VERSION);

        Assert.Equal(ServerDerivedKey, ClientDerivedKey);
    }

    /// <summary>
    /// Ensures session key derivation output uses the standard length.
    /// </summary>
    [Fact]
    public void DeriveSessionKey_ReturnsStandardLengthOutput()
    {
        Type PacketRelayType = GetPacketRelayType();
        DeriveSessionKeyDelegate DeriveSessionKey = GetDeriveSessionKey(PacketRelayType);

        byte[] SharedSecret = CreateSequence(Registry.Const.STANDARD_LENGTH, 0x55);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x66);
        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x77);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x88);

        byte[] DerivedKey = DeriveSessionKey(SharedSecret, Nonce, ServerPublicKey, ClientPublicKey, Registry.Const.PROTOCOL_VERSION);

        Assert.Equal(Registry.Const.STANDARD_LENGTH, DerivedKey.Length);
    }

    /// <summary>
    /// Ensures changes to nonce or public keys yield different session keys.
    /// </summary>
    [Fact]
    public void DeriveSessionKey_ChangesWhenNonceOrKeysChange()
    {
        Type PacketRelayType = GetPacketRelayType();
        DeriveSessionKeyDelegate DeriveSessionKey = GetDeriveSessionKey(PacketRelayType);

        byte[] SharedSecret = CreateSequence(Registry.Const.STANDARD_LENGTH, 0x10);
        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x20);
        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x30);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x40);

        byte[] BaselineKey = DeriveSessionKey(SharedSecret, Nonce, ServerPublicKey, ClientPublicKey, Registry.Const.PROTOCOL_VERSION);

        byte[] ModifiedNonce = Nonce.ToArray();
        ModifiedNonce[^1] ^= 0xFF;

        byte[] NonceChangedKey = DeriveSessionKey(SharedSecret, ModifiedNonce, ServerPublicKey, ClientPublicKey, Registry.Const.PROTOCOL_VERSION);
        Assert.NotEqual(BaselineKey, NonceChangedKey);

        byte[] ModifiedServerKey = ServerPublicKey.ToArray();
        ModifiedServerKey[^1] ^= 0x0A;

        byte[] ServerKeyChangedKey = DeriveSessionKey(SharedSecret, Nonce, ModifiedServerKey, ClientPublicKey, Registry.Const.PROTOCOL_VERSION);
        Assert.NotEqual(BaselineKey, ServerKeyChangedKey);

        byte[] ModifiedClientKey = ClientPublicKey.ToArray();
        ModifiedClientKey[^1] ^= 0x0B;

        byte[] ClientKeyChangedKey = DeriveSessionKey(SharedSecret, Nonce, ServerPublicKey, ModifiedClientKey, Registry.Const.PROTOCOL_VERSION);
        Assert.NotEqual(BaselineKey, ClientKeyChangedKey);

        byte[] SwappedOrderKey = DeriveSessionKey(SharedSecret, Nonce, ClientPublicKey, ServerPublicKey, Registry.Const.PROTOCOL_VERSION);
        Assert.NotEqual(BaselineKey, SwappedOrderKey);
    }

    /// <summary>
    /// Ensures the HKDF salt matches the legacy HMAC or SHA-256 nonce derivation based on protocol version.
    /// </summary>
    [Fact]
    public void BuildHkdfSalt_UsesLegacyHmacForV1AndSha256ForV2()
    {
        Type PacketRelayType = GetPacketRelayType();
        BuildHkdfSaltDelegate BuildHkdfSalt = GetBuildHkdfSalt(PacketRelayType);

        byte[] Nonce = CreateSequence(Registry.Const.HANDSHAKE_NONCE_BYTES, 0x13);

        byte[] LegacySalt = BuildHkdfSalt(Registry.Const.LEGACY_PROTOCOL_VERSION, Nonce);
        byte[] V2Salt = BuildHkdfSalt(Registry.Const.PROTOCOL_VERSION, Nonce);

        byte[] LegacyExpected;
#pragma warning disable CS0618
        using (var Hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Registry.Const.SHARED_KEY)))
#pragma warning restore CS0618
        {
            LegacyExpected = Hmac.ComputeHash(Nonce);
        }

        byte[] V2Expected = SHA256.HashData(Nonce);

        Assert.Equal(LegacyExpected, LegacySalt);
        Assert.Equal(V2Expected, V2Salt);
        Assert.NotEqual(LegacySalt, V2Salt);
    }

    /// <summary>
    /// Ensures the HKDF info includes the info label, protocol version, and public keys in order.
    /// </summary>
    [Fact]
    public void BuildHkdfInfo_UsesExpectedOrdering()
    {
        Type PacketRelayType = GetPacketRelayType();
        BuildHkdfInfoDelegate BuildHkdfInfo = GetBuildHkdfInfo(PacketRelayType);

        byte[] ServerPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0x91);
        byte[] ClientPublicKey = CreateSequence(Registry.Const.EXCHANGE_LENGTH, 0xA1);

        byte[] Info = BuildHkdfInfo(Registry.Const.PROTOCOL_VERSION, ServerPublicKey, ClientPublicKey);

        byte[] VersionBytes = BitConverter.GetBytes(Registry.Const.PROTOCOL_VERSION);
        byte[] InfoLabel = Encoding.UTF8.GetBytes(Registry.Const.HKDF_INFO);
        byte[] Expected = new byte[InfoLabel.Length + VersionBytes.Length + ServerPublicKey.Length + ClientPublicKey.Length];

        Buffer.BlockCopy(InfoLabel, 0, Expected, 0, InfoLabel.Length);
        Buffer.BlockCopy(VersionBytes, 0, Expected, InfoLabel.Length, VersionBytes.Length);
        Buffer.BlockCopy(ServerPublicKey, 0, Expected, InfoLabel.Length + VersionBytes.Length, ServerPublicKey.Length);
        Buffer.BlockCopy(ClientPublicKey, 0, Expected, InfoLabel.Length + VersionBytes.Length + ServerPublicKey.Length, ClientPublicKey.Length);

        Assert.Equal(Expected, Info);
    }

    /// <summary>
    /// Resolves the packet relay type from the Emberglass assembly.
    /// </summary>
    /// <returns>The packet relay type.</returns>
    static Type GetPacketRelayType()
    {
        Assembly EmberglassAssembly = typeof(Registry).Assembly;
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
    /// Retrieves a delegate for session key derivation.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for session key derivation.</returns>
    static DeriveSessionKeyDelegate GetDeriveSessionKey(Type packetRelayType)
        => CreateDelegate<DeriveSessionKeyDelegate>(packetRelayType, "DeriveSessionKey");

    /// <summary>
    /// Retrieves a delegate for building HKDF salt bytes.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for HKDF salt building.</returns>
    static BuildHkdfSaltDelegate GetBuildHkdfSalt(Type packetRelayType)
        => CreateDelegate<BuildHkdfSaltDelegate>(packetRelayType, "BuildHkdfSalt");

    /// <summary>
    /// Retrieves a delegate for building HKDF info bytes.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for HKDF info building.</returns>
    static BuildHkdfInfoDelegate GetBuildHkdfInfo(Type packetRelayType)
        => CreateDelegate<BuildHkdfInfoDelegate>(packetRelayType, "BuildHkdfInfo");

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
    /// Delegate for session key derivation.
    /// </summary>
    /// <param name="ecdhSecret">ECDH shared secret bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Derived session key bytes.</returns>
    delegate byte[] DeriveSessionKeyDelegate(ReadOnlySpan<byte> ecdhSecret, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, int protocolVersion);

    /// <summary>
    /// Delegate for HKDF salt generation.
    /// </summary>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <returns>HKDF salt bytes.</returns>
    delegate byte[] BuildHkdfSaltDelegate(int protocolVersion, ReadOnlySpan<byte> nonce);

    /// <summary>
    /// Delegate for HKDF info generation.
    /// </summary>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <returns>HKDF info bytes.</returns>
    delegate byte[] BuildHkdfInfoDelegate(int protocolVersion, ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey);
}
