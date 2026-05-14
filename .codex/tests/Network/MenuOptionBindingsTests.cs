using Emberglass.API.Client;
using Emberglass.API.Shared.Config;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers client menu binding response handling.
/// </summary>
public sealed class MenuOptionBindingsTests
{
    /// <summary>
    /// Ensures deferred response callback failures restore the optimistic local value.
    /// </summary>
    [Fact]
    public void ApplyServerResponseOrRollback_WhenApplyThrows_RestoresLocalValue()
    {
        var response = new ServerConfigChangeResponse<int>("General:Value", 42, true, string.Empty);
        long restoredRequestId = 0;

        MenuOptionBindings.ApplyServerResponseOrRollback(
            response,
            requestId: 7,
            bindingKey: "General:Value",
            applyServerResponse: (_, _) => throw new InvalidOperationException("apply failed"),
            restoreLocalValue: requestId => restoredRequestId = requestId);

        Assert.Equal(7, restoredRequestId);
    }

    /// <summary>
    /// Ensures successful response handling does not trigger rollback.
    /// </summary>
    [Fact]
    public void ApplyServerResponseOrRollback_WhenApplySucceeds_DoesNotRestoreLocalValue()
    {
        var response = new ServerConfigChangeResponse<int>("General:Value", 42, true, string.Empty);
        bool applied = false;
        bool restored = false;

        MenuOptionBindings.ApplyServerResponseOrRollback(
            response,
            requestId: 7,
            bindingKey: "General:Value",
            applyServerResponse: (_, _) => applied = true,
            restoreLocalValue: _ => restored = true);

        Assert.True(applied);
        Assert.False(restored);
    }
}
