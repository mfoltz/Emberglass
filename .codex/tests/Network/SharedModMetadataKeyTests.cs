using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers VShare shared-mod metadata key selection.
/// </summary>
public sealed class SharedModMetadataKeyTests
{
    /// <summary>
    /// Ensures release-style staged names can still resolve human-legible metadata keys.
    /// </summary>
    [Fact]
    public void GetShareMetadataKeys_IncludesRepoNameForReleaseStyleAsset()
    {
        IReadOnlyList<string> keys = Transference.GetShareMetadataKeysForTesting(
            "mfoltz_Eclipse_v1.3.14-pre.dll");

        Assert.Collection(
            keys,
            key => Assert.Equal("mfoltz_Eclipse_v1.3.14-pre", key),
            key => Assert.Equal("Eclipse", key));
    }

    /// <summary>
    /// Ensures a non-client-safe staged-name entry does not block a later safe repo-name entry.
    /// </summary>
    [Fact]
    public void TrySelectClientShareMetadata_ContinuesAfterUnsafeMatchedKey()
    {
        PluginShareMetadataStore.PluginShareMetadata unsafeEntry = new(
            "mfoltz/Eclipse",
            "v1.3.14-pre",
            false,
            false,
            Array.Empty<string>(),
            Array.Empty<string>());
        PluginShareMetadataStore.PluginShareMetadata safeEntry = unsafeEntry with
        {
            ClientSafe = true,
            HotloadAllowed = true
        };
        var entries = new Dictionary<string, PluginShareMetadataStore.PluginShareMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["mfoltz_Eclipse_v1.3.14-pre"] = unsafeEntry,
            ["Eclipse"] = safeEntry
        };

        bool resolved = Transference.TrySelectClientShareMetadataForTesting(
            new[] { "mfoltz_Eclipse_v1.3.14-pre", "Eclipse" },
            entries,
            out PluginShareMetadataStore.PluginShareMetadata metadata,
            out string skipReason);

        Assert.True(resolved);
        Assert.True(metadata.ClientSafe);
        Assert.True(metadata.HotloadAllowed);
        Assert.Equal(string.Empty, skipReason);
    }
}
