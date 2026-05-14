using System;
using System.Collections.Generic;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Provides details about a settings reload operation.
/// </summary>
/// <typeparam name="TSettings">The settings type being reloaded.</typeparam>
public sealed class ReloadContext<TSettings>
    where TSettings : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReloadContext{TSettings}"/> class.
    /// </summary>
    /// <param name="oldSettings">The previous settings snapshot.</param>
    /// <param name="newSettings">The newly applied settings snapshot.</param>
    /// <param name="reason">The reason for the reload.</param>
    /// <param name="version">The version assigned to the new snapshot.</param>
    /// <param name="changedKeys">The keys that changed during the reload.</param>
    public ReloadContext(
        TSettings oldSettings,
        TSettings newSettings,
        string reason,
        long version,
        IReadOnlyCollection<string> changedKeys)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        OldSettings = oldSettings ?? throw new ArgumentNullException(nameof(oldSettings));
        NewSettings = newSettings ?? throw new ArgumentNullException(nameof(newSettings));
        Reason = reason;
        Version = version;
        ChangedKeys = changedKeys ?? throw new ArgumentNullException(nameof(changedKeys));
    }

    /// <summary>
    /// Gets the previous settings snapshot.
    /// </summary>
    public TSettings OldSettings { get; }

    /// <summary>
    /// Gets the newly applied settings snapshot.
    /// </summary>
    public TSettings NewSettings { get; }

    /// <summary>
    /// Gets the reason for the reload.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the version assigned to the new snapshot.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// Gets the keys that changed during the reload.
    /// </summary>
    public IReadOnlyCollection<string> ChangedKeys { get; }
}
