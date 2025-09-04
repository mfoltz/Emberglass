using Emberglass.API.Shared;
using Stunlock.Localization;
using System.Text.Json.Serialization;
using UnityEngine;

namespace Emberglass.API.Client;

[Serializable]
public abstract class MenuOption
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }

    [JsonIgnore]
    public LocalizationKey NameKey;

    [JsonIgnore]
    public LocalizationKey DescKey;
    protected MenuOption() { }
    protected MenuOption(string name, string description, string category)
    {
        Name = name;
        Description = description;
        NameKey = LocalizationKeyManager.GetLocalizationKey(name);
        DescKey = LocalizationKeyManager.GetLocalizationKey(description);
        Category = category;
    }
    public abstract void ApplyDefault();
    public abstract void ApplySaved(MenuOption other);
}

[Serializable]
public abstract class MenuOption<T> : MenuOption
{
    public delegate void OptionChangedHandler<TValue>(TValue newValue);
    public virtual T Value { get; set; }
    public T DefaultValue { get; set; }

    public event OptionChangedHandler<T> OnOptionChangedHandler = delegate { };
    protected MenuOption() : base() { }
    protected MenuOption(string name, string description, string category, T defaultValue)
        : base(name, description, category)
    {
        Value = defaultValue;
        DefaultValue = defaultValue;
    }
    public virtual void SetValue(T value)
    {
        Value = value;
        OnOptionChangedHandler(value);
    }
    public void AddListener(OptionChangedHandler<T> listener) => OnOptionChangedHandler += listener;
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
            VWorld.Log.LogWarning($"[MenuOption] Type mismatch loading values - {other.Name}");
        }
    }
}

[Serializable]
public class Toggle : MenuOption<bool>
{
    public Toggle() : base() { }
    public Toggle(string name, string description, string category, bool defaultValue)
        : base(name, description, category, defaultValue) { }
    public override void ApplySaved(MenuOption other)
    {
        if (other is Toggle toggle)
        {
            SetValue(toggle.Value);
        }
    }
    public override void ApplyDefault() => SetValue(DefaultValue);
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
    public Slider() : base() { }
    public Slider(string name, string description, string category, float min, float max, float defaultValue, int decimals = default, float step = default)
        : base(name, description, category, Mathf.Clamp(defaultValue, min, max))
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
    public Dropdown() : base() { }
    public Dropdown(string name, string description, string category, int defaultIndex, string[] values)
        : base(name, description, category, defaultIndex)
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