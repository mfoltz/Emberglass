using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the operator-facing VShare provenance preflight script.
/// </summary>
public sealed class VSharePreflightScriptTests
{
    /// <summary>
    /// Ensures digest-only strict preflight can produce a local pass receipt from fixture metadata.
    /// </summary>
    [Fact]
    public void VShareProvenancePreflight_WritesPassingDigestReceipt()
    {
        using TempPreflightFixture fixture = TempPreflightFixture.Create(matchingDigest: true);

        ProcessResult result = RunPreflight(
            fixture,
            "-SkipAttestation");

        Assert.True(
            result.ExitCode == 0,
            result.StandardOutput + Environment.NewLine + result.StandardError + Environment.NewLine + ReadReceiptDebug(fixture.ReceiptRoot));
        using JsonDocument receipt = LoadReceipt(fixture.ReceiptRoot);
        JsonElement root = receipt.RootElement;
        Assert.Equal("passed", root.GetProperty("status").GetString());
        JsonElement asset = Assert.Single(root.GetProperty("assets").EnumerateArray());
        Assert.Equal("passed", asset.GetProperty("status").GetString());
        Assert.Equal("release-digest", asset.GetProperty("level").GetString());
        Assert.Equal("FriendlyEclipse.dll", asset.GetProperty("stagedFileName").GetString());
        Assert.Equal("Eclipse.dll", asset.GetProperty("releaseAssetName").GetString());
    }

    /// <summary>
    /// Ensures strict attestation mode has a fixed gh availability guard.
    /// </summary>
    [Fact]
    public void VShareProvenancePreflight_HasFixedGhAvailabilityGuard()
    {
        string scriptText = File.ReadAllText(GetScriptPath());

        Assert.Contains("Get-Command gh", scriptText);
        Assert.Contains("gh attestation verification requested", scriptText);
        Assert.Contains("attestation verify", scriptText);
        Assert.DoesNotContain("GhCommand", scriptText);
    }

    static ProcessResult RunPreflight(TempPreflightFixture fixture, string extraArgs)
    {
        string scriptPath = GetScriptPath();

        string arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
            $"-ServerModsPath \"{fixture.ServerModsPath}\" " +
            $"-ShareMetadataPath \"{fixture.ShareMetadataPath}\" " +
            $"-ReceiptRoot \"{fixture.ReceiptRoot}\" " +
            $"-ReleaseMetadataPath \"{fixture.ReleaseMetadataPath}\" " +
            extraArgs;

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    static string GetScriptPath()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "scripts",
            "vshare-provenance-preflight.ps1"));

    static JsonDocument LoadReceipt(string receiptRoot)
    {
        string receiptPath = Assert.Single(Directory.GetFiles(receiptRoot, "*.json"));
        return JsonDocument.Parse(File.ReadAllText(receiptPath));
    }

    static string ReadReceiptDebug(string receiptRoot)
    {
        string[] receiptPaths = Directory.GetFiles(receiptRoot, "*.json");
        return receiptPaths.Length == 0
            ? "No receipt was written."
            : File.ReadAllText(receiptPaths[0]);
    }

    readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    sealed class TempPreflightFixture : IDisposable
    {
        public string Root { get; }
        public string ServerModsPath { get; }
        public string ShareMetadataPath { get; }
        public string ReleaseMetadataPath { get; }
        public string ReceiptRoot { get; }

        TempPreflightFixture(
            string root,
            string serverModsPath,
            string shareMetadataPath,
            string releaseMetadataPath,
            string receiptRoot)
        {
            Root = root;
            ServerModsPath = serverModsPath;
            ShareMetadataPath = shareMetadataPath;
            ReleaseMetadataPath = releaseMetadataPath;
            ReceiptRoot = receiptRoot;
        }

        public static TempPreflightFixture Create(bool matchingDigest)
        {
            string root = Path.Combine(Path.GetTempPath(), "emberglass-vshare-preflight-" + Guid.NewGuid().ToString("N"));
            string serverModsPath = Path.Combine(root, "Server");
            string configPath = Path.Combine(root, "Emberglass");
            string receiptRoot = Path.Combine(root, "receipts");
            Directory.CreateDirectory(serverModsPath);
            Directory.CreateDirectory(configPath);
            Directory.CreateDirectory(receiptRoot);

            byte[] stagedBytes = { 1, 3, 5, 7 };
            string stagedPath = Path.Combine(serverModsPath, "FriendlyEclipse.dll");
            File.WriteAllBytes(stagedPath, stagedBytes);

            string digest = matchingDigest
                ? Convert.ToHexString(SHA256.HashData(stagedBytes)).ToLowerInvariant()
                : new string('b', 64);

            string shareMetadataPath = Path.Combine(configPath, "ShareMetadata.json");
            File.WriteAllText(
                shareMetadataPath,
                "{\n" +
                "  \"Plugins\": {\n" +
                "    \"FriendlyEclipse\": {\n" +
                "      \"GitHubRepo\": \"mfoltz/Eclipse\",\n" +
                "      \"GitHubTag\": \"v1.3.14-pre\",\n" +
                "      \"GitHubAssetName\": \"Eclipse.dll\",\n" +
                "      \"ClientSafe\": true,\n" +
                "      \"HotloadAllowed\": true,\n" +
                "      \"Tags\": [\"client\"],\n" +
                "      \"Categories\": []\n" +
                "    }\n" +
                "  }\n" +
                "}\n");

            string releaseMetadataPath = Path.Combine(root, "release-metadata.json");
            File.WriteAllText(
                releaseMetadataPath,
                "{\n" +
                "  \"releases\": [\n" +
                "    {\n" +
                "      \"owner\": \"mfoltz\",\n" +
                "      \"repo\": \"Eclipse\",\n" +
                "      \"tag\": \"v1.3.14-pre\",\n" +
                "      \"assets\": [\n" +
                "        {\n" +
                "          \"name\": \"Eclipse.dll\",\n" +
                "          \"digest\": \"sha256:" + digest + "\",\n" +
                "          \"browser_download_url\": \"https://github.com/mfoltz/Eclipse/releases/download/v1.3.14-pre/Eclipse.dll\"\n" +
                "        }\n" +
                "      ]\n" +
                "    }\n" +
                "  ]\n" +
                "}\n");

            return new TempPreflightFixture(root, serverModsPath, shareMetadataPath, releaseMetadataPath, receiptRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
