using Emberglass.API.Shared;
using Emberglass.Utilities;
using Stunlock.Localization;

namespace Emberglass.API.Client;
public static class OptionsManager
{
    public enum OptionItemType
    {
        Toggle,
        Slider,
        Dropdown,
        Divider
    }
    public class OptionEntry(OptionItemType type, string key)
    {
        public OptionItemType Type { get; } = type;
        public string Key { get; } = key;
    }

    static readonly HashSet<string> _activeCategories = [];
    public static IReadOnlyDictionary<LocalizationKey, List<OptionEntry>> CategoryEntries => _categoryEntries;
    static readonly Dictionary<LocalizationKey, List<OptionEntry>> _categoryEntries = [];
    public static IReadOnlyDictionary<string, MenuOption> Options => _options;
    static readonly Dictionary<string, MenuOption> _options = [];
    public static IReadOnlyDictionary<string, LocalizationKey> CategoryKeys => _categoryKeys;
    static readonly Dictionary<string, LocalizationKey> _categoryKeys = [];
    static readonly HashSet<string> _categoryHeaders = [];
    public static IReadOnlyList<OptionEntry> OrderedEntries => _orderedEntries;
    static readonly List<OptionEntry> _orderedEntries = [];
    public static Toggle AddToggle(string name, string description, string category, bool defaultValue)
    {
        var toggle = new Toggle(name, description, category, defaultValue);
        RegisterOption(name, category, toggle, OptionItemType.Toggle);
        return toggle;
    }
    public static Slider AddSlider(string name, string description, string category, float min, float max, float defaultVal, int decimals = 0, float step = 0)
    {
        var slider = new Slider(name, description, category, min, max, defaultVal, decimals, step);
        RegisterOption(name, category, slider, OptionItemType.Slider);
        return slider;
    }
    public static Dropdown AddDropdown(string name, string description, string category, int defaultIndex, string[] values)
    {
        var dropdown = new Dropdown(name, description, category, defaultIndex, values);
        RegisterOption(name, category, dropdown, OptionItemType.Dropdown);
        return dropdown;
    }
    public static void AddDivider(string label, string category)
    {
        _orderedEntries.Add(new(OptionItemType.Divider, label));
    }
    static void RegisterOption(string name, string category, MenuOption option, OptionItemType type)
    {
        if (_options.ContainsKey(name))
        {
            return;
        }

        if (!_categoryHeaders.Contains(category) && !_categoryKeys.TryGetValue(category, out var localizationKey))
        {
            localizationKey = LocalizationKeyManager.GetLocalizationKey(category);
            _categoryKeys[category] = localizationKey;
            _categoryHeaders.Add(category);
            _categoryEntries[localizationKey] = [];
        }
        else
        {
            localizationKey = _categoryKeys[category];
        }

        _options[name] = option;
        _categoryEntries[localizationKey].Add(new OptionEntry(type, name));
        _orderedEntries.Add(new OptionEntry(type, name));
        _activeCategories.Add(category);
    }
    public static bool TryGetOption(OptionEntry entry, out MenuOption option)
    {
        option = null;

        if (!_options.TryGetValue(entry.Key, out var raw))
        {
            VWorld.Log.LogWarning($"[OptionsManager] Key not found: {entry.Key}");
            return false;
        }

        var expectedType = GetValueType(entry.Type);
        if (expectedType == null)
        {
            VWorld.Log.LogWarning($"[OptionsManager] Unsupported type for: {entry.Key} ({entry.Type})");
            return false;
        }

        var menuOptionType = typeof(MenuOption<>).MakeGenericType(expectedType);
        if (!menuOptionType.IsInstanceOfType(raw))
        {
            VWorld.Log.LogWarning($"[OptionsManager] Type mismatch: {entry.Key} (expected: {menuOptionType.Name}, actual: {raw.GetType().Name})");
            return false;
        }

        option = raw;
        return true;
    }
    static Type GetValueType(OptionItemType type) => type switch
    {
        OptionItemType.Toggle => typeof(bool),
        OptionItemType.Slider => typeof(float),
        OptionItemType.Dropdown => typeof(int),
        _ => null
    };
    internal static void TryLoadOptions()
    {
        var loaded = Persistence.LoadOptions();
        if (loaded == null)
        {
            return;
        }

        foreach (var (key, option) in loaded)
        {
            if (!_activeCategories.Contains(option.Category))
            {
                continue;
            }

            if (_options.TryGetValue(key, out var existing))
            {
                existing.ApplySaved(option);
            }

            if (!_categoryKeys.TryGetValue(option.Category, out var locKey))
            {
                locKey = LocalizationKeyManager.GetLocalizationKey(option.Category);
                _categoryKeys[option.Category] = locKey;
                _categoryHeaders.Add(option.Category);
                _categoryEntries[locKey] = [];
            }

            var type = option switch
            {
                Toggle => OptionItemType.Toggle,
                Slider => OptionItemType.Slider,
                Dropdown => OptionItemType.Dropdown,
                _ => throw new NotSupportedException($"Unsupported option type: {option.GetType().Name}")
            };

            _categoryEntries[locKey].Add(new OptionEntry(type, key));
        }
    }
}