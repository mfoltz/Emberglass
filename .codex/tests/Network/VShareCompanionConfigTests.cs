using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers VShare companion config metadata, path safety, and managed overwrite behavior.
/// </summary>
public sealed class VShareCompanionConfigTests
{
    [Fact]
    public void BuildSharedClientModCatalogEntries_ReportsCompanionConfigCount()
    {
        PluginShareMetadataStore.PluginShareMetadata metadata = new(
            "mfoltz/Eclipse",
            "v1.3.17-pre",
            "Eclipse.dll",
            true,
            true,
            Array.Empty<string>(),
            Array.Empty<string>(),
            string.Empty,
            new[]
            {
                new PluginShareMetadataStore.PluginShareConfigMetadata("Eclipse.cfg", "Eclipse.cfg")
            });

        var metadataEntries = new Dictionary<string, PluginShareMetadataStore.PluginShareMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eclipse"] = metadata
        };

        Transference.SharedClientModPreviewEntry[] entries =
            Transference.BuildSharedClientModCatalogEntriesForTesting(
                new[]
                {
                    ("Eclipse.dll", "Eclipse", "Eclipse", "abc123", false)
                },
                metadataEntries);

        Transference.SharedClientModPreviewEntry entry = Assert.Single(entries);
        Assert.Equal(1, entry.ConfigCount);

        string description = Transference.BuildSharedModsMenuDescriptionForTesting(entries);
        Assert.Contains("Config files: 1", description);
    }

    [Fact]
    public void ConfigPathResolution_RejectsPathsOutsideAllowedRoots()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "emberglass-config-paths-" + Guid.NewGuid().ToString("N"));
        string serverConfigsRoot = Path.Combine(tempRoot, "ServerConfigs");
        string configRoot = Path.Combine(tempRoot, "Config");

        try
        {
            string stagedPluginRoot = Path.Combine(serverConfigsRoot, "Eclipse");
            Directory.CreateDirectory(stagedPluginRoot);
            File.WriteAllText(Path.Combine(stagedPluginRoot, "Eclipse.cfg"), "enabled=true");

            Assert.True(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
                tempRoot,
                "Eclipse",
                "Eclipse.cfg",
                out string sourcePath,
                out string sourceError));
            Assert.EndsWith(Path.Combine("ServerConfigs", "Eclipse", "Eclipse.cfg"), sourcePath);
            Assert.Equal(string.Empty, sourceError);

            Assert.True(VShareConfigCompanion.TryResolveClientConfigTargetForTesting(
                configRoot,
                "Eclipse.cfg",
                out string targetPath,
                out string targetError));
            Assert.EndsWith(Path.Combine("Config", "Eclipse.cfg"), targetPath);
            Assert.Equal(string.Empty, targetError);

            Assert.False(VShareConfigCompanion.TryResolveClientConfigTargetForTesting(
                configRoot,
                "..\\Eclipse.cfg",
                out _,
                out string traversalError));
            Assert.Contains("traversal", traversalError, StringComparison.OrdinalIgnoreCase);

            Assert.False(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
                tempRoot,
                "Eclipse",
                Path.Combine(stagedPluginRoot, "Eclipse.cfg"),
                out _,
                out string rootedError));
            Assert.Contains("relative", rootedError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ApplyClientConfig_OverwritesOnlyUneditedVShareManagedFiles()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "emberglass-managed-configs-" + Guid.NewGuid().ToString("N"));
        string configRoot = Path.Combine(tempRoot, "Config");
        string manifestPath = Path.Combine(configRoot, "Emberglass", "VShareManagedConfigs.json");
        byte[] firstPayload = Encoding.UTF8.GetBytes("enabled=true");
        byte[] secondPayload = Encoding.UTF8.GetBytes("enabled=false");

        try
        {
            VShareConfigApplyResult first = VShareConfigCompanion.ApplyClientConfigForTesting(
                firstPayload,
                configRoot,
                manifestPath,
                "Eclipse",
                "Eclipse",
                "Eclipse.cfg",
                "Eclipse.cfg",
                new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero));

            Assert.True(first.Success);
            Assert.Equal(VShareConfigApplyStatus.AppliedNewFile, first.Status);
            Assert.Equal("enabled=true", File.ReadAllText(Path.Combine(configRoot, "Eclipse.cfg")));

            VShareConfigApplyResult overwrite = VShareConfigCompanion.ApplyClientConfigForTesting(
                secondPayload,
                configRoot,
                manifestPath,
                "Eclipse",
                "Eclipse",
                "Eclipse.cfg",
                "Eclipse.cfg",
                new DateTimeOffset(2026, 5, 22, 12, 5, 0, TimeSpan.Zero));

            Assert.True(overwrite.Success);
            Assert.Equal(VShareConfigApplyStatus.AppliedManagedOverwrite, overwrite.Status);
            Assert.Equal("enabled=false", File.ReadAllText(Path.Combine(configRoot, "Eclipse.cfg")));

            File.WriteAllText(Path.Combine(configRoot, "Eclipse.cfg"), "user-edited=true");

            VShareConfigApplyResult edited = VShareConfigCompanion.ApplyClientConfigForTesting(
                firstPayload,
                configRoot,
                manifestPath,
                "Eclipse",
                "Eclipse",
                "Eclipse.cfg",
                "Eclipse.cfg",
                new DateTimeOffset(2026, 5, 22, 12, 10, 0, TimeSpan.Zero));

            Assert.False(edited.Success);
            Assert.Equal(VShareConfigApplyStatus.SkippedLocallyEdited, edited.Status);
            Assert.Equal("user-edited=true", File.ReadAllText(Path.Combine(configRoot, "Eclipse.cfg")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/rooted.cfg")]
    [InlineData("C:drive.cfg")]
    [InlineData("nested/../Eclipse.cfg")]
    [InlineData("nested/./Eclipse.cfg")]
    [InlineData("nested//Eclipse.cfg")]
    [InlineData("nested./Eclipse.cfg")]
    [InlineData("nested /Eclipse.cfg")]
    [InlineData("nested/Eclipse.cfg:")]
    [InlineData("nested/NUL.cfg")]
    [InlineData("Emberglass/owned.cfg")]
    [InlineData("EMBERG~1/owned.cfg")]
    [InlineData("nested/.vshare-config-private")]
    [InlineData("123456789012345678901234567890.cfg")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9.cfg")]
    public void TargetSyntax_RejectsNoncanonicalOrReservedNames(string target)
        => Assert.False(VShareConfigCompanion.TryValidateClientConfigTargetSyntax(target, out _));

    [Fact]
    public void ResolvePaths_CanonicalizesSeparatorsAndValidatesTheSource()
    {
        using var fixture = new ConfigWriteFixture();
        string stagedRoot = Path.Combine(fixture.Root, "ServerConfigs", "Eclipse", "nested");
        Directory.CreateDirectory(stagedRoot);
        string stagedFile = Path.Combine(stagedRoot, "Eclipse.cfg");
        File.WriteAllText(stagedFile, "staged");

        Assert.True(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
            fixture.Root, "Eclipse", "nested\\Eclipse.cfg", out string source, out _));
        Assert.Equal(stagedFile, source);
        foreach (string key in new[] { "..", "Eclipse/other", new string('x', 33) })
        {
            Assert.False(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
                fixture.Root, key, "nested/Eclipse.cfg", out _, out _));
        }

        Assert.False(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
            fixture.Root, "Eclipse", "nested/missing.cfg", out _, out _));
        Assert.False(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
            fixture.Root, "Eclipse", "nested", out _, out _));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "directory.cfg"));
        Assert.True(VShareConfigCompanion.TryValidateClientConfigTargetSyntax("directory.cfg", out _));
        Assert.False(VShareConfigCompanion.TryResolveClientConfigTargetForTesting(
            fixture.Root, "directory.cfg", out _, out _));

        Assert.Equal(VShareConfigApplyStatus.AppliedNewFile,
            fixture.Apply("first", source: "nested\\Eclipse.cfg", target: "nested/Eclipse.cfg").Status);
        Assert.Equal(VShareConfigApplyStatus.AppliedManagedOverwrite,
            fixture.Apply("second", source: "nested/Eclipse.cfg", target: "nested\\Eclipse.cfg").Status);
        Assert.Equal("second", File.ReadAllText(Path.Combine(fixture.Root, "nested", "Eclipse.cfg")));
    }

    [Fact]
    public void ResolvePaths_RejectsLinkedRootsDirectoriesAndFiles()
    {
        using var fixture = new ConfigWriteFixture();
        string client = Directory.CreateDirectory(Path.Combine(fixture.Root, "client")).FullName;
        string staged = Directory.CreateDirectory(Path.Combine(fixture.Root, "ServerConfigs", "Eclipse")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(fixture.Root, "other")).FullName;
        string localFile = Path.Combine(other, "Local.cfg");
        File.WriteAllText(localFile, "kept");
        var links = new List<(string Path, bool Directory)>();

        try
        {
            string linkedRoot = AddLink(Path.Combine(fixture.Root, "linked-root"), other, directory: true);
            AddLink(Path.Combine(client, "nested"), other, directory: true);
            AddLink(Path.Combine(staged, "nested"), other, directory: true);
            string linkedTarget = AddLink(Path.Combine(client, "Link.cfg"), localFile, directory: false);
            string linkedSource = AddLink(Path.Combine(staged, "Link.cfg"), localFile, directory: false);

            Assert.False(VShareConfigCompanion.TryResolveClientConfigTargetForTesting(
                linkedRoot, "Local.cfg", out _, out string rootError));
            Assert.Contains("link", rootError);
            Assert.False(VShareConfigCompanion.TryResolveClientConfigTargetForTesting(
                client, "nested/Local.cfg", out _, out string directoryError));
            Assert.Contains("link", directoryError);
            Assert.False(VShareConfigCompanion.TryResolveClientConfigTargetForTesting(
                client, Path.GetFileName(linkedTarget), out _, out string fileError));
            Assert.Contains("link", fileError);
            Assert.False(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
                fixture.Root, "Eclipse", "nested/Local.cfg", out _, out string serverDirectoryError));
            Assert.Contains("link", serverDirectoryError);
            Assert.False(VShareConfigCompanion.TryResolveServerConfigSourceForTesting(
                fixture.Root, "Eclipse", Path.GetFileName(linkedSource), out _, out string serverFileError));
            Assert.Contains("link", serverFileError);
            Assert.Equal("kept", File.ReadAllText(localFile));
        }
        finally
        {
            foreach (var link in links)
            {
                if (link.Directory) Directory.Delete(link.Path);
                else File.Delete(link.Path);
            }
        }

        string AddLink(string path, string destination, bool directory)
        {
            if (OperatingSystem.IsWindows())
            {
                // Directory junctions don't require the Windows symlink privilege.
                if (!directory)
                {
                    try
                    {
                        File.CreateSymbolicLink(path, destination);
                        links.Add((path, false));
                        return path;
                    }
                    catch (IOException) { destination = other; }
                    catch (UnauthorizedAccessException) { destination = other; }
                }

                using var command = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{path}\" \"{destination}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                })!;
                string output = command.StandardOutput.ReadToEnd();
                command.WaitForExit();
                Assert.True(command.ExitCode == 0, output);
                directory = true;
            }
            else if (directory) Directory.CreateSymbolicLink(path, destination);
            else File.CreateSymbolicLink(path, destination);

            links.Add((path, directory));
            return path;
        }
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{}")]
    [InlineData("{\"Entries\":null}")]
    [InlineData("{\"Entries\":[null]}")]
    public void ApplyClientConfig_DoesNotReplaceMalformedManifest(string manifest)
    {
        using var fixture = new ConfigWriteFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ManifestPath)!);
        File.WriteAllText(fixture.ManifestPath, manifest);

        Assert.Equal(VShareConfigApplyStatus.WriteFailed, fixture.Apply("server").Status);
        Assert.Equal(manifest, File.ReadAllText(fixture.ManifestPath));
        Assert.False(File.Exists(fixture.ConfigPath));
        fixture.AssertNoTemps();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyClientConfig_StopsWhenManifestOrManagedHashCannotBeRead(bool lockManifest)
    {
        using var fixture = new ConfigWriteFixture();
        Assert.True(fixture.Apply("previous").Success);
        string manifest = File.ReadAllText(fixture.ManifestPath);
        using (File.Open(lockManifest ? fixture.ManifestPath : fixture.ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(VShareConfigApplyStatus.WriteFailed, fixture.Apply("incoming").Status);
        }

        Assert.Equal("previous", File.ReadAllText(fixture.ConfigPath));
        Assert.Equal(manifest, File.ReadAllText(fixture.ManifestPath));
        fixture.AssertNoTemps();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyClientConfig_PreservesFileCreatedOrEditedBeforePublish(bool managed)
    {
        using var fixture = new ConfigWriteFixture();
        if (managed) Assert.True(fixture.Apply("previous").Success);
        string? manifest = managed ? File.ReadAllText(fixture.ManifestPath) : null;

        var result = fixture.Apply("incoming", step =>
        {
            if (step == VShareConfigWriteStep.BeforeConfigPublish) File.WriteAllText(fixture.ConfigPath, "local edit");
        });

        Assert.Equal(managed ? VShareConfigApplyStatus.SkippedLocallyEdited : VShareConfigApplyStatus.SkippedUnmanagedExisting,
            result.Status);
        Assert.Null(result.ManagedWrite);
        Assert.Equal("local edit", File.ReadAllText(fixture.ConfigPath));
        if (managed) Assert.Equal(manifest, File.ReadAllText(fixture.ManifestPath));
        else Assert.False(File.Exists(fixture.ManifestPath));
        fixture.AssertNoTemps();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ApplyClientConfig_WriteFailurePreservesPriorFiles(bool managed, bool failManifest)
    {
        using var fixture = new ConfigWriteFixture();
        if (managed) Assert.True(fixture.Apply("previous").Success);
        string? manifest = managed ? File.ReadAllText(fixture.ManifestPath) : null;

        var result = fixture.Apply("incoming", step =>
        {
            var failure = failManifest ? VShareConfigWriteStep.BeforeManifestPublish : VShareConfigWriteStep.BeforeConfigPublish;
            if (step != failure) return;
            if (step == VShareConfigWriteStep.BeforeManifestPublish)
            {
                Assert.Equal("incoming", File.ReadAllText(fixture.ConfigPath));
            }

            throw new IOException("injected publication failure");
        });

        Assert.Equal(VShareConfigApplyStatus.WriteFailed, result.Status);
        Assert.Null(result.ManagedWrite);
        if (managed)
        {
            Assert.Equal("previous", File.ReadAllText(fixture.ConfigPath));
            Assert.Equal(manifest, File.ReadAllText(fixture.ManifestPath));
        }
        else
        {
            Assert.False(File.Exists(fixture.ConfigPath));
            Assert.False(File.Exists(fixture.ManifestPath));
        }

        fixture.AssertNoTemps();
    }

    [Fact]
    public void ApplyClientConfig_UnsafeRollbackKeepsTheLocalEditAndRecoveryBackup()
    {
        using var fixture = new ConfigWriteFixture();
        Assert.True(fixture.Apply("previous").Success);
        string manifest = File.ReadAllText(fixture.ManifestPath);

        var result = fixture.Apply("incoming", step =>
        {
            if (step == VShareConfigWriteStep.BeforeManifestPublish)
            {
                throw new IOException("injected manifest failure");
            }

            if (step == VShareConfigWriteStep.BeforeRollback) File.WriteAllText(fixture.ConfigPath, "local edit");
        });

        Assert.Equal(VShareConfigApplyStatus.WriteFailed, result.Status);
        Assert.Null(result.ManagedWrite);
        Assert.Contains("Rollback did not complete", result.Message);
        string backup = Assert.Single(Directory.GetFiles(fixture.Root, ".vshare-config-*.bak"));
        Assert.Contains(backup, result.Message);
        Assert.Equal("previous", File.ReadAllText(backup));
        Assert.Equal("local edit", File.ReadAllText(fixture.ConfigPath));
        Assert.Equal(manifest, File.ReadAllText(fixture.ManifestPath));
        Assert.Equal(VShareConfigApplyStatus.SkippedLocallyEdited, fixture.Apply("retry").Status);
    }

    [Fact]
    public void ApplyClientConfig_DoesNotClobberManifestChangedDuringPublication()
    {
        using var fixture = new ConfigWriteFixture();
        Assert.True(fixture.Apply("previous").Success);
        string changedManifest = File.ReadAllText(fixture.ManifestPath) + "\n";

        var result = fixture.Apply("incoming", step =>
        {
            if (step == VShareConfigWriteStep.BeforeManifestPublish)
            {
                File.WriteAllText(fixture.ManifestPath, changedManifest);
            }
        });

        Assert.Equal(VShareConfigApplyStatus.WriteFailed, result.Status);
        Assert.Null(result.ManagedWrite);
        Assert.Equal("previous", File.ReadAllText(fixture.ConfigPath));
        Assert.Equal(changedManifest, File.ReadAllText(fixture.ManifestPath));
        fixture.AssertNoTemps();
    }

    sealed class ConfigWriteFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "emberglass-config-write-" + Guid.NewGuid().ToString("N"))).FullName;
        public string ConfigPath => Path.Combine(Root, "Eclipse.cfg");
        public string ManifestPath => Path.Combine(Root, "Emberglass", "VShareManagedConfigs.json");

        public VShareConfigApplyResult Apply(string text, Action<VShareConfigWriteStep>? checkpoint = null,
            string source = "Eclipse.cfg", string target = "Eclipse.cfg")
            => VShareConfigCompanion.ApplyClientConfigForTesting(Encoding.UTF8.GetBytes(text), Root, ManifestPath,
                "Eclipse", "Eclipse", source, target, DateTimeOffset.UnixEpoch, checkpoint!);

        public void AssertNoTemps() => Assert.Empty(Directory.GetFiles(Root, ".vshare-config-*", SearchOption.AllDirectories));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void ApplyClientConfig_SkipsUnmanagedExistingFiles()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "emberglass-unmanaged-configs-" + Guid.NewGuid().ToString("N"));
        string configRoot = Path.Combine(tempRoot, "Config");
        string manifestPath = Path.Combine(configRoot, "Emberglass", "VShareManagedConfigs.json");

        try
        {
            Directory.CreateDirectory(configRoot);
            string targetPath = Path.Combine(configRoot, "Eclipse.cfg");
            File.WriteAllText(targetPath, "local=true");

            VShareConfigApplyResult result = VShareConfigCompanion.ApplyClientConfigForTesting(
                Encoding.UTF8.GetBytes("server=true"),
                configRoot,
                manifestPath,
                "Eclipse",
                "Eclipse",
                "Eclipse.cfg",
                "Eclipse.cfg",
                DateTimeOffset.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(VShareConfigApplyStatus.SkippedUnmanagedExisting, result.Status);
            Assert.Null(result.ManagedWrite);
            Assert.Equal("local=true", File.ReadAllText(targetPath));
            Assert.False(File.Exists(manifestPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void HotloadConfig_RetainsOwnershipAfterGeneratedComments()
    {
        using var fixture = new HotloadConfigFixture();
        VShareConfigApplyResult initial = fixture.Apply(HotloadConfigFixture.Staged);
        var snapshot = fixture.Capture(initial);
        File.WriteAllText(fixture.ConfigPath, HotloadConfigFixture.Generated);

        Assert.NotNull(snapshot);
        Assert.True(VShareConfigCompanion.RetainAfterHotload(fixture.ManifestPath, snapshot));
        Assert.Equal(HotloadConfigFixture.Generated, File.ReadAllText(fixture.ConfigPath));
        Assert.Equal(VShareConfigApplyStatus.AppliedManagedOverwrite, fixture.Apply("[UIOptions]\nAttributeBuffs = false\n").Status);
    }

    [Fact]
    public void HotloadConfig_FutureCommentEditsAreStillProtected()
    {
        using var fixture = new HotloadConfigFixture();
        var snapshot = fixture.Capture(fixture.Apply(HotloadConfigFixture.Staged));
        File.WriteAllText(fixture.ConfigPath, HotloadConfigFixture.Generated);
        Assert.True(VShareConfigCompanion.RetainAfterHotload(fixture.ManifestPath, snapshot));
        const string edited = HotloadConfigFixture.Generated + "# My notes\n";
        File.WriteAllText(fixture.ConfigPath, edited);

        Assert.Equal(VShareConfigApplyStatus.SkippedLocallyEdited, fixture.Apply(HotloadConfigFixture.Staged).Status);
        Assert.Equal(edited, File.ReadAllText(fixture.ConfigPath));
    }

    [Theory]
    [InlineData("AttributeBuffs = true", "AttributeBuffs = false")]
    [InlineData("AttributeBuffs = true", "AttributeBuffs = true # Inline content matters")]
    [InlineData("[UIOptions]", "[OtherSection]")]
    [InlineData("## Plugin GUID: io.zfolmt.Eclipse", "## Plugin GUID: local.other")]
    public void HotloadConfig_DoesNotAdoptChangedSettingsOrWrongOwner(string before, string after)
    {
        using var fixture = new HotloadConfigFixture();
        var snapshot = fixture.Capture(fixture.Apply(HotloadConfigFixture.Staged));
        string edited = HotloadConfigFixture.Generated.Replace(before, after, StringComparison.Ordinal);
        File.WriteAllText(fixture.ConfigPath, edited);

        Assert.False(VShareConfigCompanion.RetainAfterHotload(fixture.ManifestPath, snapshot));
        Assert.Equal(VShareConfigApplyStatus.SkippedLocallyEdited, fixture.Apply(HotloadConfigFixture.Staged).Status);
        Assert.Equal(edited, File.ReadAllText(fixture.ConfigPath));
    }

    [Fact]
    public void HotloadConfig_RequiresTheFreshWriteAndExactBeforeHash()
    {
        using var fixture = new HotloadConfigFixture();
        VShareConfigApplyResult initial = fixture.Apply(HotloadConfigFixture.Staged);
        Assert.Null(fixture.Capture(initial, "local.other"));

        File.WriteAllText(fixture.ConfigPath, HotloadConfigFixture.Staged + "# Local edit before hotload\n");
        Assert.Null(fixture.Capture(initial));
        Assert.Null(fixture.Capture(fixture.Apply(HotloadConfigFixture.Staged)));
    }

    [Fact]
    public void HotloadConfig_DoesNotReplaceAManifestEntryThatChangedDuringLoad()
    {
        using var fixture = new HotloadConfigFixture();
        var snapshot = fixture.Capture(fixture.Apply(HotloadConfigFixture.Staged));
        Assert.Equal(VShareConfigApplyStatus.AppliedManagedOverwrite,
            fixture.Apply(HotloadConfigFixture.Staged, DateTimeOffset.UnixEpoch.AddMinutes(1)).Status);
        File.WriteAllText(fixture.ConfigPath, HotloadConfigFixture.Generated);

        Assert.False(VShareConfigCompanion.RetainAfterHotload(fixture.ManifestPath, snapshot));
        Assert.Equal(VShareConfigApplyStatus.SkippedLocallyEdited, fixture.Apply(HotloadConfigFixture.Staged).Status);
    }

    [Fact]
    public void HotloadConfig_FailedManifestPublicationDoesNotClaimTheRewrittenFile()
    {
        using var fixture = new HotloadConfigFixture();
        var snapshot = fixture.Capture(fixture.Apply(HotloadConfigFixture.Staged));
        Assert.NotNull(snapshot);
        string manifest = File.ReadAllText(fixture.ManifestPath);
        File.WriteAllText(fixture.ConfigPath, HotloadConfigFixture.Generated);

        Assert.False(VShareConfigCompanion.RetainAfterHotload(fixture.ManifestPath, snapshot, _ =>
            throw new IOException("injected manifest publish failure")));
        Assert.Equal(manifest, File.ReadAllText(fixture.ManifestPath));
        Assert.Equal(HotloadConfigFixture.Generated, File.ReadAllText(fixture.ConfigPath));
        Assert.Equal(VShareConfigApplyStatus.SkippedLocallyEdited, fixture.Apply(HotloadConfigFixture.Staged).Status);
    }

    sealed class HotloadConfigFixture : IDisposable
    {
        const string Guid = "io.zfolmt.Eclipse";
        const string Target = Guid + ".cfg";
        public const string Staged = "\uFEFF## Companion\r\n[UIOptions]\r\nAttributeBuffs = true\r\n";
        public const string Generated = "## Settings file was created by plugin Eclipse v1.3.17\n"
            + "## Plugin GUID: " + Guid + "\n\n"
            + "[UIOptions]\n\n## Enable bonus stats\n# Setting type: Boolean\n# Default value: false\nAttributeBuffs = true\n";

        readonly string root = Path.Combine(Path.GetTempPath(), "emberglass-hotload-config-" + System.Guid.NewGuid().ToString("N"));
        public string ConfigPath => Path.Combine(root, Target);
        public string ManifestPath => Path.Combine(root, "Emberglass", "VShareManagedConfigs.json");

        public VShareConfigApplyResult Apply(string text, DateTimeOffset? appliedUtc = null)
            => VShareConfigCompanion.ApplyClientConfigForTesting(
                Encoding.UTF8.GetBytes(text), root, ManifestPath, "Eclipse", "Eclipse", Target, Target,
                appliedUtc ?? DateTimeOffset.UnixEpoch);

        public VShareHotloadConfigSnapshot? Capture(VShareConfigApplyResult result, string guid = Guid)
            => VShareConfigCompanion.CaptureBeforeHotload(root, ManifestPath, guid,
                result.ManagedWrite is { } write ? new[] { write } : Array.Empty<VShareManagedConfigWrite>());

        public void Dispose()
        {
            File.Delete(ConfigPath);
            File.Delete(ManifestPath);
            Directory.Delete(Path.GetDirectoryName(ManifestPath)!);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void SharedModInstallResult_GatesCompanionConfigTransfers()
    {
        string source = File.ReadAllText(FindRepositoryFile("Network", "Transference.cs"));

        Assert.Contains("Register<SharedModInstallResult>(Direction.Serverbound", source);
        Assert.Contains("if (!result.Success)", source);
        Assert.Contains("InternalTransferRequest(user, TransferRequest.CreateConfig(", source);
    }

    [Fact]
    public void CompanionInstall_WaitsForEverySlotIncludingProtectedSkips()
    {
        DateTime now = DateTime.UnixEpoch;
        var install = new VShareCompanionInstall(2, 12, 7, now.AddMinutes(2));
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        Assert.True(install.TryBegin(0, first, 12, 7, now));
        Assert.True(install.TryBegin(1, second, 12, 7, now));
        install.Finish(1, second, new VShareConfigApplyResult(
            false, VShareConfigApplyStatus.SkippedUnmanagedExisting, string.Empty, "Existing.cfg"));
        Assert.False(install.IsComplete);
        Assert.Equal(1, install.PreservedConfigCount);
        Assert.False(install.CanApply(1, second, 12, 7, now));
        Assert.False(install.TryBegin(1, Guid.NewGuid(), 12, 7, now));

        var write = new VShareManagedConfigWrite("Eclipse", "Eclipse.cfg", "Eclipse.cfg", "hash", DateTimeOffset.UnixEpoch);
        install.Finish(0, first, new VShareConfigApplyResult(
            true, VShareConfigApplyStatus.AppliedNewFile, string.Empty, "Eclipse.cfg", write));
        Assert.True(install.IsComplete);
        Assert.Equal(write, Assert.Single(install.AppliedConfigs));
        Assert.Equal(1, install.PreservedConfigCount);
    }

    [Fact]
    public void CompanionInstall_RejectsUnrelatedDuplicateAndExpiredTransfers()
    {
        DateTime now = DateTime.UnixEpoch;
        var install = new VShareCompanionInstall(2, 12, 7, now.AddMinutes(2));
        Guid transferId = Guid.NewGuid();

        Assert.False(install.TryBegin(0, transferId, 13, 7, now));
        Assert.False(install.TryBegin(0, transferId, 12, 8, now));
        Assert.False(install.TryBegin(2, transferId, 12, 7, now));
        Assert.True(install.TryBegin(0, transferId, 12, 7, now));
        Assert.False(install.TryBegin(0, Guid.NewGuid(), 12, 7, now));
        Assert.False(install.TryBegin(1, transferId, 12, 7, now));
        Assert.True(install.CanApply(0, transferId, 12, 7, now));
        Assert.False(install.CanApply(0, Guid.NewGuid(), 12, 7, now));
        Assert.False(install.CanApply(0, transferId, 12, 8, now));
        Assert.False(install.CanApply(0, transferId, 12, 7, now.AddMinutes(2)));
        Assert.False(install.IsComplete);
    }

    [Fact]
    public void CompanionInstall_StopsOnFailure()
    {
        DateTime now = DateTime.UnixEpoch;
        foreach (var status in new[] { VShareConfigApplyStatus.RejectedInvalidPath, VShareConfigApplyStatus.WriteFailed })
        {
            var install = new VShareCompanionInstall(2, 12, 7, now.AddMinutes(2));
            Guid transferId = Guid.NewGuid();
            Assert.True(install.TryBegin(0, transferId, 12, 7, now));
            install.Finish(0, transferId, VShareCompanionInstall.IsResolved(status));
            Assert.True(install.Failed);
            Assert.False(install.IsComplete);
            Assert.False(install.TryBegin(1, Guid.NewGuid(), 12, 7, now));
        }
    }

    [Fact]
    public void TransferSession_CompanionCorrelationRoundTripsWithinPacketBudget()
    {
        Type type = typeof(Transference.TransferSession);
        Guid modId = Guid.NewGuid();
        byte[] emptyDigest = Array.Empty<byte>();
        var mod = new Transference.TransferSession(modId, 100, "Eclipse.dll", emptyDigest, emptyDigest, companionCount: 2);
        var config = new Transference.TransferSession(
            Guid.NewGuid(), 40, "io.zfolmt.Eclipse.cfg", emptyDigest, emptyDigest, VShareTransferAssetKind.Config,
            "Eclipse", "io.zfolmt.Eclipse.cfg", "io.zfolmt.Eclipse.cfg", modId, companionIndex: 1);

        var pack = Serialization.GetPacker(type);
        var unpack = Serialization.GetUnpacker(type);
        byte[] payload = pack(config);
        Assert.InRange(payload.Length, 1, Registry.Const.PACKET_BYTES);
        Assert.Equal(2, ((Transference.TransferSession)unpack(pack(mod))).CompanionCount);
        var decoded = (Transference.TransferSession)unpack(payload);
        Assert.Equal(modId, decoded.ParentTransferId);
        Assert.Equal(1, decoded.CompanionIndex);
        Assert.Equal(config.ConfigTargetString, decoded.ConfigTargetString);
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
