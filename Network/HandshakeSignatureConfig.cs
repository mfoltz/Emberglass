using BepInEx;
using Emberglass.API.Shared;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Emberglass.Network;

/// <summary>
/// Provides configuration storage for P-256 handshake signing keys.
/// </summary>
internal sealed class HandshakeSignatureConfig
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    static string _configDirectoryPath = string.Empty;
    static string _configFilePath = string.Empty;
    static string ConfigDirectoryPath
        => string.IsNullOrWhiteSpace(_configDirectoryPath)
            ? Path.Combine(GetDefaultConfigPath(), MyPluginInfo.PLUGIN_NAME)
            : _configDirectoryPath;
    static string ConfigFilePath
        => string.IsNullOrWhiteSpace(_configFilePath)
            ? Path.Combine(ConfigDirectoryPath, "HandshakeSignature.json")
            : _configFilePath;

    /// <summary>
    /// Gets or sets the server P-256 private key stored as a Base64 string.
    /// </summary>
    public string ServerPrivateKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server P-256 public key stored as a Base64 string.
    /// </summary>
    public string ServerPublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable server trust identifier used for client-side TOFU pins.
    /// </summary>
    public string ServerTrustId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client-side trusted server public-key pins.
    /// </summary>
    public List<TrustedServerPin> TrustedServers { get; set; } = [];

    /// <summary>
    /// Loads the handshake signature configuration from disk.
    /// </summary>
    /// <returns>The loaded configuration.</returns>
    public static HandshakeSignatureConfig Load()
        => LoadInternal();

    /// <summary>
    /// Loads the handshake signature configuration and ensures the server has a keypair.
    /// </summary>
    /// <returns>The loaded configuration.</returns>
    public static HandshakeSignatureConfig LoadForServer()
    {
        HandshakeSignatureConfig config = LoadInternal();
        bool changed = false;

        if (string.IsNullOrWhiteSpace(config.ServerTrustId))
        {
            config.ServerTrustId = Guid.NewGuid().ToString("N");
            changed = true;
        }

        if (!config.TryGetServerPrivateKey(out _)
            || !config.TryGetServerPublicKey(out _))
        {
            GenerateServerKeys(config);
            changed = true;
        }

        if (changed)
        {
            config.Save();
        }

        return config;
    }

    /// <summary>
    /// Attempts to decode the server public key from Base64.
    /// </summary>
    /// <param name="publicKey">Decoded public key bytes.</param>
    /// <returns>True when the key is available and decodes successfully.</returns>
    public bool TryGetServerPublicKey(out byte[] publicKey)
        => TryDecodeKey(ServerPublicKeyBase64, out publicKey);

    /// <summary>
    /// Attempts to decode the server private key from Base64.
    /// </summary>
    /// <param name="privateKey">Decoded private key bytes.</param>
    /// <returns>True when the key is available and decodes successfully.</returns>
    public bool TryGetServerPrivateKey(out byte[] privateKey)
        => TryDecodeKey(ServerPrivateKeyBase64, out privateKey);

    /// <summary>
    /// Trusts or validates a server public key for the provided stable server trust identifier.
    /// </summary>
    /// <param name="serverTrustId">Stable server trust identifier.</param>
    /// <param name="publicKey">Server public key bytes.</param>
    /// <returns>The trust decision for the provided key.</returns>
    public TrustPinResult TrustServerPublicKey(string serverTrustId, byte[] publicKey)
    {
        if (string.IsNullOrWhiteSpace(serverTrustId)
            || publicKey.Length != SignaturesP256.PublicKeySize)
        {
            return TrustPinResult.Invalid;
        }

        TrustedServers ??= [];

        string publicKeyBase64 = Convert.ToBase64String(publicKey);
        string fingerprint = ComputePublicKeyFingerprint(publicKey);
        TrustedServerPin existing = TrustedServers.FirstOrDefault(pin =>
            string.Equals(pin.ServerTrustId, serverTrustId, StringComparison.Ordinal));

        if (existing is null)
        {
            TrustedServers.Add(new TrustedServerPin
            {
                ServerTrustId = serverTrustId,
                ServerPublicKeyBase64 = publicKeyBase64,
                PublicKeyFingerprint = fingerprint,
                FirstSeenUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            });
            Save();
            return TrustPinResult.Pinned;
        }

        if (!string.Equals(existing.ServerPublicKeyBase64, publicKeyBase64, StringComparison.Ordinal)
            || !string.Equals(existing.PublicKeyFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return TrustPinResult.Mismatch;
        }

        existing.LastSeenUtc = DateTimeOffset.UtcNow;
        Save();
        return TrustPinResult.AlreadyTrusted;
    }

    /// <summary>
    /// Computes a stable SHA-256 fingerprint for a server public key.
    /// </summary>
    /// <param name="publicKey">Server public key bytes.</param>
    /// <returns>Uppercase hexadecimal fingerprint.</returns>
    public static string ComputePublicKeyFingerprint(ReadOnlySpan<byte> publicKey)
        => Convert.ToHexString(SHA256.HashData(publicKey.ToArray()));

    /// <summary>
    /// Persists the configuration to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            string configDirectoryPath = ConfigDirectoryPath;
            string configFilePath = ConfigFilePath;
            if (!Directory.Exists(configDirectoryPath))
            {
                Directory.CreateDirectory(configDirectoryPath);
            }

            string payload = JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(configFilePath, payload);
        }
        catch (IOException ex)
        {
            LogWarning($"Failed to save handshake signature config: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the configuration from disk or returns defaults when unavailable.
    /// </summary>
    /// <returns>The loaded configuration.</returns>
    static HandshakeSignatureConfig LoadInternal()
    {
        try
        {
            string configDirectoryPath = ConfigDirectoryPath;
            string configFilePath = ConfigFilePath;
            if (!Directory.Exists(configDirectoryPath))
            {
                Directory.CreateDirectory(configDirectoryPath);
            }

            if (!File.Exists(configFilePath))
            {
                HandshakeSignatureConfig config = new();
                string payload = JsonSerializer.Serialize(config, _jsonOptions);
                File.WriteAllText(configFilePath, payload);
                return config;
            }

            string fileText = File.ReadAllText(configFilePath);
            if (fileText.IsNullOrWhiteSpace())
            {
                return new HandshakeSignatureConfig();
            }

            return JsonSerializer.Deserialize<HandshakeSignatureConfig>(fileText, _jsonOptions)
                ?? new HandshakeSignatureConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            LogWarning($"Failed to load handshake signature config: {ex.Message}");
            return new HandshakeSignatureConfig();
        }
    }

    /// <summary>
    /// Generates a new server P-256 keypair and populates the config values.
    /// </summary>
    /// <param name="config">Configuration instance to populate.</param>
    static void GenerateServerKeys(HandshakeSignatureConfig config)
    {
        SignaturesP256.CreateKey(out byte[] publicKey, out byte[] privateKey);
        config.ServerPublicKeyBase64 = Convert.ToBase64String(publicKey);
        config.ServerPrivateKeyBase64 = Convert.ToBase64String(privateKey);
    }

    /// <summary>
    /// Attempts to decode a Base64 key string into bytes.
    /// </summary>
    /// <param name="base64">Base64-encoded key string.</param>
    /// <param name="key">Decoded key bytes.</param>
    /// <returns>True when the key decodes successfully.</returns>
    static bool TryDecodeKey(string base64, out byte[] key)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            key = [];
            return false;
        }

        try
        {
            key = Convert.FromBase64String(base64);
            return key.Length > 0;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }

    /// <summary>
    /// Gets the default BepInEx config path, falling back to a temp path for tests.
    /// </summary>
    /// <returns>The config root path.</returns>
    static string GetDefaultConfigPath()
    {
        try
        {
            string configPath = Paths.ConfigPath;
            return string.IsNullOrWhiteSpace(configPath)
                ? Path.Combine(Path.GetTempPath(), MyPluginInfo.PLUGIN_NAME)
                : configPath;
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException)
        {
            return Path.Combine(Path.GetTempPath(), MyPluginInfo.PLUGIN_NAME);
        }
    }

    /// <summary>
    /// Logs a warning when the runtime logger is available.
    /// </summary>
    /// <param name="message">Warning message.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void LogWarning(string message)
    {
        try
        {
            VWorld.Log.LogWarning(message);
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException)
        {
        }
    }
}

/// <summary>
/// Result of evaluating a TOFU server public-key pin.
/// </summary>
internal enum TrustPinResult
{
    /// <summary>
    /// The server trust identifier or public key was invalid.
    /// </summary>
    Invalid,

    /// <summary>
    /// The server was pinned for the first time.
    /// </summary>
    Pinned,

    /// <summary>
    /// The server key matched an existing pin.
    /// </summary>
    AlreadyTrusted,

    /// <summary>
    /// The server key did not match an existing pin.
    /// </summary>
    Mismatch
}

/// <summary>
/// Client-side trusted server public-key pin.
/// </summary>
internal sealed class TrustedServerPin
{
    /// <summary>
    /// Gets or sets the stable server trust identifier.
    /// </summary>
    public string ServerTrustId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server public key as Base64.
    /// </summary>
    public string ServerPublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the public-key SHA-256 fingerprint.
    /// </summary>
    public string PublicKeyFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the pin was first observed.
    /// </summary>
    public DateTimeOffset FirstSeenUtc { get; set; }

    /// <summary>
    /// Gets or sets when the pin was last observed.
    /// </summary>
    public DateTimeOffset LastSeenUtc { get; set; }
}
