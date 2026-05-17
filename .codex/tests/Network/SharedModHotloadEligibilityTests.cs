using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers VShare shared-mod hotload eligibility.
/// </summary>
public sealed class SharedModHotloadEligibilityTests
{
    /// <summary>
    /// Ensures hotload requires a distinct opt-in beyond client sharing eligibility.
    /// </summary>
    [Fact]
    public void SharedModHotloadEligibility_RequiresDllClientSafeAndHotloadAllowed()
    {
        PluginShareMetadataStore.PluginShareMetadata eligible = new(
            "mfoltz/Eclipse",
            "v1.3.14-pre",
            true,
            true,
            Array.Empty<string>(),
            Array.Empty<string>());

        PluginShareMetadataStore.PluginShareMetadata downloadOnly = eligible with
        {
            HotloadAllowed = false
        };

        PluginShareMetadataStore.PluginShareMetadata unsafeHotload = eligible with
        {
            ClientSafe = false,
            HotloadAllowed = true
        };

        Assert.True(Transference.IsSharedModHotloadAllowedForTesting(isZip: false, eligible));
        Assert.False(Transference.IsSharedModHotloadAllowedForTesting(isZip: true, eligible));
        Assert.False(Transference.IsSharedModHotloadAllowedForTesting(isZip: false, downloadOnly));
        Assert.False(Transference.IsSharedModHotloadAllowedForTesting(isZip: false, unsafeHotload));
    }
}
