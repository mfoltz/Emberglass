using Emberglass.API.Shared;
using Emberglass.Patches.Client;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ProjectM.UI;
using Stunlock.Localization;
using StunShared.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Emberglass.API.Client;

/// <summary>
/// Represents a menu entry that can build its UI representation.
/// </summary>
public interface IMenuEntry
{
    /// <summary>
    /// Gets the category identifier for this entry.
    /// </summary>
    string Category { get; }
    /// <summary>
    /// Gets the display order for this entry.
    /// </summary>
    int Order { get; }
    /// <summary>
    /// Builds the entry UI in the provided panel.
    /// </summary>
    /// <param name="panel">The options panel hosting the entry UI.</param>
    void BuildUI(OptionsPanel_Interface panel);
}

/// <summary>
/// Initializes a new instance of the <see cref="ToggleEntry"/> class.
/// </summary>
/// <param name="toggle">The registered toggle option.</param>
/// <param name="order">The display order for the entry.</param>
internal sealed class ToggleEntry(Toggle toggle, int order) : IMenuEntry
{
    readonly Toggle toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));

    /// <inheritdoc />
    public string Category { get; } = toggle.Category;
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public void BuildUI(OptionsPanel_Interface panel)
    {
        var toggleEntry = UIHelper.InstantiatePrefabUnderAnchor(panel.CheckboxPrefab, panel.ContentNode);
        LocalizationKey descriptionKey = toggle.GetDisplayDescriptionKey();

        toggleEntry.Initialize(
            toggle.NameKey,
            new Il2CppSystem.Nullable_Unboxed<LocalizationKey>(descriptionKey),
            toggle.DefaultValue,
            toggle.Value,
            toggle.OnValueChange()
        );

        SettingsEntryBase toggleBase = toggleEntry;
        panel.EntriesSelectionGroup.AddEntry(ref toggleBase, true);
    }
}

/// <summary>
/// Initializes a new instance of the <see cref="SliderEntry"/> class.
/// </summary>
/// <param name="slider">The registered slider option.</param>
/// <param name="order">The display order for the entry.</param>
internal sealed class SliderEntry(Slider slider, int order) : IMenuEntry
{
    readonly Slider slider = slider ?? throw new ArgumentNullException(nameof(slider));

    /// <inheritdoc />
    public string Category { get; } = slider.Category;
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public void BuildUI(OptionsPanel_Interface panel)
    {
        var sliderEntry = UIHelper.InstantiatePrefabUnderAnchor(panel.SliderPrefab, panel.ContentNode);
        LocalizationKey descriptionKey = slider.GetDisplayDescriptionKey();

        sliderEntry.Initialize(
            slider.NameKey,
            new Il2CppSystem.Nullable_Unboxed<LocalizationKey>(descriptionKey),
            slider.MinValue,
            slider.MaxValue,
            slider.DefaultValue,
            slider.Value,
            slider.Decimals,
            slider.Decimals == 0,
            slider.OnValueChange(),
            fixedStepValue: slider.StepValue
        );

        SettingsEntryBase sliderBase = sliderEntry;
        panel.EntriesSelectionGroup.AddEntry(ref sliderBase, true);
    }
}

/// <summary>
/// Initializes a new instance of the <see cref="DropdownEntry"/> class.
/// </summary>
/// <param name="dropdown">The registered dropdown option.</param>
/// <param name="order">The display order for the entry.</param>
internal sealed class DropdownEntry(Dropdown dropdown, int order) : IMenuEntry
{
    readonly Dropdown dropdown = dropdown ?? throw new ArgumentNullException(nameof(dropdown));

    /// <inheritdoc />
    public string Category { get; } = dropdown.Category;
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public void BuildUI(OptionsPanel_Interface panel)
    {
        var dropdownEntry = UIHelper.InstantiatePrefabUnderAnchor(panel.DropdownPrefab, panel.ContentNode);
        Il2CppSystem.Collections.Generic.List<string> dropdownOptions = new(dropdown.Values.Count);
        LocalizationKey descriptionKey = dropdown.GetDisplayDescriptionKey();

        foreach (string value in dropdown.Values)
        {
            dropdownOptions.Add(value);
        }

        dropdownEntry.Initialize(
            dropdown.NameKey,
            new Il2CppSystem.Nullable_Unboxed<LocalizationKey>(descriptionKey),
            new Il2CppReferenceArray<LocalizedKeyValue>([]),
            dropdownOptions,
            dropdown.DefaultValue,
            dropdown.Value,
            dropdown.OnValueChange()
        );

        SettingsEntryBase dropdownBase = dropdownEntry;
        panel.EntriesSelectionGroup.AddEntry(ref dropdownBase);
    }
}

/// <summary>
/// Initializes a new instance of the <see cref="DividerEntry"/> class.
/// </summary>
/// <param name="dividerText">The divider label.</param>
/// <param name="category">The category identifier for the divider.</param>
/// <param name="order">The display order for the entry.</param>
internal sealed class DividerEntry(string dividerText, string category, int order) : IMenuEntry
{
    const float DividerHeight = 28f;
    const float DividerFontSize = 20f;
    static readonly Color DividerColor = new(0.12f, 0.15f, 0.2f, 0.15f);
    readonly string dividerText = dividerText ?? throw new ArgumentNullException(nameof(dividerText));

    /// <inheritdoc />
    public string Category { get; } = category ?? throw new ArgumentNullException(nameof(category));
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public void BuildUI(OptionsPanel_Interface panel)
    {
        GameObject dividerGameObject = new("Divider");

        RectTransform dividerTransform = dividerGameObject.AddComponent<RectTransform>();
        dividerTransform.SetParent(panel.ContentNode);
        dividerTransform.localScale = Vector3.one;
        dividerTransform.sizeDelta = new Vector2(0f, DividerHeight);

        Image dividerImage = dividerGameObject.AddComponent<Image>();
        dividerImage.color = DividerColor;

        LayoutElement dividerLayout = dividerGameObject.AddComponent<LayoutElement>();
        dividerLayout.preferredHeight = DividerHeight;

        GameObject dividerTextGameObject = new("Text");
        RectTransform dividerTextTransform = dividerTextGameObject.AddComponent<RectTransform>();
        dividerTextTransform.SetParent(dividerGameObject.transform);
        dividerTextTransform.localScale = Vector3.one;

        TextMeshProUGUI textMeshDivider = dividerTextGameObject.AddComponent<TextMeshProUGUI>();
        Il2CppArrayBase<TextMeshProUGUI> textMeshArray = panel.ContentNode.GetComponentsInChildren<TextMeshProUGUI>();
        TMP_FontAsset fontAsset = textMeshArray.FirstOrDefault()?.font ?? TMP_Settings.defaultFontAsset;

        textMeshDivider.alignment = TextAlignmentOptions.Center;
        textMeshDivider.fontStyle = FontStyles.SmallCaps;
        textMeshDivider.fontSize = DividerFontSize;

        if (fontAsset != null)
        {
            textMeshDivider.font = fontAsset;
        }

        textMeshDivider.SetText(dividerText);
        dividerGameObject.SetActive(true);
    }
}

/// <summary>
/// Initializes a new instance of the <see cref="ButtonEntry"/> class.
/// </summary>
/// <param name="id">The stable identifier for the button.</param>
/// <param name="nameKey">The localization key for the button name.</param>
/// <param name="descKey">The localization key for the button description.</param>
/// <param name="category">The category identifier for the entry.</param>
/// <param name="order">The display order for the entry.</param>
/// <param name="onClick">The action invoked when the button is clicked.</param>
internal sealed class ButtonEntry(string id, LocalizationKey nameKey, LocalizationKey descKey, string category, int order, Action onClick) : IMenuEntry
{
    readonly Action onClick = onClick ?? throw new ArgumentNullException(nameof(onClick));

    /// <summary>
    /// Gets the identifier for diagnostic purposes.
    /// </summary>
    public string Id { get; } = id;
    /// <summary>
    /// Gets the localization key for the button name.
    /// </summary>
    public LocalizationKey NameKey { get; } = nameKey;
    /// <summary>
    /// Gets the localization key for the button description.
    /// </summary>
    public LocalizationKey DescKey { get; } = descKey;
    /// <inheritdoc />
    public string Category { get; } = category ?? throw new ArgumentNullException(nameof(category));
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public void BuildUI(OptionsPanel_Interface panel)
    {
        SettingsEntry_Button buttonPrefab = OptionsMenuPatches.ResolveButtonPrefab(panel);
        if (buttonPrefab == null)
        {
            VWorld.Log.LogWarning($"ButtonEntry '{Id}': Button prefab is null!");
            return;
        }

        SettingsEntry_Button buttonEntry = UIHelper.InstantiatePrefabUnderAnchor(buttonPrefab, panel.ContentNode);

        buttonEntry.Initialize(
            NameKey,
            new Il2CppSystem.Nullable_Unboxed<LocalizationKey>(DescKey),
            new Il2CppSystem.Nullable_Unboxed<LocalizationKey>(LocalizationKey.Empty),
            new Il2CppSystem.Nullable_Unboxed<LocalizationKey>(NameKey),
            onClick
        );

        SettingsEntryBase buttonBase = buttonEntry;
        panel.EntriesSelectionGroup.AddEntry(ref buttonBase, true);
    }
}
