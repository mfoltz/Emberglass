using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers transfer overwrite confirmation scoping.
/// </summary>
[Collection("Assembly setup")]
public sealed class TransferOverwriteConfirmationTests : IDisposable
{
    readonly Func<DateTime> originalUtcNowProvider;

    /// <summary>
    /// Initializes isolated overwrite confirmation state.
    /// </summary>
    public TransferOverwriteConfirmationTests()
    {
        originalUtcNowProvider = Transference.UtcNowProvider;
        Transference.ResetOverwriteConfirmationsForTesting();
    }

    /// <summary>
    /// Ensures overwrite confirmation cannot bleed between offers for the same file name.
    /// </summary>
    [Fact]
    public void OverwriteConfirmation_IsScopedToTransferIdAndFileName()
    {
        Guid FirstOfferId = Guid.NewGuid();
        Guid SecondOfferId = Guid.NewGuid();
        const string FileName = "Foo.dll";

        Transference.RegisterOverwriteConfirmationForTesting(FirstOfferId, FileName);

        Assert.False(Transference.TryConsumeOverwriteConfirmationForTesting(SecondOfferId, FileName));
        Assert.True(Transference.TryConsumeOverwriteConfirmationForTesting(FirstOfferId, FileName));
        Assert.False(Transference.TryConsumeOverwriteConfirmationForTesting(FirstOfferId, FileName));
    }

    /// <summary>
    /// Ensures overwrite confirmation still matches file names case-insensitively for the same transfer.
    /// </summary>
    [Fact]
    public void OverwriteConfirmation_FileNameMatchIsCaseInsensitive()
    {
        Guid OfferId = Guid.NewGuid();

        Transference.RegisterOverwriteConfirmationForTesting(OfferId, "Foo.dll");

        Assert.True(Transference.TryConsumeOverwriteConfirmationForTesting(OfferId, "foo.DLL"));
    }

    /// <summary>
    /// Ensures expired confirmations are removed before they can authorize an overwrite.
    /// </summary>
    [Fact]
    public void OverwriteConfirmation_Expires()
    {
        DateTime CurrentTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Guid OfferId = Guid.NewGuid();

        Transference.UtcNowProvider = () => CurrentTime;
        Transference.RegisterOverwriteConfirmationForTesting(OfferId, "Foo.dll");

        CurrentTime = CurrentTime.Add(TimeSpan.FromMinutes(10));

        Assert.False(Transference.TryConsumeOverwriteConfirmationForTesting(OfferId, "Foo.dll"));
    }

    /// <summary>
    /// Restores shared transfer test state.
    /// </summary>
    public void Dispose()
    {
        Transference.UtcNowProvider = originalUtcNowProvider;
        Transference.ResetOverwriteConfirmationsForTesting();
    }
}
