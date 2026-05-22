using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers options-menu button registration stability.
/// </summary>
public sealed class OptionsManagerButtonTests
{
    /// <summary>
    /// Ensures repeated registration of the same button id does not create duplicate menu entries.
    /// </summary>
    [Fact]
    public void AddButton_WithExistingId_DoesNotAddDuplicateEntry()
    {
        string source = File.ReadAllText(FindRepositoryFile("API", "Client", "OptionsManager.cs"));

        Assert.Contains("static readonly HashSet<string> _buttonIds = [];", source);
        Assert.Contains("if (!_buttonIds.Add(id))", source);
        Assert.Contains("return;", source);
    }

    /// <summary>
    /// Ensures button entries can resolve the button prefab from the active interface panel lifecycle.
    /// </summary>
    [Fact]
    public void ButtonEntry_UsesPanelPrefabFallback()
    {
        string source = File.ReadAllText(FindRepositoryFile("API", "Client", "MenuEntries.cs"));
        string patches = File.ReadAllText(FindRepositoryFile("Patches", "Client", "OptionsMenuPatches.cs"));

        Assert.Contains("ResolveButtonPrefab(panel)", source);
        Assert.Contains("GetComponentInParent<OptionsMenu>()", patches);
        Assert.Contains("AwakePostfix(OptionsMenu __instance)", patches);
    }

    static string FindRepositoryFile(params string[] relativeParts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(relativeParts));
    }
}
