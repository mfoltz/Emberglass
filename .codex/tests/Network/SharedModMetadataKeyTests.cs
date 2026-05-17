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
}
