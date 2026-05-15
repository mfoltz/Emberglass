using Emberglass.Network.Examples;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for networking example packet DTOs.
/// </summary>
public sealed class NetworkingExamplePacketTests
{
    /// <summary>
    /// Ensures client feature registration DTOs default strings for serialization.
    /// </summary>
    [Fact]
    public void ClientFeatureRegistration_HasSerializationFriendlyDefaults()
    {
        ClientFeatureRegistration registration = new();

        Assert.Equal(string.Empty, registration.FeatureName);
        Assert.Equal(string.Empty, registration.FeatureVersion);
    }

    /// <summary>
    /// Ensures server setting receipts preserve the authoritative decision.
    /// </summary>
    [Fact]
    public void ServerSettingChangeReceipt_PreservesAuthoritativeDecision()
    {
        ServerSettingChangeReceipt receipt = new(
            "example.enabled",
            true,
            false,
            "Server controls this setting.");

        Assert.Equal("example.enabled", receipt.RequestKey);
        Assert.True(receipt.AuthoritativeValue);
        Assert.False(receipt.IsAccepted);
        Assert.Equal("Server controls this setting.", receipt.RejectionReason);
    }
}
