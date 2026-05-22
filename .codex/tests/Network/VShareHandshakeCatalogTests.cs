using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the VShare catalog advertised to clients after VNetwork readiness.
/// </summary>
public sealed class VShareHandshakeCatalogTests
{
    /// <summary>
    /// Ensures the server sends a VShare catalog when a client completes the authenticated handshake.
    /// </summary>
    [Fact]
    public void TransferenceBootstrap_SubscribesToVNetworkReadyForCatalogSend()
    {
        string source = File.ReadAllText(FindRepositoryFile("Network", "Transference.cs"));

        Assert.Contains("VNetwork.OnReady += OnVNetworkReady", source);
        Assert.Contains("SendSharedClientModCatalog(user)", source);
    }

    /// <summary>
    /// Ensures catalog construction describes all client-safe staged mods before missing-mod filtering.
    /// </summary>
    [Fact]
    public void BuildSharedClientModCatalogEntries_IncludesAllClientSafeStagedMods()
    {
        PluginShareMetadataStore.PluginShareMetadata eclipse = new(
            "mfoltz/Eclipse",
            "v1.3.17-pre",
            "Eclipse.dll",
            true,
            true,
            Array.Empty<string>(),
            Array.Empty<string>(),
            string.Empty);
        PluginShareMetadataStore.PluginShareMetadata retroCamera = new(
            "mfoltz/RetroCamera",
            "v1.5.4",
            "RetroCamera.dll",
            true,
            true,
            Array.Empty<string>(),
            Array.Empty<string>(),
            string.Empty);
        PluginShareMetadataStore.PluginShareMetadata unsafeEntry = new(
            "mfoltz/ServerOnly",
            "v0.1.0",
            "ServerOnly.dll",
            false,
            true,
            Array.Empty<string>(),
            Array.Empty<string>(),
            string.Empty);
        var metadata = new Dictionary<string, PluginShareMetadataStore.PluginShareMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eclipse"] = eclipse,
            ["RetroCamera"] = retroCamera,
            ["ServerOnly"] = unsafeEntry
        };

        Transference.SharedClientModPreviewEntry[] entries =
            Transference.BuildSharedClientModCatalogEntriesForTesting(
                new[]
                {
                    ("Eclipse.dll", "Eclipse", "Eclipse", "abc123", false),
                    ("RetroCamera.dll", "RetroCamera", "RetroCamera", "def456", false),
                    ("ServerOnly.dll", "ServerOnly", "ServerOnly", "789abc", false)
                },
                metadata);

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal("Eclipse", entry.DisplayName);
                Assert.Equal("mfoltz/Eclipse", entry.GitHubRepo);
                Assert.Equal("v1.3.17-pre", entry.GitHubTag);
                Assert.Equal("Eclipse.dll", entry.FileName);
                Assert.True(entry.Hotload);
            },
            entry =>
            {
                Assert.Equal("RetroCamera", entry.DisplayName);
                Assert.Equal("mfoltz/RetroCamera", entry.GitHubRepo);
                Assert.Equal("v1.5.4", entry.GitHubTag);
                Assert.Equal("RetroCamera.dll", entry.FileName);
                Assert.True(entry.Hotload);
            });
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
