using Emberglass.Utilities;
using Stunlock.Localization;

namespace Emberglass.API.Client;
public static class OptionsManager
{
    static readonly HashSet<string> _activeCategories = [];
    public static IReadOnlyDictionary<LocalizationKey, List<IMenuEntry>> CategoryEntries => _categoryEntries;
    static readonly Dictionary<LocalizationKey, List<IMenuEntry>> _categoryEntries = [];
    public static IReadOnlyDictionary<string, MenuOption> Options => _options;
    static readonly Dictionary<string, MenuOption> _options = [];
    public static IReadOnlyDictionary<string, LocalizationKey> CategoryKeys => _categoryKeys;
    static readonly Dictionary<string, LocalizationKey> _categoryKeys = [];
    static readonly HashSet<string> _categoryHeaders = [];
    static int _nextOrder;
    /// <summary>
    /// Registers a toggle option.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="defaultValue">The default value for the toggle.</param>
    /// <returns>The created toggle option.</returns>
    public static Toggle AddToggle(string id, string name, string description, string category, bool defaultValue)
    {
        var toggle = new Toggle(id, name, description, category, defaultValue);
        if (TryRegisterOption(id, category, toggle))
        {
            var localizationKey = GetOrCreateCategoryKey(category);
            var order = GetNextOrder();
            _categoryEntries[localizationKey].Add(new ToggleEntry(toggle, order));
        }

        return toggle;
    }
    /// <summary>
    /// Registers a slider option.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="min">The minimum value for the slider.</param>
    /// <param name="max">The maximum value for the slider.</param>
    /// <param name="defaultVal">The default value for the slider.</param>
    /// <param name="decimals">The number of decimal places to display.</param>
    /// <param name="step">The slider step value.</param>
    /// <returns>The created slider option.</returns>
    public static Slider AddSlider(string id, string name, string description, string category, float min, float max, float defaultVal, int decimals = 0, float step = 0)
    {
        var slider = new Slider(id, name, description, category, min, max, defaultVal, decimals, step);
        if (TryRegisterOption(id, category, slider))
        {
            var localizationKey = GetOrCreateCategoryKey(category);
            var order = GetNextOrder();
            _categoryEntries[localizationKey].Add(new SliderEntry(slider, order));
        }

        return slider;
    }
    /// <summary>
    /// Registers a dropdown option.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="defaultIndex">The default selected index.</param>
    /// <param name="values">The dropdown values.</param>
    /// <returns>The created dropdown option.</returns>
    public static Dropdown AddDropdown(string id, string name, string description, string category, int defaultIndex, string[] values)
    {
        var dropdown = new Dropdown(id, name, description, category, defaultIndex, values);
        if (TryRegisterOption(id, category, dropdown))
        {
            var localizationKey = GetOrCreateCategoryKey(category);
            var order = GetNextOrder();
            _categoryEntries[localizationKey].Add(new DropdownEntry(dropdown, order));
        }

        return dropdown;
    }
    /// <summary>
    /// Adds a divider entry to the category menu.
    /// </summary>
    /// <param name="label">The divider label text.</param>
    /// <param name="category">The category identifier for the divider.</param>
    public static void AddDivider(string label, string category)
    {
        var localizationKey = GetOrCreateCategoryKey(category);
        var order = GetNextOrder();
        _categoryEntries[localizationKey].Add(new DividerEntry(label, category, order));
    }
    /// <summary>
    /// Registers a button entry in the options menu.
    /// </summary>
    /// <param name="id">The stable identifier for the button.</param>
    /// <param name="name">The display name for the button.</param>
    /// <param name="description">The display description for the button.</param>
    /// <param name="category">The category identifier for the button.</param>
    /// <param name="onClick">The action invoked when the button is clicked.</param>
    public static void AddButton(string id, string name, string description, string category, Action onClick)
    {
        var localizationKey = GetOrCreateCategoryKey(category);
        var order = GetNextOrder();
        var nameKey = LocalizationKeyManager.GetLocalizationKey(name);
        var descriptionKey = LocalizationKeyManager.GetLocalizationKey(description);

        _categoryEntries[localizationKey].Add(new ButtonEntry(id, nameKey, descriptionKey, category, order, onClick));
    }
    /// <summary>
    /// Registers a menu option and tracks it by identifier.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="option">The option instance.</param>
    /// <returns>True when the option was registered; otherwise, false.</returns>
    static bool TryRegisterOption(string id, string category, MenuOption option)
    {
        if (_options.ContainsKey(id))
        {
            return false;
        }

        _options[id] = option;
        _activeCategories.Add(category);
        return true;
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
    /// <summary>
    /// Attempts to load persisted options and applies them to registered options.
    /// </summary>
    internal static void TryLoadOptions()
    {
        var loaded = Persistence.LoadOptions();
        if (loaded == null)
        {
            return;
        }

        bool didMigrate = false;

        foreach (var (key, option) in loaded)
        {
            if (!_activeCategories.Contains(option.Category))
            {
                continue;
            }

            var resolvedKey = ResolveOptionKey(key, option, out var existing);
            if (existing == null)
            {
                continue;
            }

            existing.ApplySaved(option);
            if (!string.Equals(resolvedKey, key, StringComparison.Ordinal))
            {
                didMigrate = true;
            }
        }

        if (didMigrate)
        {
            Persistence.SaveOptions();
        }
    }
    /// <summary>
    /// Resolves a persisted option key to a registered option.
    /// </summary>
    /// <param name="key">The persisted option key.</param>
    /// <param name="option">The persisted option data.</param>
    /// <param name="existing">The resolved registered option.</param>
    /// <returns>The resolved key to use for migration checks.</returns>
    static string ResolveOptionKey(string key, MenuOption option, out MenuOption existing)
    {
        if (_options.TryGetValue(key, out existing))
        {
            return key;
        }

        if (!string.IsNullOrWhiteSpace(option.Id) && _options.TryGetValue(option.Id, out existing))
        {
            return option.Id;
        }

        foreach (var registered in _options.Values)
        {
            if (!string.Equals(registered.Name, key, StringComparison.Ordinal))
            {
                continue;
            }

            existing = registered;
            return registered.Id;
        }

        existing = null;
        return key;
    }
}
