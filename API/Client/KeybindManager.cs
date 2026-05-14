using System.Text;
using Emberglass.Utilities;
using ProjectM;
using Stunlock.Localization;
using UnityEngine;

namespace Emberglass.API.Client;
public static class KeybindManager
{
    static readonly HashSet<string> _activeCategories = [];
    public static IReadOnlyDictionary<LocalizationKey, List<IKeybindMenuEntry>> Categories => _categoryEntries;
    static readonly Dictionary<LocalizationKey, List<IKeybindMenuEntry>> _categoryEntries = [];
    public static IReadOnlyDictionary<string, Keybinding> Keybinds => _keybinds;
    static readonly Dictionary<string, Keybinding> _keybinds = [];

    static readonly Dictionary<string, LocalizationKey> _categoryKeys = [];
    static readonly HashSet<string> _categoryHeaders = [];
    static int _nextOrder;

    const ulong HASH_LONG = 14695981039346656037UL;
    const uint HASH_INT = 2166136261U;

    static readonly Dictionary<KeyCode, string> _keyLiterals = new()
    {
        { KeyCode.Space, " " },
        { KeyCode.BackQuote, "`" },
        { KeyCode.Minus, "-" },
        { KeyCode.Equals, "=" },
        { KeyCode.LeftBracket, "[" },
        { KeyCode.RightBracket, "]" },
        { KeyCode.Backslash, "\\" },
        { KeyCode.Semicolon, ";" },
        { KeyCode.Quote, "'" },
        { KeyCode.Comma, "," },
        { KeyCode.Period, "." },
        { KeyCode.Slash, "/" },
        { KeyCode.Alpha0, "0" },
        { KeyCode.Alpha1, "1" },
        { KeyCode.Alpha2, "2" },
        { KeyCode.Alpha3, "3" },
        { KeyCode.Alpha4, "4" },
        { KeyCode.Alpha5, "5" },
        { KeyCode.Alpha6, "6" },
        { KeyCode.Alpha7, "7" },
        { KeyCode.Alpha8, "8" },
        { KeyCode.Alpha9, "9" },
        { KeyCode.A, "A" },
        { KeyCode.B, "B" },
        { KeyCode.C, "C" },
        { KeyCode.D, "D" },
        { KeyCode.E, "E" },
        { KeyCode.F, "F" },
        { KeyCode.G, "G" },
        { KeyCode.H, "H" },
        { KeyCode.I, "I" },
        { KeyCode.J, "J" },
        { KeyCode.K, "K" },
        { KeyCode.L, "L" },
        { KeyCode.M, "M" },
        { KeyCode.N, "N" },
        { KeyCode.O, "O" },
        { KeyCode.P, "P" },
        { KeyCode.Q, "Q" },
        { KeyCode.R, "R" },
        { KeyCode.S, "S" },
        { KeyCode.T, "T" },
        { KeyCode.U, "U" },
        { KeyCode.V, "V" },
        { KeyCode.W, "W" },
        { KeyCode.X, "X" },
        { KeyCode.Y, "Y" },
        { KeyCode.Z, "Z" },
        { KeyCode.UpArrow, "↑" },
        { KeyCode.DownArrow, "↓" },
        { KeyCode.LeftArrow, "←" },
        { KeyCode.RightArrow, "→" }
    };
    /// <summary>
    /// Registers a keybind and returns the created binding.
    /// </summary>
    /// <param name="name">The display name for the keybind.</param>
    /// <param name="description">The display description for the keybind.</param>
    /// <param name="category">The category identifier for the keybind.</param>
    /// <param name="defaultKey">The default keyboard key.</param>
    /// <returns>The registered keybinding.</returns>
    public static Keybinding AddKeybind(string name, string description, string category, KeyCode defaultKey)
    {
        if (_keybinds.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var localizationKey = GetOrCreateCategoryKey(category);
        var keybind = new Keybinding(name, description, category, defaultKey);
        var order = GetNextOrder();

        _categoryEntries[localizationKey].Add(new KeybindEntry(keybind, order));
        _keybinds[name] = keybind;
        _activeCategories.Add(category);

        return keybind;
    }
    /// <summary>
    /// Adds a divider entry to the keybind menu without registering a persisted binding.
    /// </summary>
    /// <param name="label">The divider label text.</param>
    /// <param name="category">The category identifier for the divider.</param>
    public static void AddDivider(string label, string category)
    {
        var localizationKey = GetOrCreateCategoryKey(category);
        var order = GetNextOrder();

        _categoryEntries[localizationKey].Add(new KeybindDividerEntry(label, category, order));
    }
    public static void Rebind(Keybinding keybind, KeyCode newKey)
    {
        keybind.Primary = newKey;
        Persistence.SaveKeybinds();
    }
    /// <summary>
    /// Returns the next display order index for menu entries.
    /// </summary>
    /// <returns>The next order index to assign.</returns>
    static int GetNextOrder()
    {
        return _nextOrder++;
    }
    /// <summary>
    /// Ensures the category is registered and returns its localization key.
    /// </summary>
    /// <param name="category">The category identifier to register.</param>
    /// <returns>The localization key for the category.</returns>
    static LocalizationKey GetOrCreateCategoryKey(string category)
    {
        if (_categoryKeys.TryGetValue(category, out var localizationKey))
        {
            return localizationKey;
        }

        localizationKey = LocalizationKeyManager.GetLocalizationKey(category);
        _categoryKeys[category] = localizationKey;
        _categoryHeaders.Add(category);
        _categoryEntries[localizationKey] = [];
        return localizationKey;
    }
    public static ButtonInputAction ComputeInputFlag(string descriptionId)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(descriptionId);
        ulong num = Hash64(bytes);
        bool flag = false;

        do
        {
            foreach (ButtonInputAction buttonInputAction in Enum.GetValues<ButtonInputAction>())
            {
                if (num == (ulong)buttonInputAction)
                {
                    flag = true;
                    num--;
                }
            }
        } while (flag);

        return (ButtonInputAction)num;
    }
    public static int ComputeAssetGuid(string descriptionId)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(descriptionId);
        return (int)Hash32(bytes);
    }
    public static string GetLiteral(KeyCode key)
    {
        return _keyLiterals.TryGetValue(key, out string literal) ? literal : key.ToString();
    }
    static ulong Hash64(byte[] data)
    {
        ulong hash = HASH_LONG;

        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }
    static uint Hash32(byte[] data)
    {
        uint hash = HASH_INT;

        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 16777619U;
        }

        return hash;
    }
    internal static void TryLoadKeybinds()
    {
        var loaded = Persistence.LoadKeybinds();
        if (loaded == null)
        {
            return;
        }

        var didMigrate = false;

        foreach (var (key, keybind) in loaded)
        {
            if (!_activeCategories.Contains(keybind.Category))
            {
                continue;
            }

            if (TryResolveRegisteredKeybind(key, keybind, out var registered, out var migrated))
            {
                registered.ApplySaved(keybind);
                didMigrate |= migrated;
            }
        }

        if (didMigrate)
        {
            Persistence.SaveKeybinds();
        }
    }
    /// <summary>
    /// Resolves a registered keybind using the saved data and reports whether the lookup implies a migration.
    /// </summary>
    /// <param name="savedKey">The key name used in persistence storage.</param>
    /// <param name="savedKeybind">The saved keybind payload.</param>
    /// <param name="registered">The resolved registered keybind, if found.</param>
    /// <param name="didMigrate">Whether the resolved lookup implies the saved key should be normalized.</param>
    /// <returns><c>true</c> when a registered keybind is found; otherwise <c>false</c>.</returns>
    static bool TryResolveRegisteredKeybind(
        string savedKey,
        Keybinding savedKeybind,
        out Keybinding registered,
        out bool didMigrate)
    {
        registered = null;
        didMigrate = false;

        if (savedKeybind == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(savedKeybind.Name) &&
            _keybinds.TryGetValue(savedKeybind.Name, out registered))
        {
            didMigrate = !string.Equals(savedKey, savedKeybind.Name, StringComparison.Ordinal);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(savedKey) &&
            _keybinds.TryGetValue(savedKey, out registered))
        {
            return true;
        }

        return false;
    }
}
