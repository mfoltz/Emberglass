using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Guards the plugin-load ordering that dedicated-server startup depends on.
/// </summary>
public sealed class GameBootstrapPatchStartupOrderTests
{
    /// <summary>
    /// Ensures Unity component attachment is deferred until the game bootstrap update has begun.
    /// </summary>
    [Fact]
    public void VBehaviourInitialization_IsDeferredUntilGameBootstrapUpdate()
    {
        string source = File.ReadAllText(FindRepositoryFile("Patches", "Shared", "OnInitialize.cs"));
        int updatePostfixIndex = source.IndexOf("static void UpdatePostfix()", StringComparison.Ordinal);
        int behaviourInitializeIndex = source.IndexOf("VBehaviour.Initialize();", StringComparison.Ordinal);
        int mainThreadInvokerIndex = source.IndexOf(
            "VBehaviour.MainThreadInvoker = new MainThreadInvoker();",
            StringComparison.Ordinal);
        int serverChatPatchIndex = source.IndexOf("ChatMessageSystemPatch.Initialize();", StringComparison.Ordinal);
        int vshareInitializeIndex = source.IndexOf("VShare.Initialize();", StringComparison.Ordinal);

        Assert.True(updatePostfixIndex >= 0);
        Assert.True(behaviourInitializeIndex > updatePostfixIndex);
        Assert.True(mainThreadInvokerIndex > updatePostfixIndex);
        Assert.True(serverChatPatchIndex > updatePostfixIndex);
        Assert.True(vshareInitializeIndex > updatePostfixIndex);
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
