using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the one-click shared-mod request consent window.
/// </summary>
public sealed class SharedModRequestConsentTests
{
    /// <summary>
    /// Ensures the shared-mod request button authorizes clientbound offers only briefly.
    /// </summary>
    [Fact]
    public void SharedModRequestConsent_AutoAcceptsClientboundOffersWithinWindow()
    {
        DateTime requestedAt = new(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        var offer = new Transference.TransferOffer(Guid.NewGuid(), "Eclipse.dll".AsSpan(), clientbound: true, hotload: true);

        Transference.ClearSharedModRequestConsentForTesting();
        Transference.RecordSharedModRequestConsentForTesting(requestedAt);

        Assert.True(Transference.ShouldAutoAcceptSharedModOfferForTesting(offer, requestedAt.AddSeconds(10)));
        Assert.False(Transference.ShouldAutoAcceptSharedModOfferForTesting(offer, requestedAt.AddMinutes(6)));
    }

    /// <summary>
    /// Ensures one-click consent never auto-accepts serverbound or pre-consent offers.
    /// </summary>
    [Fact]
    public void SharedModRequestConsent_RejectsServerboundAndUnrequestedOffers()
    {
        DateTime requestedAt = new(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        var serverboundOffer = new Transference.TransferOffer(Guid.NewGuid(), "Tooling.dll".AsSpan(), clientbound: false, hotload: false);
        var clientboundOffer = new Transference.TransferOffer(Guid.NewGuid(), "Eclipse.dll".AsSpan(), clientbound: true, hotload: true);

        Transference.ClearSharedModRequestConsentForTesting();

        Assert.False(Transference.ShouldAutoAcceptSharedModOfferForTesting(clientboundOffer, requestedAt));

        Transference.RecordSharedModRequestConsentForTesting(requestedAt);

        Assert.False(Transference.ShouldAutoAcceptSharedModOfferForTesting(serverboundOffer, requestedAt.AddSeconds(10)));
    }
}
