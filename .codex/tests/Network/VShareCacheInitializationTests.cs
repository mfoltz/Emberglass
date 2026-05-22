using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Guards VShare startup cache behavior.
/// </summary>
public sealed class VShareCacheInitializationTests
{
    /// <summary>
    /// Ensures VShare startup cache compression cannot deadlock on the transfer work queue.
    /// </summary>
    [Fact]
    public void CacheCompression_DoesNotUseQueuedTransferRoutine()
    {
        string source = File.ReadAllText(FindRepositoryFile("API", "Shared", "VShare.cs"));

        Assert.DoesNotContain("CompressChunkRoutine", source, StringComparison.Ordinal);
    }

    static string FindRepositoryFile(params string[] relativeParts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(relativeParts));
    }
}
