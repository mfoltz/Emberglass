using BepInEx.Configuration;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Represents a typed config binding that builds settings snapshots.
/// </summary>
/// <typeparam name="TSettings">The settings type being constructed.</typeparam>
/// <typeparam name="TValue">The value type stored in the config entry.</typeparam>
public sealed class Binding<TSettings, TValue> : IBinding<TSettings>, IServerConfigChangeBinding
    where TSettings : class
{
    readonly string section;
    readonly string key;
    readonly string description;
    readonly TValue defaultValue;
    readonly Func<TSettings, TValue> getValue;
    readonly Func<TSettings, TValue, TSettings> assignValue;
    readonly Func<TValue, TValue> normalizeValue;
    readonly Func<TValue, TValue, bool> valuesEqual;
    bool isBound;
    ConfigEntry<TValue> configEntry;

    /// <summary>
    /// Initializes a new instance of the <see cref="Binding{TSettings, TValue}"/> class.
    /// </summary>
    /// <param name="section">The config section name.</param>
    /// <param name="key">The config key name.</param>
    /// <param name="defaultValue">The default value for the config entry.</param>
    /// <param name="description">The config entry description.</param>
    /// <param name="getValue">The accessor used to retrieve a value from a settings snapshot.</param>
    /// <param name="assignValue">The function that assigns the value into a settings snapshot.</param>
    /// <param name="normalizeValue">Optional normalization logic for the config value.</param>
    /// <param name="valuesEqual">Optional comparer for detecting value changes.</param>
    /// <param name="scope">The scope that owns the config entry.</param>
    /// <param name="reloadPolicy">The reload policy to apply to the config entry.</param>
    public Binding(
        string section,
        string key,
        TValue defaultValue,
        string description,
        Func<TSettings, TValue> getValue,
        Func<TSettings, TValue, TSettings> assignValue,
        Func<TValue, TValue> normalizeValue = null,
        Func<TValue, TValue, bool> valuesEqual = null,
        ConfigScope scope = ConfigScope.Shared,
        ReloadPolicy reloadPolicy = ReloadPolicy.OnChange)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            throw new ArgumentException("Section is required.", nameof(section));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        this.section = section;
        this.key = key;
        this.description = description ?? string.Empty;
        this.defaultValue = defaultValue;
        this.getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        this.assignValue = assignValue ?? throw new ArgumentNullException(nameof(assignValue));
        this.normalizeValue = normalizeValue ?? (value => value);
        this.valuesEqual = valuesEqual ?? EqualityComparer<TValue>.Default.Equals;
        Scope = scope;
        ReloadPolicy = reloadPolicy;
    }

    /// <inheritdoc />
    public string ChangedKey => $"{section}:{key}";

    /// <summary>
    /// Gets the scope that owns the config entry.
    /// </summary>
    public ConfigScope Scope { get; }

    /// <summary>
    /// Gets the reload policy associated with the binding.
    /// </summary>
    public ReloadPolicy ReloadPolicy { get; }

    /// <summary>
    /// Registers server-side change handling for this binding.
    /// </summary>
    void IServerConfigChangeBinding.RegisterServerChangeHandler()
        => ServerConfigChangeHandlers.RegisterBinding(this);

    /// <summary>
    /// Attempts to read the current config value.
    /// </summary>
    /// <param name="value">The current config value.</param>
    /// <returns><c>true</c> when the config entry is available; otherwise, <c>false</c>.</returns>
    public bool TryGetValue(out TValue value)
    {
        if (configEntry is null)
        {
            value = default!;
            return false;
        }

        value = configEntry.Value;
        return true;
    }

    /// <summary>
    /// Attempts to update the current config value.
    /// </summary>
    /// <param name="value">The new config value.</param>
    /// <returns><c>true</c> when the config entry is available; otherwise, <c>false</c>.</returns>
    public bool TrySetValue(TValue value)
    {
        if (configEntry is null)
        {
            return false;
        }

        configEntry.Value = value;
        return true;
    }

    /// <inheritdoc />
    public void Bind(ConfigFile configFile)
    {
        if (configFile is null)
        {
            throw new ArgumentNullException(nameof(configFile));
        }

        if (isBound)
        {
            throw new InvalidOperationException("The config entry is already bound.");
        }

        configEntry = configFile.Bind(section, key, defaultValue, new ConfigDescription(description));
        isBound = true;
    }

    /// <inheritdoc />
    public TSettings Apply(TSettings settings, TSettings oldSettings, ISet<string> changedKeys)
    {
        if (configEntry is null)
        {
            throw new InvalidOperationException("The config entry has not been bound yet.");
        }

        if (changedKeys is null)
        {
            throw new ArgumentNullException(nameof(changedKeys));
        }

        var normalizedValue = normalizeValue(configEntry.Value);

        if (!valuesEqual(configEntry.Value, normalizedValue))
        {
            configEntry.Value = normalizedValue;
        }

        if (oldSettings is not null)
        {
            var oldValue = getValue(oldSettings);

            if (!valuesEqual(oldValue, normalizedValue))
            {
                changedKeys.Add(ChangedKey);
            }
        }

        return assignValue(settings, normalizedValue);
    }
}
