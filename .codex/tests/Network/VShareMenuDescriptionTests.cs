using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the VShare options-menu detail text shown to clients.
/// </summary>
public sealed class VShareMenuDescriptionTests
{
    /// <summary>
    /// Ensures the default hover text waits for server-advertised catalog details.
    /// </summary>
    [Fact]
    public void BuildSharedModsMenuDescription_WithoutPreview_WaitsForServerDetails()
    {
        string description = Transference.BuildSharedModsMenuDescriptionForTesting(null);

        Assert.Equal(
            "Server shared mods:\n- Waiting for server details.",
            description);
    }

    /// <summary>
    /// Ensures the preview text lists concrete server-advertised mods.
    /// </summary>
    [Fact]
    public void BuildSharedModsMenuDescription_WithPreview_ListsLiteralModDetails()
    {
        var entries = new[]
        {
            new Transference.SharedClientModPreviewEntry(
                "Eclipse",
                "mfoltz/Eclipse",
                "v1.3.14-pre",
                "Eclipse.dll",
                false,
                true),
            new Transference.SharedClientModPreviewEntry(
                "RetroCamera",
                "mfoltz/RetroCamera",
                "v0.2.0",
                "RetroCamera.zip",
                true,
                false)
        };

        string description = Transference.BuildSharedModsMenuDescriptionForTesting(entries);

        Assert.Contains("Server shared mods:", description);
        Assert.Contains("- Eclipse v1.3.14-pre", description);
        Assert.Contains("Source: mfoltz/Eclipse", description);
        Assert.Contains("File: Eclipse.dll (DLL)", description);
        Assert.Contains("Runtime load: enabled", description);
        Assert.Contains("- RetroCamera v0.2.0", description);
        Assert.Contains("File: RetroCamera.zip (ZIP)", description);
        Assert.Contains("Runtime load: disabled", description);
        Assert.DoesNotContain("requires your consent", description);
        Assert.DoesNotContain("GitHub release asset digest", description);
        Assert.DoesNotContain("Integrity:", description);
    }

    /// <summary>
    /// Ensures an empty preview gives useful catalog feedback.
    /// </summary>
    [Fact]
    public void BuildSharedModsMenuDescription_WithEmptyPreview_ReportsNoAdvertisedMods()
    {
        string description = Transference.BuildSharedModsMenuDescriptionForTesting(Array.Empty<Transference.SharedClientModPreviewEntry>());

        Assert.Equal(
            "Server shared mods:\n- No server-shared mods advertised by this server.",
            description);
    }

    /// <summary>
    /// Ensures reconnect/session-reset state cannot keep showing the previous server catalog.
    /// </summary>
    [Fact]
    public void ClearSharedModRequestConsent_ClearsLatestServerPreview()
    {
        string source = File.ReadAllText(FindRepositoryFile("Network", "Transference.cs"));

        Assert.Contains("_latestSharedClientModPreview = null;", source);
        Assert.Contains("OptionsManager.RefreshButtonDescription(REQUEST_SHARED_MODS_BUTTON_ID);", source);
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
