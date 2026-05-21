using BepInEx;
using System.Text.Json;

namespace Emberglass.Network;

/// <summary>
/// Loads and caches metadata that describes shared plugin releases, client-safety flags, and hotload opt-ins.
/// </summary>
internal sealed class PluginShareMetadataStore
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    static readonly string _configDirectoryPath = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
    static readonly string _configFilePath = Path.Combine(_configDirectoryPath, "ShareMetadata.json");

    readonly object _cacheLock = new();
    Dictionary<string, PluginShareMetadata> _cachedMetadata = new(StringComparer.OrdinalIgnoreCase);
    DateTimeOffset _cachedLastWriteUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Attempts to resolve share metadata for a plugin base name.
    /// </summary>
    /// <param name="pluginBaseName">The plugin base name without extension.</param>
    /// <param name="metadata">The resolved share metadata.</param>
    /// <param name="errorMessage">An error message describing a failed lookup.</param>
    /// <returns><c>true</c> when metadata is resolved; otherwise <c>false</c>.</returns>
    public bool TryGetPluginMetadata(string pluginBaseName, out PluginShareMetadata metadata, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(pluginBaseName))
        {
            metadata = default;
            errorMessage = "Plugin base name was empty.";
            return false;
        }

        if (!TryLoadMetadata(out IReadOnlyDictionary<string, PluginShareMetadata> entries, out errorMessage))
        {
            metadata = default;
            return false;
        }

        if (!entries.TryGetValue(pluginBaseName, out metadata))
        {
            errorMessage = $"Share metadata was not found for '{pluginBaseName}'. Add an entry to {_configFilePath}.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Attempts to load the share metadata dictionary from disk, using cached data when possible.
    /// </summary>
    /// <param name="entries">The metadata entries keyed by plugin base name.</param>
    /// <param name="errorMessage">An error message describing a failed load.</param>
    /// <returns><c>true</c> when metadata entries are loaded; otherwise <c>false</c>.</returns>
    bool TryLoadMetadata(out IReadOnlyDictionary<string, PluginShareMetadata> entries, out string errorMessage)
    {
        if (!File.Exists(_configFilePath))
        {
            entries = _cachedMetadata;
            errorMessage = $"Unable to locate share metadata at {_configFilePath}.";
            return false;
        }

        DateTimeOffset lastWriteUtc = File.GetLastWriteTimeUtc(_configFilePath);
        lock (_cacheLock)
        {
            if (_cachedMetadata.Count > 0 && lastWriteUtc <= _cachedLastWriteUtc)
            {
                entries = _cachedMetadata;
                errorMessage = string.Empty;
                return true;
            }
        }

        try
        {
            string payload = File.ReadAllText(_configFilePath);
            PluginShareMetadataPayload metadataPayload =
                JsonSerializer.Deserialize<PluginShareMetadataPayload>(payload, _jsonOptions);

            if (metadataPayload?.Plugins is null)
            {
                entries = _cachedMetadata;
                errorMessage = "Share metadata JSON is missing the Plugins dictionary.";
                return false;
            }

            var nextCache = new Dictionary<string, PluginShareMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, PluginShareMetadataEntry> pair in metadataPayload.Plugins)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                {
                    continue;
                }

                nextCache[pair.Key] = pair.Value.ToMetadata();
            }

            lock (_cacheLock)
            {
                _cachedMetadata = nextCache;
                _cachedLastWriteUtc = lastWriteUtc;
            }

            entries = nextCache;
            errorMessage = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            entries = _cachedMetadata;
            errorMessage = $"Share metadata JSON could not be parsed: {ex.Message}";
            return false;
        }
        catch (IOException ex)
        {
            entries = _cachedMetadata;
            errorMessage = $"Share metadata could not be read: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Describes share metadata for a plugin release.
    /// </summary>
    public readonly record struct PluginShareMetadata(
        string GitHubRepo,
        string GitHubTag,
        string GitHubAssetName,
        bool ClientSafe,
        bool HotloadAllowed,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Categories,
        string LocalSha256);

    sealed class PluginShareMetadataPayload
    {
        public Dictionary<string, PluginShareMetadataEntry> Plugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    sealed class PluginShareMetadataEntry
    {
        public string GitHubRepo { get; set; } = string.Empty;
        public string GitHubTag { get; set; } = string.Empty;
        public string GitHubAssetName { get; set; } = string.Empty;
        public bool ClientSafe { get; set; }
        public bool HotloadAllowed { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<string> Categories { get; set; } = [];
        public string LocalSha256 { get; set; } = string.Empty;

        public PluginShareMetadata ToMetadata()
            => new(
                GitHubRepo ?? string.Empty,
                GitHubTag ?? string.Empty,
                GitHubAssetName ?? string.Empty,
                ClientSafe,
                HotloadAllowed,
                Tags ?? [],
                Categories ?? [],
                LocalSha256 ?? string.Empty);
    }
}
