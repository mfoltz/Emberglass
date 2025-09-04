using BepInEx;
using Emberglass.API.Client;
using Emberglass.API.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Emberglass.API.Client.KeybindManager;
using static Emberglass.API.Client.OptionsManager;

namespace Emberglass.Utilities;
internal static class Persistence
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new MenuOptionJsonConverter() }
    };

    static readonly string _directoryPath = Path.Join(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);

    const string KEYBINDS_KEY = "Keybinds";
    const string OPTIONS_KEY = "Options";
    // const string COMMANDS_KEY = "Commands";

    static readonly string _keybindsJson = Path.Combine(_directoryPath, $"{KEYBINDS_KEY}.json");
    static readonly string _settingsJson = Path.Combine(_directoryPath, $"{OPTIONS_KEY}.json");
    // static readonly string _commandsJson = Path.Combine(_directoryPath, $"{COMMANDS_KEY}.json");

    static readonly Dictionary<string, string> _filePaths = new()
    {
        {KEYBINDS_KEY, _keybindsJson },
        {OPTIONS_KEY, _settingsJson },
        // {COMMANDS_KEY, _commandsJson }
    };
    public static void SaveKeybinds() => SaveDictionary(Keybinds, KEYBINDS_KEY);
    public static void SaveOptions() => SaveDictionary(Options, OPTIONS_KEY);

    // public static void SaveCommands() => SaveDictionary(CommandQuips, COMMANDS_KEY);
    public static Dictionary<string, Keybinding> LoadKeybinds() => LoadDictionary<string, Keybinding>(KEYBINDS_KEY);
    public static Dictionary<string, MenuOption> LoadOptions() => LoadDictionary<string, MenuOption>(OPTIONS_KEY);

    // public static Dictionary<int, Command> LoadCommands() => LoadDictionary<int, Command>(COMMANDS_KEY);
    static Dictionary<T, U> LoadDictionary<T, U>(string fileKey)
        where T : notnull
        where U : class
    {
        if (!_filePaths.TryGetValue(fileKey, out string filePath))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                Directory.CreateDirectory(_directoryPath);
            }

            if (!File.Exists(filePath))
            {
                File.Create(filePath).Dispose();

                return [];
            }

            VWorld.Log.LogWarning($"Loading {fileKey}...");

            string fileText = File.ReadAllText(filePath);
            return fileText.IsNullOrWhiteSpace()
                ? []
                : JsonSerializer.Deserialize<Dictionary<T, U>>(fileText, _jsonOptions);
        }
        catch (IOException ex)
        {
            VWorld.Log.LogWarning($"Failed to deserialize {fileKey} contents: {ex.Message}");
            return null;
        }
    }
    static void SaveDictionary<T, U>(IReadOnlyDictionary<T, U> fileData, string fileKey)
    {
        if (!_filePaths.TryGetValue(fileKey, out string filePath))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                Directory.CreateDirectory(_directoryPath);
            }

            string fileText = JsonSerializer.Serialize(fileData, _jsonOptions);
            File.WriteAllText(filePath, fileText);
        }
        catch (IOException ex)
        {
            VWorld.Log.LogWarning($"Failed to serialize {fileKey} contents: {ex.Message}");
        }
    }

}
internal class MenuOptionJsonConverter : JsonConverter<MenuOption>
{
    const string TYPE_PROPERTY = "OptionType";
    public override MenuOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty(TYPE_PROPERTY, out var typeProp))
        {
            throw new JsonException($"Missing '{TYPE_PROPERTY}' in MenuOption JSON converter!");
        }

        var typeName = typeProp.GetString();

        return typeName switch
        {
            nameof(Toggle) => JsonSerializer.Deserialize<Toggle>(root.GetRawText(), options),
            nameof(Slider) => JsonSerializer.Deserialize<Slider>(root.GetRawText(), options),
            nameof(Dropdown) => JsonSerializer.Deserialize<Dropdown>(root.GetRawText(), options),
            _ => throw new JsonException($"Unknown MenuOption type '{typeName}'")
        };
    }
    public override void Write(Utf8JsonWriter writer, MenuOption value, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonSerializer.SerializeToDocument(value, value.GetType(), options);
        var jsonObj = JsonNode.Parse(jsonDoc.RootElement.GetRawText())!.AsObject();

        jsonObj[TYPE_PROPERTY] = value.GetType().Name;
        jsonObj.WriteTo(writer, options);
    }
}