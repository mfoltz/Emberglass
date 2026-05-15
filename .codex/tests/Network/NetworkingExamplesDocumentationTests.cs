using Xunit;

namespace Emberglass.Tests.Network;

public sealed class NetworkingExamplesDocumentationTests
{
    [Fact]
    public void NetworkingExamplesDoc_ListsPrimitiveLadder()
    {
        string repoRoot = LocateRepoRoot();
        string docs = File.ReadAllText(Path.Combine(repoRoot, "docs", "networking-examples.md"));

        Assert.Contains("Ready Gate", docs);
        Assert.Contains("Typed Signal", docs);
        Assert.Contains("Client Registration", docs);
        Assert.Contains("Request and Receipt", docs);
        Assert.Contains("Server Push", docs);
        Assert.Contains("Soft Bridge Migration", docs);
        Assert.Contains("Bloodcraft/Eclipse", docs);
    }

    [Fact]
    public void Readme_LinksToNetworkingExamples()
    {
        string repoRoot = LocateRepoRoot();
        string readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));

        Assert.Contains("docs/networking-examples.md", readme);
        Assert.DoesNotContain("see PingPong", readme, StringComparison.OrdinalIgnoreCase);
    }

    static string LocateRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Emberglass.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate Emberglass repository root.");
    }
}
