using System.Reflection;
using Emberglass.API.Shared.Config;
using ProjectM.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers server config request rejection behavior.
/// </summary>
public sealed class ServerConfigChangeHandlersTests
{
    /// <summary>
    /// Ensures malformed requests return a rejection packet instead of throwing while constructing one.
    /// </summary>
    [Fact]
    public void HandleRequest_ReturnsRejectedResponseForNullRequest()
    {
        ServerConfigChangeResponse<int> Response = InvokeHandleRequest<int>(null);

        Assert.False(Response.IsAccepted);
        Assert.False(string.IsNullOrWhiteSpace(Response.BindingKey));
        Assert.Contains("not registered", Response.RejectionReason);
    }

    /// <summary>
    /// Ensures blank binding keys return a rejection packet with a valid response binding key.
    /// </summary>
    [Fact]
    public void HandleRequest_ReturnsRejectedResponseForBlankBindingKey()
    {
        var Request = new ServerConfigChangeRequest<int>
        {
            BindingKey = " ",
            Value = 42
        };

        ServerConfigChangeResponse<int> Response = InvokeHandleRequest(Request);

        Assert.False(Response.IsAccepted);
        Assert.False(string.IsNullOrWhiteSpace(Response.BindingKey));
        Assert.Equal(42, Response.Value);
        Assert.Contains("Binding key is required", Response.RejectionReason);
    }

    static ServerConfigChangeResponse<TValue> InvokeHandleRequest<TValue>(
        ServerConfigChangeRequest<TValue>? request)
    {
        MethodInfo Method = typeof(ServerConfigChangeHandlers).GetMethod(
            "HandleRequest",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("HandleRequest method not found.");

        MethodInfo GenericMethod = Method.MakeGenericMethod(typeof(TValue));
        object Result = GenericMethod.Invoke(null, new object?[] { default(User), request })
            ?? throw new InvalidOperationException("HandleRequest returned null.");
        return (ServerConfigChangeResponse<TValue>)Result;
    }
}
