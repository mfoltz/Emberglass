using System.Reflection;
using Emberglass.API.Shared;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for client-side trust-offer handling in packet relay.
/// </summary>
[Collection("Assembly setup")]
public sealed class PacketRelayTrustOfferTests
{
    const int PublicKeyLength = 64;

    /// <summary>
    /// Ensures the default trust-on-first-use path pins the first server key and makes it available for verification.
    /// </summary>
    [Fact]
    public void OnTrustOffer_PinsFirstSeenServerKey()
    {
        byte[] PublicKey = CreateSequence(PublicKeyLength, 0x10);
        HandshakeSignatureConfig ClientConfig = new();

        using ConfigPathScope ConfigScope = new();
        using PacketRelayTrustScope TrustScope = new(ClientConfig, hasExplicitKey: false, serverPublicKey: Array.Empty<byte>());

        InvokeOnTrustOffer(CreateTrustOffer("server-a", PublicKey));

        Assert.Equal(PublicKey, TrustScope.ServerSignaturePublicKey);
        TrustedServerPin Pin = Assert.Single(ClientConfig.TrustedServers);
        Assert.Equal("server-a", Pin.ServerTrustId);
        Assert.Equal(Convert.ToBase64String(PublicKey), Pin.ServerPublicKeyBase64);
        Assert.Equal(HandshakeSignatureConfig.ComputePublicKeyFingerprint(PublicKey), Pin.PublicKeyFingerprint);
    }

    /// <summary>
    /// Ensures an explicit configured server key is accepted without writing a TOFU pin.
    /// </summary>
    [Fact]
    public void OnTrustOffer_AcceptsExplicitServerKeyWithoutPinning()
    {
        byte[] PublicKey = CreateSequence(PublicKeyLength, 0x20);
        HandshakeSignatureConfig ClientConfig = new();

        using PacketRelayTrustScope TrustScope = new(ClientConfig, hasExplicitKey: true, serverPublicKey: PublicKey);

        InvokeOnTrustOffer(CreateTrustOffer("server-a", PublicKey));

        Assert.Equal(PublicKey, TrustScope.ServerSignaturePublicKey);
        Assert.Empty(ClientConfig.TrustedServers);
    }

    /// <summary>
    /// Ensures an explicit configured server key rejects mismatched offers without replacing the trusted key.
    /// </summary>
    [Fact]
    public void OnTrustOffer_RejectsExplicitServerKeyMismatch()
    {
        byte[] ConfiguredPublicKey = CreateSequence(PublicKeyLength, 0x30);
        byte[] OfferedPublicKey = CreateSequence(PublicKeyLength, 0x40);
        HandshakeSignatureConfig ClientConfig = new();

        using PacketRelayTrustScope TrustScope = new(ClientConfig, hasExplicitKey: true, serverPublicKey: ConfiguredPublicKey);

        InvokeOnTrustOffer(CreateTrustOffer("server-a", OfferedPublicKey));

        Assert.Equal(ConfiguredPublicKey, TrustScope.ServerSignaturePublicKey);
        Assert.Empty(ClientConfig.TrustedServers);
    }

    /// <summary>
    /// Ensures invalid trust offers fail closed without installing a public key.
    /// </summary>
    [Fact]
    public void OnTrustOffer_RejectsInvalidOfferWithoutInstallingKey()
    {
        HandshakeSignatureConfig ClientConfig = new();

        using PacketRelayTrustScope TrustScope = new(ClientConfig, hasExplicitKey: false, serverPublicKey: Array.Empty<byte>());

        object Offer = CreateTrustOffer("server-a", CreateSequence(PublicKeyLength, 0x50));
        Offer.GetType().GetProperty("ServerPublicKeyBase64")?.SetValue(Offer, "not-base64!!");

        InvokeOnTrustOffer(Offer);

        Assert.Empty(TrustScope.ServerSignaturePublicKey);
        Assert.Empty(ClientConfig.TrustedServers);
    }

    static object CreateTrustOffer(string serverTrustId, byte[] publicKey)
    {
        Type TrustOfferType = GetTrustOfferType();
        object Offer = Activator.CreateInstance(TrustOfferType, true)
            ?? throw new InvalidOperationException("Failed to create trust offer.");
        string Fingerprint = HandshakeSignatureConfig.ComputePublicKeyFingerprint(publicKey);

        SetProperty(TrustOfferType, Offer, "ServerTrustId", serverTrustId);
        SetProperty(TrustOfferType, Offer, "ServerPublicKeyBase64", Convert.ToBase64String(publicKey));
        SetProperty(TrustOfferType, Offer, "PublicKeyFingerprint", Fingerprint);
        return Offer;
    }

    static void InvokeOnTrustOffer(object offer)
    {
        Type PacketRelayType = GetPacketRelayType();
        MethodInfo Method = PacketRelayType.GetMethod("OnTrustOffer", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("OnTrustOffer method not found.");
        Method.Invoke(null, new[] { offer });
    }

    static Type GetPacketRelayType()
    {
        Type? PacketRelayType = typeof(VBehaviour).Assembly.GetType("Emberglass.Network.PacketRelay");
        return PacketRelayType ?? throw new InvalidOperationException("PacketRelay type not found.");
    }

    static Type GetTrustOfferType()
    {
        Type? TrustOfferType = GetPacketRelayType().GetNestedType("TrustOffer", BindingFlags.NonPublic);
        return TrustOfferType ?? throw new InvalidOperationException("TrustOffer type not found.");
    }

    static void SetProperty(Type type, object instance, string propertyName, object value)
    {
        PropertyInfo Property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found.");
        Property.SetValue(instance, value);
    }

    static byte[] CreateSequence(int length, byte start)
    {
        byte[] Buffer = new byte[length];
        for (int i = 0; i < Buffer.Length; i++)
        {
            Buffer[i] = (byte)(start + i);
        }

        return Buffer;
    }

    sealed class PacketRelayTrustScope : IDisposable
    {
        readonly FieldInfo clientConfigField;
        readonly FieldInfo explicitKeyField;
        readonly FieldInfo serverPublicKeyField;
        readonly object originalClientConfig;
        readonly bool originalExplicitKey;
        readonly byte[] originalServerPublicKey;

        public PacketRelayTrustScope(HandshakeSignatureConfig clientConfig, bool hasExplicitKey, byte[] serverPublicKey)
        {
            Type PacketRelayType = GetPacketRelayType();
            clientConfigField = PacketRelayType.GetField("_clientSignatureConfig", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Client config field not found.");
            explicitKeyField = PacketRelayType.GetField("_clientHasExplicitServerPublicKey", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Explicit key field not found.");
            serverPublicKeyField = PacketRelayType.GetField("_serverSignaturePublicKey", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Server public key field not found.");

            originalClientConfig = clientConfigField.GetValue(null)
                ?? throw new InvalidOperationException("Original client config missing.");
            originalExplicitKey = (bool)(explicitKeyField.GetValue(null)
                ?? throw new InvalidOperationException("Original explicit key state missing."));
            originalServerPublicKey = (byte[])(serverPublicKeyField.GetValue(null) ?? Array.Empty<byte>());

            clientConfigField.SetValue(null, clientConfig);
            explicitKeyField.SetValue(null, hasExplicitKey);
            serverPublicKeyField.SetValue(null, serverPublicKey);
        }

        public byte[] ServerSignaturePublicKey
            => (byte[])(serverPublicKeyField.GetValue(null) ?? Array.Empty<byte>());

        public void Dispose()
        {
            clientConfigField.SetValue(null, originalClientConfig);
            explicitKeyField.SetValue(null, originalExplicitKey);
            serverPublicKeyField.SetValue(null, originalServerPublicKey);
        }
    }

    sealed class ConfigPathScope : IDisposable
    {
        const string ConfigFileName = "HandshakeSignature.json";
        readonly FieldInfo directoryField;
        readonly FieldInfo fileField;
        readonly string originalDirectoryPath;
        readonly string originalFilePath;

        public ConfigPathScope()
        {
            Type ConfigType = typeof(HandshakeSignatureConfig);
            directoryField = ConfigType.GetField("_configDirectoryPath", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Config directory field not found.");
            fileField = ConfigType.GetField("_configFilePath", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Config file field not found.");

            originalDirectoryPath = (string)(directoryField.GetValue(null) ?? string.Empty);
            originalFilePath = (string)(fileField.GetValue(null) ?? string.Empty);

            string ConfigDirectoryPath = Path.Combine(Path.GetTempPath(), "EmberglassTests", Guid.NewGuid().ToString("N"));
            string ConfigFilePath = Path.Combine(ConfigDirectoryPath, ConfigFileName);
            directoryField.SetValue(null, ConfigDirectoryPath);
            fileField.SetValue(null, ConfigFilePath);
        }

        public void Dispose()
        {
            directoryField.SetValue(null, originalDirectoryPath);
            fileField.SetValue(null, originalFilePath);
        }
    }
}
