using Emberglass.Utilities;
using ProjectM;
using Stunlock.Localization;
using System.Text;
using UnityEngine;

namespace Emberglass.API.Client;
public static class KeybindManager
{
    static readonly HashSet<string> _activeCategories = [];
    public static IReadOnlyDictionary<LocalizationKey, Dictionary<string, Keybinding>> Categories => _categories;
    static readonly Dictionary<LocalizationKey, Dictionary<string, Keybinding>> _categories = [];
    public static IReadOnlyDictionary<string, Keybinding> Keybinds => _keybinds;
    static readonly Dictionary<string, Keybinding> _keybinds = [];

    static readonly Dictionary<string, LocalizationKey> _categoryKeys = [];
    static readonly HashSet<string> _categoryHeaders = [];

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
    public static Keybinding AddKeybind(string name, string description, string category, KeyCode defaultKey)
    {
        if (_keybinds.TryGetValue(name, out var existing))
        {
            return existing;
        }

        if (!_categoryHeaders.Contains(category) && !_categoryKeys.TryGetValue(category, out var localizationKey))
        {
            localizationKey = LocalizationKeyManager.GetLocalizationKey(category);
            _categoryKeys[category] = localizationKey;
            _categoryHeaders.Add(category);
            _categories[localizationKey] = [];
        }
        else
        {
            localizationKey = _categoryKeys[category];
        }

        var keybinds = _categories[localizationKey];
        var keybind = new Keybinding(name, description, category, defaultKey);

        keybinds[name] = keybind;
        _keybinds[name] = keybind;
        _activeCategories.Add(category);

        return keybind;
    }
    public static void Rebind(Keybinding keybind, KeyCode newKey)
    {
        keybind.Primary = newKey;
        Persistence.SaveKeybinds();
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
        return _keyLiterals.TryGetValue(key, out var literal) ? literal : key.ToString();
    }
    static ulong Hash64(byte[] data)
    {
        ulong hash = HASH_LONG;

        foreach (var b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }
    static uint Hash32(byte[] data)
    {
        uint hash = HASH_INT;

        foreach (var b in data)
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

        foreach (var (key, keybind) in loaded)
        {
            if (!_activeCategories.Contains(keybind.Category))
            {
                continue;
            }

            if (!_categoryKeys.TryGetValue(keybind.Category, out var locKey))
            {
                locKey = LocalizationKeyManager.GetLocalizationKey(keybind.Category);
                _categoryKeys[keybind.Category] = locKey;
                _categoryHeaders.Add(keybind.Category);
                _categories[locKey] = [];
            }

            if (_categories.TryGetValue(_categoryKeys[keybind.Category], out var keybinds) &&
                keybinds.TryGetValue(keybind.Name, out var registered))
            {
                registered.ApplySaved(keybind);
            }
        }
    }
}
