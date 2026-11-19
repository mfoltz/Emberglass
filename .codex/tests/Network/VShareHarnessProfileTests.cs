using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the VShare runtime harness profile inputs that should exercise companion configs.
/// </summary>
public sealed class VShareHarnessProfileTests
{
    [Fact]
    public void MenuDetailHarnessPrep_StagesCompanionConfigMetadataAndReceiptHints()
    {
        string repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, ".codex", "scripts", "Prepare-VShareMenuDetailBridgeHarness.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Contains("ServerConfigs", script);
        Assert.Contains("Configs = @(", script);
        Assert.Contains("Source = \"Eclipse.VShareProof.cfg\"", script);
        Assert.Contains("Target = \"Eclipse.VShareProof.cfg\"", script);
        Assert.Contains("Source = \"io.zfolmt.Eclipse.cfg\"", script);
        Assert.Contains("Target = \"io.zfolmt.Eclipse.cfg\"", script);
        Assert.Contains(@"BepInEx\config\io.zfolmt.Eclipse.cfg", script);
        Assert.Contains("## ProofSentinel = $proofSentinel", script);
        Assert.Contains("UseEmberglassBridge = true", script);
        Assert.Contains("ExperienceBar = true", script);
        Assert.Contains("AttributeBuffs = true", script);
        Assert.Contains("expectedClientEvidence", script);
        Assert.Contains("VShareManagedConfigs.json", script);
        Assert.Contains("ProofRunId = $runId", script);
        Assert.Contains("ProofSentinel = $proofSentinel", script);
        Assert.Contains("companionConfigValues", script);
        Assert.Contains("eclipseUiOptions", script);
        Assert.Contains("Set-IniValue -Path $bloodcraftConfigPath -Section \"Leveling\" -Key \"LevelingSystem\" -Value \"true\"", script);
        Assert.Contains("Set-IniValue -Path $bloodcraftConfigPath -Section \"Leveling\" -Key \"RestedXPSystem\" -Value \"true\"", script);
        Assert.Contains("Set-IniValue -Path $bloodcraftConfigPath -Section \"Leveling\" -Key \"ExpShare\" -Value \"true\"", script);
        Assert.Contains("Set-IniValue -Path $bloodcraftConfigPath -Section \"Legacies\" -Key \"LegacySystem\" -Value \"true\"", script);
        Assert.Contains("Set-IniValue -Path $bloodcraftConfigPath -Section \"Expertise\" -Key \"ExpertiseSystem\" -Value \"true\"", script);
        Assert.Contains("Set-IniValue -Path $bloodcraftConfigPath -Section \"Familiars\" -Key \"FamiliarSystem\" -Value \"true\"", script);
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
