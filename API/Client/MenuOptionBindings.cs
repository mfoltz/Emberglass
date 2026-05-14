using BepInEx.Configuration;
using Emberglass.API.Shared;
using Emberglass.API.Shared.Config;
using System.Threading;
using System.Threading.Tasks;

namespace Emberglass.API.Client;

/// <summary>
/// Provides helpers for binding menu options to config entries or live settings bindings.
/// </summary>
public static class MenuOptionBindings
{
    const string DEFAULT_RELOAD_REASON = "Menu config option changed";
    const int SERVER_CONFIG_REQUEST_TIMEOUT_SECONDS = 10;

    /// <summary>
    /// Creates a toggle bound to the provided config entry.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="configEntry">The config entry backing the toggle.</param>
    /// <param name="requestReload">The reload trigger to invoke after menu changes.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <param name="reloadPolicy">The reload policy controlling whether reloads are requested.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created toggle option.</returns>
    public static Toggle AddToggle(
        string id,
        string name,
        string description,
        string category,
        ConfigEntry<bool> configEntry,
        Action<string> requestReload,
        string reloadReason = DEFAULT_RELOAD_REASON,
        ReloadPolicy reloadPolicy = ReloadPolicy.OnChange)
    {
        var toggle = OptionsManager.AddToggle(id, name, description, category, configEntry.Value);
        BindMenuOption(toggle, configEntry, requestReload, reloadReason, reloadPolicy);
        return toggle;
    }

    /// <summary>
    /// Creates a slider bound to the provided config entry.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="configEntry">The config entry backing the slider.</param>
    /// <param name="min">The minimum value for the slider.</param>
    /// <param name="max">The maximum value for the slider.</param>
    /// <param name="decimals">The number of decimal places to display.</param>
    /// <param name="step">The slider step value.</param>
    /// <param name="requestReload">The reload trigger to invoke after menu changes.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <param name="reloadPolicy">The reload policy controlling whether reloads are requested.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created slider option.</returns>
    public static Slider AddSlider(
        string id,
        string name,
        string description,
        string category,
        ConfigEntry<float> configEntry,
        float min,
        float max,
        int decimals,
        float step,
        Action<string> requestReload,
        string reloadReason = DEFAULT_RELOAD_REASON,
        ReloadPolicy reloadPolicy = ReloadPolicy.OnChange)
    {
        var slider = OptionsManager.AddSlider(id, name, description, category, min, max, configEntry.Value, decimals, step);
        BindMenuOption(slider, configEntry, requestReload, reloadReason, reloadPolicy);
        return slider;
    }

    /// <summary>
    /// Creates a dropdown bound to the provided config entry.
    /// </summary>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="configEntry">The config entry backing the dropdown.</param>
    /// <param name="values">The dropdown values.</param>
    /// <param name="requestReload">The reload trigger to invoke after menu changes.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <param name="reloadPolicy">The reload policy controlling whether reloads are requested.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created dropdown option.</returns>
    public static Dropdown AddDropdown(
        string id,
        string name,
        string description,
        string category,
        ConfigEntry<int> configEntry,
        string[] values,
        Action<string> requestReload,
        string reloadReason = DEFAULT_RELOAD_REASON,
        ReloadPolicy reloadPolicy = ReloadPolicy.OnChange)
    {
        int index = ClampDropdownIndex(configEntry.Value, values);
        var dropdown = OptionsManager.AddDropdown(id, name, description, category, index, values);
        BindDropdown(dropdown, configEntry, requestReload, reloadReason, reloadPolicy, values);
        return dropdown;
    }

    /// <summary>
    /// Creates a dropdown bound to an enum-backed config entry.
    /// </summary>
    /// <typeparam name="TEnum">The enum type stored in the config entry.</typeparam>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="configEntry">The config entry backing the dropdown.</param>
    /// <param name="requestReload">The reload trigger to invoke after menu changes.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <param name="reloadPolicy">The reload policy controlling whether reloads are requested.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created dropdown option.</returns>
    public static Dropdown AddDropdown<TEnum>(
        string id,
        string name,
        string description,
        string category,
        ConfigEntry<TEnum> configEntry,
        Action<string> requestReload,
        string reloadReason = DEFAULT_RELOAD_REASON,
        ReloadPolicy reloadPolicy = ReloadPolicy.OnChange)
        where TEnum : struct, Enum
    {
        string[] values = Enum.GetNames(typeof(TEnum));
        int index = ResolveEnumIndex(values, configEntry.Value);
        var dropdown = OptionsManager.AddDropdown(id, name, description, category, index, values);
        BindEnumDropdown(dropdown, configEntry, requestReload, reloadReason, reloadPolicy, values);
        return dropdown;
    }

    /// <summary>
    /// Creates a toggle bound to a live settings binding.
    /// </summary>
    /// <typeparam name="TSettings">The settings type backing the binding.</typeparam>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="binding">The binding backing the toggle.</param>
    /// <param name="liveSettings">The live settings source for reload notifications.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the binding reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created toggle option.</returns>
    public static Toggle AddToggle<TSettings>(
        string id,
        string name,
        string description,
        string category,
        Binding<TSettings, bool> binding,
        LiveSettings<TSettings> liveSettings,
        string reloadReason = DEFAULT_RELOAD_REASON)
        where TSettings : class
    {
        bool currentValue = GetBindingValue(binding);
        var toggle = OptionsManager.AddToggle(id, name, description, category, currentValue);
        BindMenuOption(toggle, binding, liveSettings, reloadReason);
        return toggle;
    }

    /// <summary>
    /// Creates a slider bound to a live settings binding.
    /// </summary>
    /// <typeparam name="TSettings">The settings type backing the binding.</typeparam>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="binding">The binding backing the slider.</param>
    /// <param name="min">The minimum value for the slider.</param>
    /// <param name="max">The maximum value for the slider.</param>
    /// <param name="decimals">The number of decimal places to display.</param>
    /// <param name="step">The slider step value.</param>
    /// <param name="liveSettings">The live settings source for reload notifications.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the binding reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created slider option.</returns>
    public static Slider AddSlider<TSettings>(
        string id,
        string name,
        string description,
        string category,
        Binding<TSettings, float> binding,
        float min,
        float max,
        int decimals,
        float step,
        LiveSettings<TSettings> liveSettings,
        string reloadReason = DEFAULT_RELOAD_REASON)
        where TSettings : class
    {
        float currentValue = GetBindingValue(binding);
        var slider = OptionsManager.AddSlider(id, name, description, category, min, max, currentValue, decimals, step);
        BindMenuOption(slider, binding, liveSettings, reloadReason);
        return slider;
    }

    /// <summary>
    /// Creates a dropdown bound to a live settings binding.
    /// </summary>
    /// <typeparam name="TSettings">The settings type backing the binding.</typeparam>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="binding">The binding backing the dropdown.</param>
    /// <param name="values">The dropdown values.</param>
    /// <param name="liveSettings">The live settings source for reload notifications.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the binding reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created dropdown option.</returns>
    public static Dropdown AddDropdown<TSettings>(
        string id,
        string name,
        string description,
        string category,
        Binding<TSettings, int> binding,
        string[] values,
        LiveSettings<TSettings> liveSettings,
        string reloadReason = DEFAULT_RELOAD_REASON)
        where TSettings : class
    {
        int index = ClampDropdownIndex(GetBindingValue(binding), values);
        var dropdown = OptionsManager.AddDropdown(id, name, description, category, index, values);
        BindDropdown(dropdown, binding, liveSettings, reloadReason, values);
        return dropdown;
    }

    /// <summary>
    /// Creates a dropdown bound to an enum-backed live settings binding.
    /// </summary>
    /// <typeparam name="TSettings">The settings type backing the binding.</typeparam>
    /// <typeparam name="TEnum">The enum type stored in the binding.</typeparam>
    /// <param name="id">The stable identifier for the option.</param>
    /// <param name="name">The display name for the option.</param>
    /// <param name="description">The display description for the option.</param>
    /// <param name="category">The category identifier for the option.</param>
    /// <param name="binding">The binding backing the dropdown.</param>
    /// <param name="liveSettings">The live settings source for reload notifications.</param>
    /// <param name="reloadReason">The reload reason to include with the trigger.</param>
    /// <remarks>
    /// Reloads are requested only when the value changes and the binding reload policy is
    /// <see cref="ReloadPolicy.OnChange"/>.
    /// </remarks>
    /// <returns>The created dropdown option.</returns>
    public static Dropdown AddDropdown<TSettings, TEnum>(
        string id,
        string name,
        string description,
        string category,
        Binding<TSettings, TEnum> binding,
        LiveSettings<TSettings> liveSettings,
        string reloadReason = DEFAULT_RELOAD_REASON)
        where TSettings : class
        where TEnum : struct, Enum
    {
        string[] values = Enum.GetNames(typeof(TEnum));
        int index = ResolveEnumIndex(values, GetBindingValue(binding));
        var dropdown = OptionsManager.AddDropdown(id, name, description, category, index, values);
        BindEnumDropdown(dropdown, binding, liveSettings, reloadReason, values);
        return dropdown;
    }

    static void BindMenuOption<TValue>(
        MenuOption<TValue> option,
        ConfigEntry<TValue> configEntry,
        Action<string> requestReload,
        string reloadReason,
        ReloadPolicy reloadPolicy)
    {
        if (configEntry is null)
        {
            throw new ArgumentNullException(nameof(configEntry));
        }

        if (requestReload is null)
        {
            throw new ArgumentNullException(nameof(requestReload));
        }

        ApplyMenuOptionMetadata(option, MenuOptionControlScope.ClientOnlyLive, reloadPolicy);

        BindMenuOption(
            option,
            () => configEntry.Value,
            value => configEntry.Value = value,
            handler => SubscribeToSettingChanged(configEntry, handler),
            requestReload,
            reloadReason,
            reloadPolicy);
    }

    static void BindMenuOption<TSettings, TValue>(
        MenuOption<TValue> option,
        Binding<TSettings, TValue> binding,
        LiveSettings<TSettings> liveSettings,
        string reloadReason)
        where TSettings : class
    {
        if (binding is null)
        {
            throw new ArgumentNullException(nameof(binding));
        }

        if (liveSettings is null)
        {
            throw new ArgumentNullException(nameof(liveSettings));
        }

        if (binding.Scope == ConfigScope.Server)
        {
            BindServerScopedMenuOption(option, binding, liveSettings, reloadReason);
            return;
        }

        ApplyMenuOptionMetadata(option, MenuOptionControlScope.ClientOnlyLive, binding.ReloadPolicy);

        BindMenuOption(
            option,
            () => GetBindingValue(binding),
            value => SetBindingValue(binding, value),
            handler => SubscribeToReloaded(binding, liveSettings, handler),
            liveSettings.RequestReload,
            reloadReason,
            binding.ReloadPolicy);
    }

    static void BindServerScopedMenuOption<TSettings, TValue>(
        MenuOption<TValue> option,
        Binding<TSettings, TValue> binding,
        LiveSettings<TSettings> liveSettings,
        string reloadReason)
        where TSettings : class
    {
        if (option is null)
        {
            throw new ArgumentNullException(nameof(option));
        }

        if (binding is null)
        {
            throw new ArgumentNullException(nameof(binding));
        }

        if (liveSettings is null)
        {
            throw new ArgumentNullException(nameof(liveSettings));
        }

        ApplyMenuOptionMetadata(option, MenuOptionControlScope.ServerControlled, binding.ReloadPolicy);

        bool isUpdating = false;
        long requestSequence = 0;
        var comparer = EqualityComparer<TValue>.Default;

        option.AddListener(value =>
        {
            if (isUpdating)
            {
                return;
            }

            long requestId = Interlocked.Increment(ref requestSequence);
            _ = SendServerChangeAsync(value, requestId);
        });

        async Task SendServerChangeAsync(TValue value, long requestId)
        {
            if (!VNetwork.IsReady || !VWorld.IsClient)
            {
                RestoreLocalValue(requestId);
                return;
            }

            try
            {
                var request = new ServerConfigChangeRequest<TValue>(binding.ChangedKey, value);
                var response = await VNetwork.SendRequestAsync<ServerConfigChangeRequest<TValue>, ServerConfigChangeResponse<TValue>>(
                    VWorld.LocalUser.GetUser(),
                    request,
                    TimeSpan.FromSeconds(SERVER_CONFIG_REQUEST_TIMEOUT_SECONDS));

                ApplyServerResponse(response, requestId);
            }
            catch (Exception ex)
            {
                VWorld.Log.LogWarning($"[MenuOptionBindings] Server config change failed ({binding.ChangedKey}): {ex.Message}");
                RestoreLocalValue(requestId);
            }
        }

        void RestoreLocalValue(long requestId)
        {
            if (requestId != Volatile.Read(ref requestSequence))
            {
                return;
            }

            TValue currentValue = GetBindingValue(binding);
            ApplyAuthoritativeValue(currentValue, requestId, shouldRequestReload: false);
        }

        void ApplyServerResponse(ServerConfigChangeResponse<TValue> response, long requestId)
        {
            if (response is null)
            {
                RestoreLocalValue(requestId);
                return;
            }

            if (requestId != Volatile.Read(ref requestSequence))
            {
                return;
            }

            if (!string.Equals(response.BindingKey, binding.ChangedKey, StringComparison.Ordinal))
            {
                VWorld.Log.LogWarning($"[MenuOptionBindings] Ignoring server response for unexpected binding '{response.BindingKey}'.");
                return;
            }

            bool shouldRequestReload = response.IsAccepted && binding.ReloadPolicy == ReloadPolicy.OnChange;
            if (!response.IsAccepted && !string.IsNullOrWhiteSpace(response.RejectionReason))
            {
                VWorld.Log.LogWarning($"[MenuOptionBindings] Server rejected config change ({binding.ChangedKey}): {response.RejectionReason}");
            }

            ApplyAuthoritativeValue(response.Value, requestId, shouldRequestReload);
        }

        void ApplyAuthoritativeValue(TValue value, long requestId, bool shouldRequestReload)
        {
            if (requestId != Volatile.Read(ref requestSequence))
            {
                return;
            }

            TValue currentValue = GetBindingValue(binding);
            bool hasChanged = !comparer.Equals(currentValue, value);

            try
            {
                isUpdating = true;
                SetBindingValue(binding, value);
                option.SetValue(value);
            }
            finally
            {
                isUpdating = false;
            }

            if (hasChanged && shouldRequestReload)
            {
                liveSettings.RequestReload(reloadReason);
            }
        }
    }

    static void BindEnumDropdown<TEnum>(
        Dropdown dropdown,
        ConfigEntry<TEnum> configEntry,
        Action<string> requestReload,
        string reloadReason,
        ReloadPolicy reloadPolicy,
        IReadOnlyList<string> values)
        where TEnum : struct, Enum
    {
        if (configEntry is null)
        {
            throw new ArgumentNullException(nameof(configEntry));
        }

        if (requestReload is null)
        {
            throw new ArgumentNullException(nameof(requestReload));
        }

        BindMenuOption<int>(
            dropdown,
            () => ResolveEnumIndex(values, configEntry.Value),
            value => configEntry.Value = ResolveEnumValue(values, value, configEntry.Value),
            handler => SubscribeToSettingChanged(configEntry, enumValue => handler(ResolveEnumIndex(values, enumValue))),
            requestReload,
            reloadReason,
            reloadPolicy);
    }

    static void ApplyMenuOptionMetadata(MenuOption option, MenuOptionControlScope controlScope, ReloadPolicy reloadPolicy)
    {
        if (option is null)
        {
            throw new ArgumentNullException(nameof(option));
        }

        option.ControlScope = controlScope;
        option.RequiresReload = reloadPolicy == ReloadPolicy.OnChange;
    }

    static void BindDropdown(
        Dropdown dropdown,
        ConfigEntry<int> configEntry,
        Action<string> requestReload,
        string reloadReason,
        ReloadPolicy reloadPolicy,
        IReadOnlyCollection<string> values)
    {
        if (configEntry is null)
        {
            throw new ArgumentNullException(nameof(configEntry));
        }

        if (requestReload is null)
        {
            throw new ArgumentNullException(nameof(requestReload));
        }

        BindMenuOption(
            dropdown,
            () => ClampDropdownIndex(configEntry.Value, values),
            value => configEntry.Value = ClampDropdownIndex(value, values),
            handler => SubscribeToSettingChanged(configEntry, handler),
            requestReload,
            reloadReason,
            reloadPolicy);
    }

    static void BindDropdown<TSettings>(
        Dropdown dropdown,
        Binding<TSettings, int> binding,
        LiveSettings<TSettings> liveSettings,
        string reloadReason,
        IReadOnlyCollection<string> values)
        where TSettings : class
    {
        if (binding is null)
        {
            throw new ArgumentNullException(nameof(binding));
        }

        if (liveSettings is null)
        {
            throw new ArgumentNullException(nameof(liveSettings));
        }

        BindMenuOption(
            dropdown,
            () => ClampDropdownIndex(GetBindingValue(binding), values),
            value => SetBindingValue(binding, ClampDropdownIndex(value, values)),
            handler => SubscribeToReloaded(binding, liveSettings, handler),
            liveSettings.RequestReload,
            reloadReason,
            binding.ReloadPolicy);
    }

    static void BindEnumDropdown<TSettings, TEnum>(
        Dropdown dropdown,
        Binding<TSettings, TEnum> binding,
        LiveSettings<TSettings> liveSettings,
        string reloadReason,
        IReadOnlyList<string> values)
        where TSettings : class
        where TEnum : struct, Enum
    {
        if (binding is null)
        {
            throw new ArgumentNullException(nameof(binding));
        }

        if (liveSettings is null)
        {
            throw new ArgumentNullException(nameof(liveSettings));
        }

        BindMenuOption<int>(
            dropdown,
            () => ResolveEnumIndex(values, GetBindingValue(binding)),
            value => SetBindingValue(binding, ResolveEnumValue(values, value, GetBindingValue(binding))),
            handler => SubscribeToReloaded(binding, liveSettings, enumValue => handler(ResolveEnumIndex(values, enumValue))),
            liveSettings.RequestReload,
            reloadReason,
            binding.ReloadPolicy);
    }

    static void BindMenuOption<TValue>(
        MenuOption<TValue> option,
        Func<TValue> getConfigValue,
        Action<TValue> setConfigValue,
        Action<Action<TValue>> subscribeToConfigChanges,
        Action<string> requestReload,
        string reloadReason,
        ReloadPolicy reloadPolicy)
    {
        if (option is null)
        {
            throw new ArgumentNullException(nameof(option));
        }

        if (getConfigValue is null)
        {
            throw new ArgumentNullException(nameof(getConfigValue));
        }

        if (setConfigValue is null)
        {
            throw new ArgumentNullException(nameof(setConfigValue));
        }

        if (subscribeToConfigChanges is null)
        {
            throw new ArgumentNullException(nameof(subscribeToConfigChanges));
        }

        if (requestReload is null)
        {
            throw new ArgumentNullException(nameof(requestReload));
        }

        bool isUpdating = false;
        var comparer = EqualityComparer<TValue>.Default;

        option.AddListener(value =>
        {
            if (isUpdating)
            {
                return;
            }

            try
            {
                isUpdating = true;
                var current = getConfigValue();

                bool hasChanged = !comparer.Equals(current, value);

                if (hasChanged)
                {
                    setConfigValue(value);
                }

                if (hasChanged && reloadPolicy == ReloadPolicy.OnChange)
                {
                    requestReload(reloadReason);
                }
            }
            finally
            {
                isUpdating = false;
            }
        });

        subscribeToConfigChanges(value =>
        {
            if (isUpdating)
            {
                return;
            }

            try
            {
                isUpdating = true;
                option.SetValue(value);
            }
            finally
            {
                isUpdating = false;
            }
        });
    }

    static void SubscribeToSettingChanged<TValue>(
        ConfigEntry<TValue> configEntry,
        Action<TValue> handler)
    {
        if (configEntry.ConfigFile is null)
        {
            return;
        }

        configEntry.ConfigFile.SettingChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.ChangedSetting, configEntry))
            {
                return;
            }

            handler(configEntry.Value);
        };
    }

    static void SubscribeToReloaded<TSettings, TValue>(
        Binding<TSettings, TValue> binding,
        LiveSettings<TSettings> liveSettings,
        Action<TValue> handler)
        where TSettings : class
    {
        liveSettings.Reloaded += (_, args) =>
        {
            if (!args.ChangedKeys.Contains(binding.ChangedKey))
            {
                return;
            }

            handler(GetBindingValue(binding));
        };
    }

    static TValue GetBindingValue<TSettings, TValue>(Binding<TSettings, TValue> binding)
        where TSettings : class
    {
        if (!binding.TryGetValue(out var value))
        {
            throw new InvalidOperationException("Binding has not been bound to a config file.");
        }

        return value;
    }

    static void SetBindingValue<TSettings, TValue>(Binding<TSettings, TValue> binding, TValue value)
        where TSettings : class
    {
        if (!binding.TrySetValue(value))
        {
            throw new InvalidOperationException("Binding has not been bound to a config file.");
        }
    }

    static int ClampDropdownIndex(int value, IReadOnlyCollection<string> values)
    {
        if (values is null || values.Count == 0)
        {
            return 0;
        }

        return Math.Clamp(value, 0, values.Count - 1);
    }

    static int ResolveEnumIndex<TEnum>(IReadOnlyList<string> values, TEnum value)
        where TEnum : struct, Enum
    {
        if (values is null || values.Count == 0)
        {
            return 0;
        }

        string stringValue = value.ToString();
        for (int index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], stringValue, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    static TEnum ResolveEnumValue<TEnum>(IReadOnlyList<string> values, int index, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (values is null || values.Count == 0)
        {
            return fallback;
        }

        int clampedIndex = Math.Clamp(index, 0, values.Count - 1);
        return Enum.Parse<TEnum>(values[clampedIndex]);
    }
}
