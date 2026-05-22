using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the menu-click consent window for server-shared mod offers.
/// </summary>
public sealed class VShareMenuRequestAutoAcceptTests
{
    /// <summary>
    /// Ensures only catalog-listed offers are auto-accepted during the request window.
    /// </summary>
    [Fact]
    public void ShouldAutoAcceptSharedModOffer_RequiresCatalogMatchAndActiveWindow()
    {
        DateTime now = new(2026, 5, 22, 2, 0, 0, DateTimeKind.Utc);
        DateTime expiresAt = now.AddSeconds(15);
        var catalogFiles = new[] { "Eclipse.dll", "RetroCamera.dll" };

        Assert.True(Transference.ShouldAutoAcceptSharedModOfferForTesting(
            "Eclipse.dll",
            now,
            expiresAt,
            catalogFiles));
        Assert.False(Transference.ShouldAutoAcceptSharedModOfferForTesting(
            "ServerOnly.dll",
            now,
            expiresAt,
            catalogFiles));
        Assert.False(Transference.ShouldAutoAcceptSharedModOfferForTesting(
            "RetroCamera.dll",
            now.AddSeconds(16),
            expiresAt,
            catalogFiles));
    }
}
