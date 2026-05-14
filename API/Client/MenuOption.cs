using Emberglass.API.Shared;
using Emberglass.Utilities;
using Stunlock.Localization;
using System.Text.Json.Serialization;
using UnityEngine;

namespace Emberglass.API.Client;

[Serializable]
public abstract class MenuOption
{
    /// <summary>
    /// Gets or sets the stable identifier for this option.
    /// </summary>
    public string Id { get; set; }
    /// <summary>
    /// Gets or sets the display name for this option.
    /// </summary>
    public string Name { get; set; }
    /// <summary>
    /// Gets or sets the display description for this option.
    /// </summary>
    public string Description { get; set; }
    /// <summary>
    /// Gets or sets the category identifier for this option.
    /// </summary>
    public string Category { get; set; }
    /// <summary>
    /// Gets or sets the control scope metadata for the option.
    /// </summary>
    public MenuOptionControlScope ControlScope { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether the option requires a reload to apply.
    /// </summary>
    public bool RequiresReload { get; set; }

    [JsonIgnore]
    public LocalizationKey NameKey;

    [JsonIgnore]
    public LocalizationKey DescKey;
    protected MenuOption() { }
    /// <summary>
    /// Initializes a new instance of the <see cref="MenuOption"/> class.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    protected MenuOption(string id, string name, string description, string category)
    {
        Id = id;
        Name = name;
        Description = description;
        NameKey = LocalizationKeyManager.GetLocalizationKey(name);
        DescKey = LocalizationKeyManager.GetLocalizationKey(description);
        Category = category;
    }
    /// <summary>
    /// Gets the description localization key with any status labels appended.
    /// </summary>
    /// <returns>The localization key for the combined description text.</returns>
    public LocalizationKey GetDisplayDescriptionKey()
    {
        string labelText = MenuOptionLabelBuilder.BuildLabelText(ControlScope, RequiresReload);
        if (string.IsNullOrWhiteSpace(labelText))
        {
            return DescKey;
        }

        string combinedText = string.IsNullOrWhiteSpace(Description)
            ? labelText
            : $"{Description}\n{labelText}";

        return LocalizationKeyManager.GetLocalizationKey(combinedText);
    }
    public abstract void ApplyDefault();
    public abstract void ApplySaved(MenuOption other);
}

[Serializable]
public abstract class MenuOption<T> : MenuOption
{
    public delegate void ConfigChangeHandler<TValue>(TValue newValue);
    public event ConfigChangeHandler<T> OnConfigChange = delegate { };
    public virtual T Value { get; set; }
    public T DefaultValue { get; set; }
    protected MenuOption() { }
    /// <summary>
    /// Initializes a new instance of the <see cref="MenuOption{T}"/> class.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="defaultValue">The default value for the option.</param>
    protected MenuOption(string id, string name, string description, string category, T defaultValue)
        : base(id, name, description, category)
    {
        Value = defaultValue;
        DefaultValue = defaultValue;
    }
    public virtual void SetValue(T value)
    {
        Value = value;
        OnConfigChange(value);
    }

    public Action<T> OnValueChange() // previous actions were static
    {
        return value =>
        {
            SetValue(value);
            Persistence.SaveOptions();
        };
    }

    public void AddListener(ConfigChangeHandler<T> listener)
         => OnConfigChange += listener;
    public override void ApplyDefault() => SetValue(DefaultValue);
    public override void ApplySaved(MenuOption other)
    {
        if (other is MenuOption<T> typed)
        {
            VWorld.Log.LogWarning($"[MenuOption] Applying saved values - {other.Name}");
            SetValue(typed.Value);
        }
        else
        {
            VWorld.Log.LogWarning($"[MenuOption] Type mismatch loading saved values - {other.Name}");
        }
    }
}

[Serializable]
public class Toggle : MenuOption<bool>
{
    public Toggle() { }
    /// <summary>
    /// Initializes a new instance of the <see cref="Toggle"/> class.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="defaultValue">The default value for the option.</param>
    public Toggle(string id, string name, string description, string category, bool defaultValue)
        : base(id, name, description, category, defaultValue) { }
    public override void ApplySaved(MenuOption other)
    {
        if (other is Toggle toggle)
        {
            SetValue(toggle.Value);
        }
    }
    public override void ApplyDefault()
        => SetValue(DefaultValue);
}

[Serializable]
public class Slider : MenuOption<float>
{
    public float MinValue { get; set; }
    public float MaxValue { get; set; }

    [JsonIgnore]
    public int Decimals { get; set; }

    [JsonIgnore]
    public float StepValue { get; set; }
    public override float Value
    {
        get => Mathf.Clamp(base.Value, MinValue, MaxValue);
        set => base.Value = Mathf.Clamp(value, MinValue, MaxValue);
    }
    public Slider() { }
    /// <summary>
    /// Initializes a new instance of the <see cref="Slider"/> class.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="min">The minimum value for the slider.</param>
    /// <param name="max">The maximum value for the slider.</param>
    /// <param name="defaultValue">The default value for the slider.</param>
    /// <param name="decimals">The number of decimal places to display.</param>
    /// <param name="step">The slider step value.</param>
    public Slider(string id, string name, string description, string category, float min, float max, float defaultValue, int decimals = default, float step = default)
        : base(id, name, description, category, Mathf.Clamp(defaultValue, min, max))
    {
        MinValue = min;
        MaxValue = max;
        Decimals = decimals;
        StepValue = step;
        Value = defaultValue;
    }
    public override void SetValue(float value)
    {
        base.SetValue(Mathf.Clamp(value, MinValue, MaxValue));
    }
    public override void ApplySaved(MenuOption other)
    {
        if (other is Slider slider)
        {
            SetValue(slider.Value);
        }
    }
    public override void ApplyDefault()
    {
        SetValue(DefaultValue);
    }
}

[Serializable]
public class Dropdown : MenuOption<int>
{
    public List<string> Values { get; set; } = [];
    public Dropdown() { }
    /// <summary>
    /// Initializes a new instance of the <see cref="Dropdown"/> class.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="defaultIndex">The default selected index.</param>
    /// <param name="values">The dropdown values.</param>
    public Dropdown(string id, string name, string description, string category, int defaultIndex, string[] values)
        : base(id, name, description, category, defaultIndex)
    {
        Values = values?.ToList() ?? [];
    }
    public T GetEnumValue<T>(T fallback = default)
    {
        try { return (T)Enum.Parse(typeof(T), Values[Value]); }
        catch { return fallback; }
    }
    public override void ApplySaved(MenuOption other)
    {
        if (other is Dropdown dropdown)
        {
            int index = Mathf.Clamp(dropdown.Value, 0, Values.Count - 1);
            SetValue(index);
        }
    }
    public override void ApplyDefault()
    {
        SetValue(Mathf.Clamp(DefaultValue, 0, Values.Count - 1));
    }
}
