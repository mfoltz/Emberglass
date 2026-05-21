using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers release workflow artifact attestation wiring.
/// </summary>
public sealed class WorkflowAttestationTests
{
    /// <summary>
    /// Ensures release artifacts are attested in prerelease publish jobs.
    /// </summary>
    [Fact]
    public void BuildWorkflow_AttestsPrereleaseDllArtifacts()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string workflowPath = Path.Combine(repoRoot, ".github", "workflows", "build.yml");
        string workflow = File.ReadAllText(workflowPath);

        Assert.Contains("attestations: write", workflow);
        Assert.Contains("id-token: write", workflow);
        Assert.Contains("uses: actions/attest@v4", workflow);
        Assert.Contains("Attest prerelease artifact", workflow);
        Assert.Contains("Attest feature-testing artifact", workflow);
        Assert.Contains("subject-path: ./bin/Release/net6.0/Emberglass.dll", workflow);
    }
}
