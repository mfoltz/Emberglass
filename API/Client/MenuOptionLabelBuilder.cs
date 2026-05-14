using System.Text;

namespace Emberglass.API.Client;

/// <summary>
/// Builds localized status labels for menu option descriptions.
/// </summary>
public static class MenuOptionLabelBuilder
{
    const string CLIENT_ONLY_LIVE_LABEL = "Client-only (live)";
    const string SERVER_CONTROLLED_LABEL = "Server-controlled (requires server approval)";
    const string RELOAD_REQUIRED_LABEL = "Reload required";
    const string LABEL_SEPARATOR = " • ";

    /// <summary>
    /// Builds a label string for the provided metadata.
    /// </summary>
    /// <param name="controlScope">The control scope for the option.</param>
    /// <param name="requiresReload">Whether the option requires a reload to apply.</param>
    /// <returns>The combined label string, or empty when no labels apply.</returns>
    public static string BuildLabelText(MenuOptionControlScope controlScope, bool requiresReload)
    {
        StringBuilder labelBuilder = new();

        AppendLabel(labelBuilder, GetControlScopeLabel(controlScope));
        if (requiresReload)
        {
            AppendLabel(labelBuilder, RELOAD_REQUIRED_LABEL);
        }

        return labelBuilder.ToString();
    }

    static string GetControlScopeLabel(MenuOptionControlScope controlScope)
    {
        return controlScope switch
        {
            MenuOptionControlScope.ClientOnlyLive => CLIENT_ONLY_LIVE_LABEL,
            MenuOptionControlScope.ServerControlled => SERVER_CONTROLLED_LABEL,
            _ => string.Empty
        };
    }

    static void AppendLabel(StringBuilder labelBuilder, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        LocalizationKeyManager.GetLocalizationKey(label);

        if (labelBuilder.Length > 0)
        {
            labelBuilder.Append(LABEL_SEPARATOR);
        }

        labelBuilder.Append(label);
    }
}
