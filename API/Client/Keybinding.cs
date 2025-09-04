using ProjectM;
using Stunlock.Localization;
using System.Text.Json.Serialization;
using UnityEngine;

namespace Emberglass.API.Client;

[Serializable]
public class Keybinding
{
    public string Name;
    public string Description;
    public string Category;

    public KeyCode Primary = KeyCode.None;
    public string PrimaryName => KeybindManager.GetLiteral(Primary);

    public delegate void KeyHandler();

    public event KeyHandler OnKeyPressedHandler = delegate { };
    public event KeyHandler OnKeyDownHandler = delegate { };
    public event KeyHandler OnKeyUpHandler = delegate { };

    [JsonIgnore]
    public LocalizationKey NameKey;

    [JsonIgnore]
    public LocalizationKey DescriptionKey;

    [JsonIgnore]
    public ButtonInputAction InputFlag;

    [JsonIgnore]
    public int AssetGuid;
    public Keybinding() { }
    public Keybinding(string name, string description, string category, KeyCode defaultKey)
    {
        Name = name;
        Description = description;
        Category = category;
        Primary = defaultKey;
        NameKey = LocalizationKeyManager.GetLocalizationKey(name);
        DescriptionKey = LocalizationKeyManager.GetLocalizationKey(description);
        InputFlag = KeybindManager.ComputeInputFlag(name);
        AssetGuid = KeybindManager.ComputeAssetGuid(name);
    }
    public void AddKeyPressedListener(KeyHandler action) => OnKeyPressedHandler += action;
    public void AddKeyDownListener(KeyHandler action) => OnKeyDownHandler += action;
    public void AddKeyUpListener(KeyHandler action) => OnKeyUpHandler += action;
    public void KeyPressed() => OnKeyPressedHandler();
    public void KeyDown() => OnKeyDownHandler();
    public void KeyUp() => OnKeyUpHandler();
    public void ApplySaved(Keybinding keybind)
    {
        if (keybind == null)
        {
            return;
        }

        Primary = keybind.Primary;
    }
}
