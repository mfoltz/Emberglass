using BepInEx.Configuration;

namespace Emberglass.Network;

/// <summary>
/// Configuration-backed VShare transfer throttling settings.
/// </summary>
internal sealed class VShareTransferSettings
{
    public const int DefaultTransferWorkBudgetMs = 2;
    public const int DefaultMaxTransferWorkStepsPerFrame = 8;
    public const int DefaultMaxActiveOutgoingTransfers = 2;

    const string Section = "VShare";

    VShareTransferSettings(
        int transferWorkBudgetMs,
        int maxTransferWorkStepsPerFrame,
        int maxActiveOutgoingTransfers)
    {
        TransferWorkBudgetMs = transferWorkBudgetMs;
        MaxTransferWorkStepsPerFrame = maxTransferWorkStepsPerFrame;
        MaxActiveOutgoingTransfers = maxActiveOutgoingTransfers;
    }

    /// <summary>
    /// Gets the per-frame transfer work time budget in milliseconds.
    /// </summary>
    public int TransferWorkBudgetMs { get; }

    /// <summary>
    /// Gets the maximum transfer work steps processed per frame.
    /// </summary>
    public int MaxTransferWorkStepsPerFrame { get; }

    /// <summary>
    /// Gets the maximum active outgoing transfers.
    /// </summary>
    public int MaxActiveOutgoingTransfers { get; }

    /// <summary>
    /// Binds settings to the BepInEx config file and returns a clamped snapshot.
    /// </summary>
    /// <param name="configFile">The config file to bind against.</param>
    /// <returns>The clamped settings snapshot.</returns>
    public static VShareTransferSettings Bind(ConfigFile configFile)
    {
        if (configFile is null)
        {
            throw new ArgumentNullException(nameof(configFile));
        }

        int transferWorkBudgetMs = configFile.Bind(
            Section,
            "TransferWorkBudgetMs",
            DefaultTransferWorkBudgetMs,
            new ConfigDescription(
                "Per-frame transfer work time budget in milliseconds.",
                new AcceptableValueRange<int>(1, 10))).Value;

        int maxTransferWorkStepsPerFrame = configFile.Bind(
            Section,
            "MaxTransferWorkStepsPerFrame",
            DefaultMaxTransferWorkStepsPerFrame,
            new ConfigDescription(
                "Maximum transfer work steps processed per frame across all transfers.",
                new AcceptableValueRange<int>(1, 64))).Value;

        int maxActiveOutgoingTransfers = configFile.Bind(
            Section,
            "MaxActiveOutgoingTransfers",
            DefaultMaxActiveOutgoingTransfers,
            new ConfigDescription(
                "Maximum outgoing VShare transfers allowed to actively send at once; extra accepted transfers queue fairly.",
                new AcceptableValueRange<int>(1, 8))).Value;

        return new VShareTransferSettings(
            Math.Clamp(transferWorkBudgetMs, 1, 10),
            Math.Clamp(maxTransferWorkStepsPerFrame, 1, 64),
            Math.Clamp(maxActiveOutgoingTransfers, 1, 8));
    }
}
