using System.Reflection;
using Emberglass.API.Shared;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides tests for the handshake signature configuration lifecycle.
/// </summary>
[Collection("Assembly setup")]
public sealed class HandshakeSignatureConfigTests
{
    const int P256PublicKeyLength = 64;
    const int P256PrivateKeyLength = 32;

    /// <summary>
    /// Ensures loading the configuration creates a default file when one is missing.
    /// </summary>
    [Fact]
    public void LoadInternal_CreatesDefaultConfigFileWhenMissing()
    {
        Type ConfigType = GetHandshakeSignatureConfigType();

        using ConfigPathScope Scope = new(ConfigType);

        Assert.False(File.Exists(Scope.ConfigFilePath));

        object Config = InvokeStaticMethod(ConfigType, "LoadInternal");

        Assert.NotNull(Config);
        Assert.True(File.Exists(Scope.ConfigFilePath));
        string FileContents = File.ReadAllText(Scope.ConfigFilePath);
        Assert.False(string.IsNullOrWhiteSpace(FileContents));
    }

    /// <summary>
    /// Ensures server-side loading generates a usable Ed25519 keypair when keys are missing.
    /// </summary>
    [Fact]
    public void LoadForServer_GeneratesKeyPairWhenMissing()
    {
        Type ConfigType = GetHandshakeSignatureConfigType();

        using ConfigPathScope Scope = new(ConfigType);

        object Config = InvokeStaticMethod(ConfigType, "LoadForServer");

        bool PublicKeyResult = InvokeTryGetKey(Config, "TryGetServerPublicKey", out byte[] PublicKey);
        bool PrivateKeyResult = InvokeTryGetKey(Config, "TryGetServerPrivateKey", out byte[] PrivateKey);

        Assert.True(PublicKeyResult);
        Assert.True(PrivateKeyResult);
        Assert.Equal(P256PublicKeyLength, PublicKey.Length);
        Assert.Equal(P256PrivateKeyLength, PrivateKey.Length);
        string ServerTrustId = (string)(ConfigType.GetProperty("ServerTrustId")?.GetValue(Config)
            ?? throw new InvalidOperationException("ServerTrustId property missing."));
        Assert.False(string.IsNullOrWhiteSpace(ServerTrustId));
    }

    /// <summary>
    /// Ensures first-use trust stores a server public-key pin and accepts it on later checks.
    /// </summary>
    [Fact]
    public void TrustServerPublicKey_PinsAndAcceptsKnownServer()
    {
        Type ConfigType = GetHandshakeSignatureConfigType();
        object Config = CreateConfigInstance(ConfigType);
        byte[] PublicKey = CreateSequence(P256PublicKeyLength, 0x20);

        object FirstResult = InvokeTrustServerPublicKey(Config, "server-a", PublicKey);
        object SecondResult = InvokeTrustServerPublicKey(Config, "server-a", PublicKey);

        Assert.Equal("Pinned", FirstResult.ToString());
        Assert.Equal("AlreadyTrusted", SecondResult.ToString());
    }

    /// <summary>
    /// Ensures a known server trust identifier rejects a changed public key.
    /// </summary>
    [Fact]
    public void TrustServerPublicKey_RejectsChangedKnownServerKey()
    {
        Type ConfigType = GetHandshakeSignatureConfigType();
        object Config = CreateConfigInstance(ConfigType);
        byte[] PublicKey = CreateSequence(P256PublicKeyLength, 0x30);
        byte[] ChangedPublicKey = PublicKey.ToArray();
        ChangedPublicKey[^1] ^= 0xFF;

        object FirstResult = InvokeTrustServerPublicKey(Config, "server-a", PublicKey);
        object SecondResult = InvokeTrustServerPublicKey(Config, "server-a", ChangedPublicKey);

        Assert.Equal("Pinned", FirstResult.ToString());
        Assert.Equal("Mismatch", SecondResult.ToString());
    }

    /// <summary>
    /// Ensures invalid Base64 values return false and empty outputs for server key retrieval.
    /// </summary>
    [Fact]
    public void TryGetServerKeys_ReturnsFalseForInvalidBase64()
    {
        Type ConfigType = GetHandshakeSignatureConfigType();
        object Config = CreateConfigInstance(ConfigType);

        SetPropertyValue(ConfigType, Config, "ServerPublicKeyBase64", "not-base64!!");
        SetPropertyValue(ConfigType, Config, "ServerPrivateKeyBase64", "still-not-base64!!");

        bool PublicKeyResult = InvokeTryGetKey(Config, "TryGetServerPublicKey", out byte[] PublicKey);
        bool PrivateKeyResult = InvokeTryGetKey(Config, "TryGetServerPrivateKey", out byte[] PrivateKey);

        Assert.False(PublicKeyResult);
        Assert.False(PrivateKeyResult);
        Assert.Empty(PublicKey);
        Assert.Empty(PrivateKey);
    }

    /// <summary>
    /// Resolves the internal handshake configuration type from the Emberglass assembly.
    /// </summary>
    /// <returns>The handshake configuration type.</returns>
    static Type GetHandshakeSignatureConfigType()
    {
        Assembly EmberglassAssembly = typeof(VBehaviour).Assembly;
        Type? ConfigType = EmberglassAssembly.GetType("Emberglass.Network.HandshakeSignatureConfig");
        return ConfigType ?? throw new InvalidOperationException("HandshakeSignatureConfig type not found.");
    }

    /// <summary>
    /// Creates an instance of the handshake configuration using non-public access.
    /// </summary>
    /// <param name="configType">The configuration type to instantiate.</param>
    /// <returns>The configuration instance.</returns>
    static object CreateConfigInstance(Type configType)
    {
        return Activator.CreateInstance(configType, true)
            ?? throw new InvalidOperationException("Failed to create handshake config instance.");
    }

    /// <summary>
    /// Invokes a static method by name on the handshake configuration type.
    /// </summary>
    /// <param name="configType">The configuration type.</param>
    /// <param name="methodName">The static method name.</param>
    /// <param name="parameters">The parameters to pass.</param>
    /// <returns>The returned value.</returns>
    static object InvokeStaticMethod(Type configType, string methodName, params object?[] parameters)
    {
        MethodInfo Method = configType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return Method.Invoke(null, parameters)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
    }

    /// <summary>
    /// Invokes a TryGet method on the configuration instance to retrieve key bytes.
    /// </summary>
    /// <param name="config">The configuration instance.</param>
    /// <param name="methodName">The TryGet method name.</param>
    /// <param name="keyBytes">The decoded key bytes.</param>
    /// <returns>True when decoding succeeded.</returns>
    static bool InvokeTryGetKey(object config, string methodName, out byte[] keyBytes)
    {
        MethodInfo Method = config.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        object?[] Arguments = { null };
        bool Result = (bool)(Method.Invoke(config, Arguments)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null."));
        keyBytes = (byte[])(Arguments[0] ?? Array.Empty<byte>());
        return Result;
    }

    /// <summary>
    /// Sets a property value using reflection on the handshake configuration instance.
    /// </summary>
    /// <param name="configType">The configuration type.</param>
    /// <param name="config">The configuration instance.</param>
    /// <param name="propertyName">The property name to set.</param>
    /// <param name="value">The value to set.</param>
    static void SetPropertyValue(Type configType, object config, string propertyName, string value)
    {
        PropertyInfo Property = configType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found.");
        Property.SetValue(config, value);
    }

    /// <summary>
    /// Invokes the trust-pin method on the configuration instance.
    /// </summary>
    /// <param name="config">The configuration instance.</param>
    /// <param name="serverTrustId">The server trust identifier.</param>
    /// <param name="publicKey">The public key bytes.</param>
    /// <returns>The trust-pin result enum value.</returns>
    static object InvokeTrustServerPublicKey(object config, string serverTrustId, byte[] publicKey)
    {
        MethodInfo Method = config.GetType().GetMethod("TrustServerPublicKey", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Method 'TrustServerPublicKey' not found.");
        return Method.Invoke(config, new object[] { serverTrustId, publicKey })
            ?? throw new InvalidOperationException("Method 'TrustServerPublicKey' returned null.");
    }

    /// <summary>
    /// Creates a deterministic byte sequence.
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
    /// Provides a temporary override for the handshake configuration file paths.
    /// </summary>
    sealed class ConfigPathScope : IDisposable
    {
        const string ConfigFileName = "HandshakeSignature.json";

        readonly Type configType;
        readonly string originalDirectoryPath;
        readonly string originalFilePath;

        /// <summary>
        /// Initializes the scope and swaps in temporary configuration paths.
        /// </summary>
        /// <param name="configType">The configuration type to modify.</param>
        public ConfigPathScope(Type configType)
        {
            this.configType = configType;
            FieldInfo DirectoryField = GetStaticField("_configDirectoryPath");
            FieldInfo FileField = GetStaticField("_configFilePath");

            originalDirectoryPath = (string)(DirectoryField.GetValue(null)
                ?? throw new InvalidOperationException("Original directory path missing."));
            originalFilePath = (string)(FileField.GetValue(null)
                ?? throw new InvalidOperationException("Original file path missing."));

            ConfigDirectoryPath = Path.Combine(Path.GetTempPath(), "EmberglassTests", Guid.NewGuid().ToString("N"));
            ConfigFilePath = Path.Combine(ConfigDirectoryPath, ConfigFileName);

            SetStaticReadonlyField(DirectoryField, ConfigDirectoryPath);
            SetStaticReadonlyField(FileField, ConfigFilePath);
        }

        /// <summary>
        /// Gets the temporary configuration directory path.
        /// </summary>
        public string ConfigDirectoryPath { get; }

        /// <summary>
        /// Gets the temporary configuration file path.
        /// </summary>
        public string ConfigFilePath { get; }

        /// <summary>
        /// Restores original configuration paths and removes temporary files.
        /// </summary>
        public void Dispose()
        {
            FieldInfo DirectoryField = GetStaticField("_configDirectoryPath");
            FieldInfo FileField = GetStaticField("_configFilePath");

            SetStaticReadonlyField(DirectoryField, originalDirectoryPath);
            SetStaticReadonlyField(FileField, originalFilePath);

            if (Directory.Exists(ConfigDirectoryPath))
            {
                Directory.Delete(ConfigDirectoryPath, true);
            }
        }

        /// <summary>
        /// Retrieves a static non-public field from the configuration type.
        /// </summary>
        /// <param name="fieldName">The field name to retrieve.</param>
        /// <returns>The field info.</returns>
        FieldInfo GetStaticField(string fieldName)
        {
            return configType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Field '{fieldName}' not found.");
        }

        /// <summary>
        /// Overrides static readonly field values using reflection.
        /// </summary>
        /// <param name="field">The field to update.</param>
        /// <param name="value">The value to set.</param>
        static void SetStaticReadonlyField(FieldInfo field, string value)
        {
            if (field.IsInitOnly)
            {
                FieldInfo? AttributesField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.Instance | BindingFlags.NonPublic);
                if (AttributesField is not null)
                {
                    AttributesField.SetValue(field, field.Attributes & ~FieldAttributes.InitOnly);
                }
            }

            field.SetValue(null, value);
        }
    }
}
