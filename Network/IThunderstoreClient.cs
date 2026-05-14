namespace Emberglass.Network;

/// <summary>
/// Defines Thunderstore metadata lookup operations for transfer verification.
/// </summary>
public interface IThunderstoreClient
{
    /// <summary>
    /// Resolves the Thunderstore package identity for a plugin name using explicit metadata or thunderstore.toml.
    /// </summary>
    /// <param name="pluginName">The plugin name to resolve.</param>
    /// <param name="explicitIdentity">An explicit identity override when available.</param>
    /// <param name="identity">The resolved Thunderstore package identity.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the identity is resolved; otherwise <c>false</c>.</returns>
    bool TryResolvePackageForPlugin(
        string pluginName,
        ThunderstoreClient.ThunderstorePackageIdentity? explicitIdentity,
        out ThunderstoreClient.ThunderstorePackageIdentity identity,
        out string errorMessage);

    /// <summary>
    /// Begins an asynchronous lookup for the Thunderstore SHA-256 hash.
    /// </summary>
    /// <param name="identity">The resolved Thunderstore package identity.</param>
    /// <param name="cancellationToken">A cancellation token for the network request.</param>
    /// <param name="onComplete">Callback invoked with the hash lookup result.</param>
    void BeginPackageHashLookup(
        ThunderstoreClient.ThunderstorePackageIdentity identity,
        CancellationToken cancellationToken,
        Action<ThunderstoreClient.ThunderstoreHashResult> onComplete);
}
