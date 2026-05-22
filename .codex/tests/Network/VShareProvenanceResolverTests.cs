using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers VShare staged-release provenance identity resolution.
/// </summary>
public sealed class VShareProvenanceResolverTests
{
    /// <summary>
    /// Ensures explicit share metadata is preferred over release-style file names.
    /// </summary>
    [Fact]
    public void TryResolveStagedReleaseIdentity_MetadataFirst_AllowsFriendlyFileName()
    {
        var entries = new Dictionary<string, PluginShareMetadataStore.PluginShareMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["FriendlyEclipse"] = new(
                "mfoltz/Eclipse",
                "v1.3.14-pre",
                "Eclipse.dll",
                true,
                true,
                new[] { "client" },
                Array.Empty<string>(),
                string.Empty)
        };

        bool resolved = Transference.TryResolveStagedReleaseIdentityForTesting(
            "FriendlyEclipse.dll",
            entries,
            out Transference.StagedReleaseIdentity identity,
            out string errorMessage);

        Assert.True(resolved);
        Assert.Equal("mfoltz", identity.Owner);
        Assert.Equal("Eclipse", identity.Repo);
        Assert.Equal("v1.3.14-pre", identity.Tag);
        Assert.Equal("Eclipse.dll", identity.AssetName);
        Assert.True(identity.FromMetadata);
        Assert.Equal("FriendlyEclipse", identity.MetadataKey);
        Assert.Equal(string.Empty, errorMessage);
    }

    /// <summary>
    /// Ensures the staged asset naming convention remains a compatibility fallback.
    /// </summary>
    [Fact]
    public void TryResolveStagedReleaseIdentity_FallsBackToReleaseStyleFileName()
    {
        bool resolved = Transference.TryResolveStagedReleaseIdentityForTesting(
            "mfoltz_Eclipse_v1.3.14-pre.dll",
            new Dictionary<string, PluginShareMetadataStore.PluginShareMetadata>(StringComparer.OrdinalIgnoreCase),
            out Transference.StagedReleaseIdentity identity,
            out string errorMessage);

        Assert.True(resolved);
        Assert.Equal("mfoltz", identity.Owner);
        Assert.Equal("Eclipse", identity.Repo);
        Assert.Equal("v1.3.14-pre", identity.Tag);
        Assert.Equal("mfoltz_Eclipse_v1.3.14-pre.dll", identity.AssetName);
        Assert.False(identity.FromMetadata);
        Assert.Equal("Eclipse", identity.MetadataKey);
        Assert.Equal(string.Empty, errorMessage);
    }

    /// <summary>
    /// Ensures explicit metadata can point a local staged file at a differently named release asset.
    /// </summary>
    [Fact]
    public void TryResolveStagedReleaseIdentity_GitHubAssetNameOverridesLocalFileName()
    {
        var entries = new Dictionary<string, PluginShareMetadataStore.PluginShareMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["EclipseLocal"] = new(
                "mfoltz/Eclipse",
                "v1.3.14-pre",
                "mfoltz_Eclipse_v1.3.14-pre.dll",
                true,
                false,
                Array.Empty<string>(),
                new[] { "client" },
                string.Empty)
        };

        bool resolved = Transference.TryResolveStagedReleaseIdentityForTesting(
            "EclipseLocal.dll",
            entries,
            out Transference.StagedReleaseIdentity identity,
            out string errorMessage);

        Assert.True(resolved);
        Assert.Equal("mfoltz_Eclipse_v1.3.14-pre.dll", identity.AssetName);
        Assert.True(identity.FromMetadata);
        Assert.Equal(string.Empty, errorMessage);
    }

    /// <summary>
    /// Ensures local staged bytes must match the resolved GitHub Release asset digest.
    /// </summary>
    [Fact]
    public void TryValidateStagedReleaseDigestForTesting_RejectsDigestMismatch()
    {
        byte[] stagedBytes = { 1, 2, 3, 4 };
        string wrongDigest = new('A', 64);

        bool valid = Transference.TryValidateStagedReleaseDigestForTesting(
            "Eclipse.dll",
            stagedBytes,
            wrongDigest,
            out string localSha256,
            out byte[] releaseDigestBytes,
            out string errorMessage);

        Assert.False(valid);
        Assert.Equal(64, localSha256.Length);
        Assert.Empty(releaseDigestBytes);
        Assert.Contains("digest mismatch", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures staged file access failures are skipped instead of interrupting share enumeration.
    /// </summary>
    [Fact]
    public void IsStagedModFileAccessExceptionForTesting_AllowsExpectedAccessFailures()
    {
        Assert.True(Transference.IsStagedModFileAccessExceptionForTesting(new IOException("locked")));
        Assert.True(Transference.IsStagedModFileAccessExceptionForTesting(new UnauthorizedAccessException("denied")));
        Assert.True(Transference.IsStagedModFileAccessExceptionForTesting(new System.Security.SecurityException("blocked")));
        Assert.False(Transference.IsStagedModFileAccessExceptionForTesting(new InvalidOperationException("bug")));
    }
}
