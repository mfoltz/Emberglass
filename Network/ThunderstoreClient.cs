using BepInEx;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emberglass.Network;

/// <summary>
/// Provides access to Thunderstore metadata for verifying package hashes.
/// </summary>
public sealed class ThunderstoreClient
{
    const string DEFAULT_COMMUNITY = "v-rising";
    const string THUNDERSTORE_BASE_URL = "https://thunderstore.io";
    const int REQUEST_TIMEOUT_SECONDS = 15;
    static readonly HttpClient _httpClient = new();
    static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(15);
    readonly Dictionary<ThunderstoreCacheKey, ThunderstoreCacheEntry> _hashCache = [];
    readonly object _cacheLock = new();

    ThunderstorePackageIdentity? _packageIdentity;
    readonly object _packageIdentityLock = new();

    /// <summary>
    /// Resolves the Thunderstore package identity for a plugin name using thunderstore.toml metadata.
    /// </summary>
    /// <param name="pluginName">The plugin name to resolve.</param>
    /// <param name="identity">The resolved Thunderstore package identity.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the identity is resolved; otherwise <c>false</c>.</returns>
    public bool TryResolvePackageForPlugin(string pluginName, out ThunderstorePackageIdentity identity, out string errorMessage) =>
        TryResolvePackageForPlugin(pluginName, null, out identity, out errorMessage);

    /// <summary>
    /// Resolves the Thunderstore package identity for a plugin name using explicit metadata or thunderstore.toml.
    /// </summary>
    /// <param name="pluginName">The plugin name to resolve.</param>
    /// <param name="explicitIdentity">An explicit identity override when available.</param>
    /// <param name="identity">The resolved Thunderstore package identity.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the identity is resolved; otherwise <c>false</c>.</returns>
    public bool TryResolvePackageForPlugin(
        string pluginName,
        ThunderstorePackageIdentity? explicitIdentity,
        out ThunderstorePackageIdentity identity,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
        {
            identity = default;
            errorMessage = "Plugin name was empty.";
            return false;
        }

        if (!TryResolvePackageIdentity(explicitIdentity, out identity, out errorMessage))
        {
            return false;
        }

        bool matchesPackage =
            pluginName.Equals(identity.Name, StringComparison.OrdinalIgnoreCase) ||
            pluginName.Equals($"{identity.Namespace}-{identity.Name}", StringComparison.OrdinalIgnoreCase);

        if (!matchesPackage)
        {
            errorMessage = $"Plugin '{pluginName}' does not map to Thunderstore package '{identity.Namespace}-{identity.Name}'.";
            identity = default;
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Begins an asynchronous lookup for the Thunderstore SHA-256 hash.
    /// </summary>
    /// <param name="identity">The resolved Thunderstore package identity.</param>
    /// <param name="cancellationToken">A cancellation token for the network request.</param>
    /// <param name="onComplete">Callback invoked with the hash lookup result.</param>
    public void BeginPackageHashLookup(
        ThunderstorePackageIdentity identity,
        CancellationToken cancellationToken,
        Action<ThunderstoreHashResult> onComplete)
    {
        if (onComplete is null)
        {
            throw new ArgumentNullException(nameof(onComplete));
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            ThunderstoreHashResult result = ExecutePackageHashLookup(identity, cancellationToken);
            onComplete(result);
        });
    }

    /// <summary>
    /// Retrieves the Thunderstore SHA-256 hash for the requested package version.
    /// </summary>
    /// <param name="identity">The resolved Thunderstore package identity.</param>
    /// <param name="cancellationToken">A cancellation token for the network request.</param>
    /// <returns>A hash lookup result with status details.</returns>
    ThunderstoreHashResult ExecutePackageHashLookup(
        ThunderstorePackageIdentity identity,
        CancellationToken cancellationToken)
    {
        try
        {
            if (TryGetCachedHash(identity, out ThunderstoreHashResult cachedResult))
            {
                return cachedResult;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ThunderstoreHashResult.Failure(identity.Version, "Thunderstore hash lookup was cancelled.");
            }

            string url = $"{THUNDERSTORE_BASE_URL}/c/{identity.Community}/api/v1/package/{identity.Namespace}/{identity.Name}/";
            using CancellationTokenSource timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS));
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using var response = _httpClient.Send(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                return ThunderstoreHashResult.Failure(
                    identity.Version,
                    $"Thunderstore request failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
            }

            using var responseStream = response.Content.ReadAsStream();
            ThunderstorePackageResponse responseData = JsonSerializer.Deserialize<ThunderstorePackageResponse>(
                responseStream,
                _serializerOptions);

            ThunderstorePackageVersionResponse matchedVersion = responseData?.Versions
                ?.FirstOrDefault(version => string.Equals(version.VersionNumber, identity.Version, StringComparison.OrdinalIgnoreCase));

            if (matchedVersion is null || string.IsNullOrWhiteSpace(matchedVersion.Sha256))
            {
                return ThunderstoreHashResult.Failure(identity.Version, "Thunderstore version metadata was missing a SHA-256 hash.");
            }

            ThunderstoreHashResult result = ThunderstoreHashResult.Success(matchedVersion.Sha256, matchedVersion.VersionNumber);
            CacheHash(identity, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            return ThunderstoreHashResult.Failure(identity.Version, "Thunderstore hash lookup was cancelled.");
        }
        catch (Exception ex)
        {
            return ThunderstoreHashResult.Failure(identity.Version, $"Thunderstore hash lookup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves a Thunderstore package identity from explicit overrides or thunderstore.toml.
    /// </summary>
    /// <param name="explicitIdentity">An explicit identity override.</param>
    /// <param name="identity">The resolved package identity.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the identity is resolved; otherwise <c>false</c>.</returns>
    bool TryResolvePackageIdentity(
        ThunderstorePackageIdentity? explicitIdentity,
        out ThunderstorePackageIdentity identity,
        out string errorMessage)
    {
        if (explicitIdentity.HasValue)
        {
            identity = explicitIdentity.Value;
            errorMessage = string.Empty;
            return true;
        }

        return TryLoadPackageIdentityFromRootToml(out identity, out errorMessage);
    }

    bool TryLoadPackageIdentityFromRootToml(out ThunderstorePackageIdentity identity, out string errorMessage)
    {
        lock (_packageIdentityLock)
        {
            if (_packageIdentity.HasValue)
            {
                identity = _packageIdentity.Value;
                errorMessage = string.Empty;
                return true;
            }
        }

        string tomlPath = Path.Combine(Paths.GameRootPath, "thunderstore.toml");
        if (!File.Exists(tomlPath))
        {
            identity = default;
            errorMessage = $"Unable to locate thunderstore.toml at {tomlPath}.";
            return false;
        }

        string packageNamespace = string.Empty;
        string packageName = string.Empty;
        string packageVersion = string.Empty;
        string communityName = DEFAULT_COMMUNITY;

        bool inPackageSection = false;
        bool inPublishSection = false;

        foreach (string line in File.ReadLines(tomlPath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                inPackageSection = trimmed.Equals("[package]", StringComparison.OrdinalIgnoreCase);
                inPublishSection = trimmed.Equals("[publish]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inPackageSection)
            {
                if (TryReadTomlValue(trimmed, "namespace", out string value))
                {
                    packageNamespace = value;
                }
                else if (TryReadTomlValue(trimmed, "name", out value))
                {
                    packageName = value;
                }
                else if (TryReadTomlValue(trimmed, "versionNumber", out value))
                {
                    packageVersion = value;
                }
            }

            if (inPublishSection && TryReadTomlArray(trimmed, "communities", out string[] communities) && communities.Length > 0)
            {
                communityName = communities[0];
            }
        }

        if (string.IsNullOrWhiteSpace(packageNamespace) ||
            string.IsNullOrWhiteSpace(packageName) ||
            string.IsNullOrWhiteSpace(packageVersion))
        {
            identity = default;
            errorMessage = "thunderstore.toml is missing required package metadata.";
            return false;
        }

        identity = new ThunderstorePackageIdentity(packageNamespace, packageName, packageVersion, communityName);
        lock (_packageIdentityLock)
        {
            _packageIdentity = identity;
        }

        errorMessage = string.Empty;
        return true;
    }

    static bool TryReadTomlValue(string line, string key, out string value)
    {
        value = string.Empty;
        if (!line.StartsWith($"{key} ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int equalsIndex = line.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            return false;
        }

        string rawValue = line[(equalsIndex + 1)..].Trim();
        value = rawValue.Trim('"');
        return !string.IsNullOrWhiteSpace(value);
    }

    static bool TryReadTomlArray(string line, string key, out string[] values)
    {
        values = [];
        if (!line.StartsWith($"{key} ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int equalsIndex = line.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            return false;
        }

        string rawValue = line[(equalsIndex + 1)..].Trim();
        if (!rawValue.StartsWith("[", StringComparison.Ordinal) || !rawValue.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        string inner = rawValue[1..^1];
        values = [..inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim('"'))
            .Where(entry => !string.IsNullOrWhiteSpace(entry))];
        return values.Length > 0;
    }

    bool TryGetCachedHash(ThunderstorePackageIdentity identity, out ThunderstoreHashResult result)
    {
        ThunderstoreCacheKey key = new(identity.Namespace, identity.Name, identity.Version, identity.Community);
        lock (_cacheLock)
        {
            if (_hashCache.TryGetValue(key, out ThunderstoreCacheEntry entry) &&
                DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                result = ThunderstoreHashResult.Success(entry.Hash, entry.Version);
                return true;
            }
        }

        result = default;
        return false;
    }

    void CacheHash(ThunderstorePackageIdentity identity, ThunderstoreHashResult result)
    {
        if (!result.IsSuccess)
        {
            return;
        }

        ThunderstoreCacheKey key = new(identity.Namespace, identity.Name, identity.Version, identity.Community);
        ThunderstoreCacheEntry entry = new(result.Hash, result.Version, DateTimeOffset.UtcNow.Add(_cacheTtl));
        lock (_cacheLock)
        {
            _hashCache[key] = entry;
        }
    }

    record struct ThunderstoreCacheKey(string Namespace, string Name, string Version, string Community);
    record struct ThunderstoreCacheEntry(string Hash, string Version, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Describes the Thunderstore package identity resolved from metadata.
    /// </summary>
    public readonly record struct ThunderstorePackageIdentity(string Namespace, string Name, string Version, string Community);

    /// <summary>
    /// Represents the outcome of a Thunderstore hash lookup.
    /// </summary>
    public readonly record struct ThunderstoreHashResult(bool IsSuccess, string Hash, string Version, string ErrorMessage)
    {
        /// <summary>
        /// Creates a success result with the specified hash and version.
        /// </summary>
        /// <param name="hash">The Thunderstore hash.</param>
        /// <param name="version">The matching version identifier.</param>
        /// <returns>A success result.</returns>
        public static ThunderstoreHashResult Success(string hash, string version) =>
            new(true, hash, version, string.Empty);

        /// <summary>
        /// Creates a failure result with the specified version and error message.
        /// </summary>
        /// <param name="version">The version that failed to resolve.</param>
        /// <param name="errorMessage">The failure description.</param>
        /// <returns>A failure result.</returns>
        public static ThunderstoreHashResult Failure(string version, string errorMessage) =>
            new(false, string.Empty, version, errorMessage);
    }

    sealed class ThunderstorePackageResponse
    {
        [JsonPropertyName("versions")]
        public List<ThunderstorePackageVersionResponse> Versions { get; init; } = [];

        [JsonPropertyName("categories")]
        public List<string> Categories { get; init; } = [];

        [JsonPropertyName("tags")]
        public List<string> Tags { get; init; } = [];
    }

    sealed class ThunderstorePackageVersionResponse
    {
        [JsonPropertyName("version_number")]
        public string VersionNumber { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;
    }

}
