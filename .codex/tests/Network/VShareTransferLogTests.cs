using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers operator-facing VShare transfer log formatting.
/// </summary>
[Collection("Assembly setup")]
public sealed class VShareTransferLogTests
{
    /// <summary>
    /// Transfer starts should be concise and clearly identify the asset size.
    /// </summary>
    [Fact]
    public void FormatTransferStartMessage_UsesReadableVSharePrefix()
    {
        string message = Transference.FormatTransferStartMessageForTesting("Eclipse.dll", 1_835_008);

        Assert.Equal("[VShare] Transfer started: Eclipse.dll (1.75 MB).", message);
    }

    /// <summary>
    /// Progress messages should show a stable bar, percent, and byte counts.
    /// </summary>
    [Fact]
    public void FormatTransferProgressMessage_ShowsCoarseProgress()
    {
        string message = Transference.FormatTransferProgressMessageForTesting(
            "RetroCamera.dll",
            receivedBytes: 233_472,
            totalBytes: 466_944);

        Assert.Equal(
            "[VShare] Downloading RetroCamera.dll [#####-----] 50% (228 KB / 456 KB).",
            message);
    }

    /// <summary>
    /// Progress should log once per milestone bucket rather than every chunk.
    /// </summary>
    [Fact]
    public void ShouldLogTransferProgress_OnlyLogsNewMilestones()
    {
        Assert.True(Transference.ShouldLogTransferProgressForTesting(
            receivedBytes: 116_736,
            totalBytes: 466_944,
            lastLoggedPercent: 0,
            out int firstMilestone));
        Assert.Equal(25, firstMilestone);

        Assert.False(Transference.ShouldLogTransferProgressForTesting(
            receivedBytes: 120_000,
            totalBytes: 466_944,
            lastLoggedPercent: firstMilestone,
            out int repeatedMilestone));
        Assert.Equal(firstMilestone, repeatedMilestone);
    }

    /// <summary>
    /// Completion should read as a single receipt-like progress line.
    /// </summary>
    [Fact]
    public void FormatTransferCompleteMessage_IncludesDigestReceiptHint()
    {
        string message = Transference.FormatTransferCompleteMessageForTesting("Eclipse.dll", 1_835_008);

        Assert.Equal("[VShare] Download complete: Eclipse.dll (1.75 MB, digest verified).", message);
    }
}
