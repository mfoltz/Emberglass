using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for GitHub Release metadata normalization.
/// </summary>
public sealed class GitHubReleaseClientTests
{
    /// <summary>
    /// Ensures GitHub's algorithm-qualified digest field is normalized to raw hex.
    /// </summary>
    [Fact]
    public void TryNormalizeSha256Digest_RemovesSha256Prefix()
    {
        const string RawDigest = "0123456789abcdef";

        bool Result = GitHubReleaseClient.TryNormalizeSha256Digest(
            $" sha256:{RawDigest} ",
            out string Digest,
            out string ErrorMessage);

        Assert.True(Result);
        Assert.Equal(RawDigest, Digest);
        Assert.Equal(string.Empty, ErrorMessage);
    }

    /// <summary>
    /// Ensures already-normalized digest values remain accepted.
    /// </summary>
    [Fact]
    public void TryNormalizeSha256Digest_AcceptsRawDigest()
    {
        const string RawDigest = "ABCDEF0123456789";

        bool Result = GitHubReleaseClient.TryNormalizeSha256Digest(
            $" {RawDigest} ",
            out string Digest,
            out string ErrorMessage);

        Assert.True(Result);
        Assert.Equal(RawDigest, Digest);
        Assert.Equal(string.Empty, ErrorMessage);
    }

    /// <summary>
    /// Ensures non-SHA-256 algorithm prefixes are rejected before transfer verification.
    /// </summary>
    [Fact]
    public void TryNormalizeSha256Digest_RejectsOtherAlgorithmPrefixes()
    {
        bool Result = GitHubReleaseClient.TryNormalizeSha256Digest(
            "sha512:0123456789abcdef",
            out string Digest,
            out string ErrorMessage);

        Assert.False(Result);
        Assert.Equal(string.Empty, Digest);
        Assert.Contains("SHA-256", ErrorMessage);
    }
}
