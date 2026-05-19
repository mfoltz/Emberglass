using BepInEx;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emberglass.CustomPrefabs;

internal sealed record CustomPrefabManifestEntry(
    string ProviderId,
    int SourcePrefabGuid,
    int GeneratedPrefabGuid,
    string GeneratedAssetGuid,
    string GeneratedAssetName,
    bool ClientSyncRequired,
    CustomPrefabCleanupPolicy CleanupPolicy);

internal sealed class CustomPrefabManifest
{
    public List<CustomPrefabManifestEntry> Entries { get; set; } = [];
}

internal sealed class CustomPrefabManifestStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static CustomPrefabManifestStore Default { get; } = new(
        Path.Combine(GetDefaultConfigPath(), MyPluginInfo.PLUGIN_NAME, "CustomPrefabs.json"));

    readonly string _manifestPath;

    public CustomPrefabManifestStore(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Manifest path is required.", nameof(manifestPath));
        }

        _manifestPath = manifestPath;
    }

    public CustomPrefabManifest Load()
    {
        if (!File.Exists(_manifestPath))
        {
            return new();
        }

        string json = File.ReadAllText(_manifestPath);
        return JsonSerializer.Deserialize<CustomPrefabManifest>(json, JsonOptions) ?? new();
    }

    public void RecordRegistration(CustomPrefabRegistration registration)
    {
        CustomPrefabManifest manifest = Load();
        CustomPrefabManifestEntry entry = new(
            registration.ProviderId,
            registration.SourcePrefabGuid,
            registration.GeneratedPrefabGuid,
            registration.GeneratedAssetGuid,
            registration.GeneratedAssetName,
            registration.ClientSyncRequired,
            registration.CleanupPolicy);

        manifest.Entries.RemoveAll(existing => existing.GeneratedPrefabGuid == registration.GeneratedPrefabGuid);
        manifest.Entries.Add(entry);
        Save(manifest);
    }

    public void Save(CustomPrefabManifest manifest)
    {
        string directory = Path.GetDirectoryName(_manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(_manifestPath, json);
    }

    static string GetDefaultConfigPath()
    {
        try
        {
            string configPath = Paths.ConfigPath;
            return string.IsNullOrWhiteSpace(configPath)
                ? Path.GetTempPath()
                : configPath;
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException)
        {
            return Path.GetTempPath();
        }
    }
}
