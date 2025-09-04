using Il2CppInterop.Runtime.Injection;
using ProjectM;
using ProjectM.UI;
using Unity.Entities;
using UnityEngine;

namespace Emberglass.API.Shared;
public class VBehaviour : MonoBehaviour
{
    public static VBehaviour Instance => _instance;
    static VBehaviour _instance;
    public static void Initialize()
    {
        ClassInjector.RegisterTypeInIl2Cpp<VBehaviour>();
        _instance = Plugin.Instance.AddComponent<VBehaviour>();
    }
    public static void Uninitialize()
    {
        if (_instance != null)
        {
            Destroy(_instance);
            _instance = null;
        }
    }
    void Awake()
    {

    }
    void Update()
    {

    }
    void OnDestroy()
    {

    }

    /*
    public static bool SocialWheelActive => _socialWheelActive;
    static bool _socialWheelActive = false;
    public static ActionWheel SocialWheel => _socialWheel;
    static ActionWheel _socialWheel;
    public static bool _shouldActivateWheel = false;

    static Entity _rootPrefabCollection;
    static bool _socialWheelInitialized = false;
    static void SocialWheelKeyPressed()
    {
        if (!Settings.CommandWheelEnabled) return;

        if (!_rootPrefabCollection.Exists() || _socialWheel == null)
        {
            Core.Log.LogWarning($"[RetroCamera] Initializing SocialWheel...");
            ActionWheelSystem?._RootPrefabCollectionAccessor.TryGetSingletonEntity(out _rootPrefabCollection);

            if (!_socialWheelInitialized && _rootPrefabCollection.TryGetComponent(out RootPrefabCollection rootPrefabCollection)
                && rootPrefabCollection.GeneralGameplayCollectionPrefab.TryGetComponent(out GeneralGameplayCollection generalGameplayCollection))
            {
                foreach (var commandQuip in CommandQuips)
                {
                    if (string.IsNullOrEmpty(commandQuip.Value.Name)
                        || string.IsNullOrEmpty(commandQuip.Value.Command))
                        continue;

                    ChatQuip chatQuip = generalGameplayCollection.ChatQuips[commandQuip.Key];
                    chatQuip.Text = commandQuip.Value.NameKey;

                    // Core.Log.LogWarning($"[RetroCamera] QuipData - {commandQuip.Value.Name} | {commandQuip.Value.Command} | {chatQuip.Sequence} | {chatQuip.Sequence.ToPrefabGUID()}");

                    generalGameplayCollection.ChatQuips[commandQuip.Key] = chatQuip;
                }

                ActionWheelSystem.InitializeSocialWheel(true, generalGameplayCollection);
                _socialWheelInitialized = true;

                try
                {
                    LocalizationManager.LocalizeText();
                }
                catch (Exception ex)
                {
                    Core.Log.LogError($"[RetroCamera.Update] Failed to localize keys - {ex.Message}");
                }

                try
                {
                    var chatQuips = generalGameplayCollection.ChatQuips;
                    var socialWheelData = ActionWheelSystem._SocialWheelDataList;
                    var socialWheelShortcuts = ActionWheelSystem._SocialWheelShortcutList;

                    // Core.Log.LogWarning($"[RetroCamera] SocialWheelData count - {socialWheelData.Count} | {chatQuips.Length}");

                    foreach (var commandQuip in CommandQuips)
                    {
                        if (string.IsNullOrEmpty(commandQuip.Value.Name)
                            || string.IsNullOrEmpty(commandQuip.Value.Command))
                            continue;

                        ActionWheelData wheelData = socialWheelData[commandQuip.Key];

                        // Core.Log.LogWarning($"[RetroCamera] WheelData - {commandQuip.Value.Name} | {commandQuip.Value.Command} | {wheelData.Name}");
                        wheelData.Name = commandQuip.Value.NameKey;
                    }
                }
                catch (Exception ex)
                {
                    Core.Log.LogError(ex);
                }
            }

            _socialWheel = ActionWheelSystem?._SocialWheel;
            var shortcuts = _socialWheel.ActionWheelShortcuts;

            foreach (var shortcut in shortcuts)
            {
                shortcut?.gameObject?.SetActive(false);
            }

            _socialWheel.gameObject.SetActive(true);
        }

        if (!_socialWheelActive)
        {
            _shouldActivateWheel = true;
            _socialWheelActive = true;
            ActionWheelSystem._CurrentActiveWheel = SocialWheel;
            Core.ActionWheelSystem.UpdateAndShowWheel(SocialWheel, inputState);
            // Core.Log.LogWarning($"[RetroCamera] Activating wheel");
        }
    }
    static void SocialWheelKeyUp()
    {
        if (!Settings.CommandWheelEnabled) return;

        if (_socialWheelActive)
        {
            _socialWheelActive = false;
            ActionWheelSystem.HideCurrentWheel();
            _socialWheel.gameObject.SetActive(false);
            ActionWheelSystem._CurrentActiveWheel = null;
            // Core.Log.LogWarning($"[RetroCamera] SocialWheelKeyUp");
        }
    }
    */
}