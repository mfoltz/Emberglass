using Emberglass.API.Shared;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for the public VEvents subscription surface.
/// </summary>
[Collection("Assembly setup")]
public sealed class VEventsTests : IDisposable
{
    /// <summary>
    /// Ensures subscription and unsubscription operate deterministically for registered modules.
    /// </summary>
    [Fact]
    public void ModuleRegistry_SubscribeAndUnsubscribe_DispatchesOnlyWhileSubscribed()
    {
        TestEventModule Module = new();
        int CallCount = 0;
        void Handler(TestEvent args) => CallCount += args.Value;

        Assert.True(VEvents.ModuleRegistry.TrySubscribe<TestEvent>(Handler));

        Module.Publish(2);
        Assert.Equal(2, CallCount);

        Assert.True(VEvents.ModuleRegistry.TryUnsubscribe<TestEvent>(Handler));

        Module.Publish(3);
        Assert.Equal(2, CallCount);
    }

    /// <summary>
    /// Ensures missing modules return false without forcing consumers through log-only behavior.
    /// </summary>
    [Fact]
    public void ModuleRegistry_TrySubscribeAndTryUnsubscribe_ReturnFalseWhenModuleMissing()
    {
        Assert.False(VEvents.ModuleRegistry.TrySubscribe<TestEvent>(_ => { }));
        Assert.False(VEvents.ModuleRegistry.TryUnsubscribe<TestEvent>(_ => { }));
    }

    /// <summary>
    /// Clears registered modules after each test.
    /// </summary>
    public void Dispose()
        => VEvents.ModuleRegistry.Uninitialize();

    /// <summary>
    /// Test event payload.
    /// </summary>
    sealed class TestEvent : VEvents.IGameEvent
    {
        /// <summary>
        /// Initializes a new instance of the test event.
        /// </summary>
        public TestEvent()
        {
        }

        /// <summary>
        /// Gets or sets the test value.
        /// </summary>
        public int Value { get; set; }
    }

    /// <summary>
    /// Test event module with a public publish helper.
    /// </summary>
    sealed class TestEventModule : VEvents.GameEvent<TestEvent>
    {
        /// <summary>
        /// Initializes the module and registers it with the registry.
        /// </summary>
        public TestEventModule()
            => VEvents.ModuleRegistry.Register(this);

        /// <summary>
        /// Raises a test event.
        /// </summary>
        /// <param name="value">Value to dispatch.</param>
        public void Publish(int value)
            => Raise(new TestEvent { Value = value });
    }
}
