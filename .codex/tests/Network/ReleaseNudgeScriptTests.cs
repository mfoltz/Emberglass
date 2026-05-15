using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the release hygiene gate that reminds maintainers to consider changelog and version updates.
/// </summary>
public sealed class ReleaseNudgeScriptTests
{
    /// <summary>
    /// Ensures the release nudge blocks by default, keeps an explicit warning-only escape hatch, and is documented next to the version bump flow.
    /// </summary>
    [Fact]
    public void ReleaseNudgeScript_BlocksByDefaultAndDocumentsWarnOnlyEscapeHatch()
    {
        string repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, ".codex", "scripts", "release-nudge.ps1");
        string releaseDocPath = Path.Combine(repoRoot, "docs", "release.md");
        string buildWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "build.yml");

        Assert.True(File.Exists(scriptPath), "release-nudge.ps1 should exist.");

        string script = File.ReadAllText(scriptPath);
        Assert.Contains("WarnOnly", script);
        Assert.DoesNotContain("FailOnNudge", script);
        Assert.Contains("exit 1", script);
        Assert.Contains("::warning", script);
        Assert.Contains("CHANGELOG.md", script);
        Assert.Contains("bump-version.ps1", script);

        string releaseDoc = File.ReadAllText(releaseDocPath);
        Assert.Contains("release-nudge.ps1", releaseDoc);
        Assert.Contains("blocks by default", releaseDoc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-WarnOnly", releaseDoc);

        string buildWorkflow = File.ReadAllText(buildWorkflowPath);
        Assert.Contains("release-nudge.ps1", buildWorkflow);
    }

    static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Emberglass.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate Emberglass repo root.");
    }
}
