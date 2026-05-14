using System.Collections;
using Emberglass.API.Shared;
using ProjectM;
using ProjectM.UI;
using StunShared.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Emberglass.API.Client;

/// <summary>
/// Represents a keybind menu entry that can build its UI representation.
/// </summary>
public interface IKeybindMenuEntry
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
    /// Builds the entry UI in the provided rebinding menu.
    /// </summary>
    /// <param name="panel">The rebinding menu hosting the entry UI.</param>
    void BuildUI(RebindingMenu panel);
}

internal sealed class KeybindEntry : IKeybindMenuEntry
{
    readonly Keybinding keybind;
    readonly int order;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindEntry"/> class.
    /// </summary>
    /// <param name="keybind">The registered keybinding.</param>
    /// <param name="order">The display order for the entry.</param>
    public KeybindEntry(Keybinding keybind, int order)
    {
        this.keybind = keybind ?? throw new ArgumentNullException(nameof(keybind));
        Category = keybind.Category;
        this.order = order;
    }

    /// <inheritdoc />
    public string Category { get; }
    /// <inheritdoc />
    public int Order => order;

    /// <inheritdoc />
    public void BuildUI(RebindingMenu panel)
    {
        if (panel == null)
        {
            throw new ArgumentNullException(nameof(panel));
        }

        SettingsEntry_Binding settingsEntryBinding = UIHelper.InstantiatePrefabUnderAnchor(panel.ControlsInputEntryPrefab, panel.ContentNode);

        settingsEntryBinding.Initialize(
            ControllerType.KeyboardAndMouse,
            keybind.InputFlag,
            AnalogInputAction.None,
            true,
            false,
            true,
            onClick: (Il2CppSystem.Action<SettingsEntry_Binding, bool, ButtonInputAction, AnalogInputAction, bool>)panel.OnEntryButtonClicked,
            onClear: (Il2CppSystem.Action<SettingsEntry_Binding, ButtonInputAction>)panel.OnEntryCleared,
            true
        );

        settingsEntryBinding.SetInputInfo(keybind.NameKey, keybind.DescriptionKey);
        settingsEntryBinding.SetPrimary(keybind.PrimaryName);

        settingsEntryBinding.PrimaryButton.onClick.AddListener((UnityAction)(() => KeybindMenuEntryHelpers.RefreshKeybind(panel, settingsEntryBinding, keybind).Run()));
        settingsEntryBinding.SecondaryButton.gameObject.SetActive(false);

        SettingsEntryBase settingsEntryBase = settingsEntryBinding;
        panel.EntriesSelectionGroup.AddEntry(ref settingsEntryBase);
    }

}

/// <summary>
/// Provides shared helpers for keybind menu entries.
/// </summary>
internal static class KeybindMenuEntryHelpers
{
    /// <summary>
    /// Starts the rebinding flow for the provided keybinding entry.
    /// </summary>
    /// <param name="panel">The rebinding menu hosting the entry.</param>
    /// <param name="binding">The UI binding entry to update.</param>
    /// <param name="keybind">The keybinding model to update.</param>
    /// <returns>The coroutine enumerator for the rebinding flow.</returns>
    internal static IEnumerator RefreshKeybind(RebindingMenu panel, SettingsEntry_Binding binding, Keybinding keybind)
    {
        KeyCode newKey = KeyCode.None;
        panel.OnEntryButtonClicked(binding, true, ButtonInputAction.None, AnalogInputAction.None, true);

        while (true)
        {
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(key))
                {
                    if (key == KeyCode.Escape)
                    {
                        panel.OnRebindingCancel(false, false);

                        yield break;
                    }

                    if (key == KeyCode.Backspace)
                    {
                        panel.OnEntryCleared(binding, ButtonInputAction.None);
                        KeybindManager.Rebind(keybind, KeyCode.None);
                        binding.SetPrimary(keybind.PrimaryName);

                        yield break;
                    }

                    newKey = key;
                    break;
                }
            }

            if (newKey != KeyCode.None)
            {
                break;
            }

            yield return null;
        }

        KeybindManager.Rebind(keybind, newKey);
        panel.OnRebindingComplete(false);
        binding.SetPrimary(keybind.PrimaryName);
    }
}

internal sealed class KeybindDividerEntry : IKeybindMenuEntry
{
    const float DividerHeight = 28f;
    const float DividerFontSize = 20f;
    static readonly Color DividerColor = new(0.12f, 0.152f, 0.2f, 0.15f);
    readonly string dividerText;
    readonly int order;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindDividerEntry"/> class.
    /// </summary>
    /// <param name="dividerText">The divider label.</param>
    /// <param name="category">The category identifier for the divider.</param>
    /// <param name="order">The display order for the entry.</param>
    public KeybindDividerEntry(string dividerText, string category, int order)
    {
        this.dividerText = dividerText ?? throw new ArgumentNullException(nameof(dividerText));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        this.order = order;
    }

    /// <inheritdoc />
    public string Category { get; }
    /// <inheritdoc />
    public int Order => order;

    /// <inheritdoc />
    public void BuildUI(RebindingMenu panel)
    {
        if (panel == null)
        {
            throw new ArgumentNullException(nameof(panel));
        }

        var dividerGameObject = new GameObject("Divider");

        RectTransform dividerTransform = dividerGameObject.AddComponent<RectTransform>();
        dividerTransform.SetParent(panel.ContentNode);
        dividerTransform.localScale = Vector3.one;
        dividerTransform.sizeDelta = new Vector2(0f, DividerHeight);

        UnityEngine.UI.Image dividerImage = dividerGameObject.AddComponent<UnityEngine.UI.Image>();
        dividerImage.color = DividerColor;

        UnityEngine.UI.LayoutElement dividerLayout = dividerGameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        dividerLayout.preferredHeight = DividerHeight;

        GameObject dividerTextGameObject = new("Text");
        RectTransform dividerTextTransform = dividerTextGameObject.AddComponent<RectTransform>();
        dividerTextTransform.SetParent(dividerGameObject.transform);
        dividerTextTransform.localScale = Vector3.one;

        TextMeshProUGUI textMeshDivider = dividerTextGameObject.AddComponent<TextMeshProUGUI>();
        textMeshDivider.alignment = TextAlignmentOptions.Center;
        textMeshDivider.fontStyle = FontStyles.SmallCaps;
        textMeshDivider.fontSize = DividerFontSize;
        textMeshDivider.SetText(dividerText);

        if (panel.ContentNode.GetComponentsInChildren<TextMeshProUGUI>().FirstOrDefault()?.font is TMP_FontAsset fontAsset)
        {
            textMeshDivider.font = fontAsset;
        }
        else if (TMP_Settings.defaultFontAsset is TMP_FontAsset fallbackFont)
        {
            textMeshDivider.font = fallbackFont;
        }

        dividerGameObject.SetActive(true);
    }
}
