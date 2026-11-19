using System.Text;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

public sealed class VShareClientNoticeTests
{
    const string FileName = "Eclipse.dll";
    const long Generation = 4;

    [Fact]
    public void Start_RequiresAcceptedIdNameAndSession()
    {
        var notices = new VShareClientNotices();
        Guid id = Guid.NewGuid();
        notices.Start(id, FileName, Generation);
        Assert.Empty(Drain(notices));

        notices.Accept(id, FileName, Generation);
        notices.Start(Guid.NewGuid(), FileName, Generation);
        notices.Start(id, "Other.dll", Generation);
        notices.Start(id, FileName, Generation + 1);
        notices.Finish(id, FileName, Generation, VShareNoticeOutcome.Failed);
        Assert.Empty(Drain(notices));

        notices.Start(id, "eclipse.dll", Generation);
        notices.Start(id, FileName, Generation);
        Assert.Contains("<color=#EEEEEE>Downloading Eclipse...</color>", Assert.Single(Drain(notices)));
    }

    [Fact]
    public void Progress_ReportsOnlyNewMilestonesAndNoneAfterLastByte()
    {
        var (notices, id) = Start();
        notices.Progress(id, FileName, Generation, 50, allBytesReceived: false);
        notices.Progress(id, FileName, Generation, 25, allBytesReceived: false);
        notices.Progress(id, FileName, Generation, 50, allBytesReceived: false);
        notices.Progress(id, FileName, Generation, 75, allBytesReceived: true);
        Assert.Contains("<color=#44EEDD>50%</color>", Assert.Single(Drain(notices)));

        var (small, smallId) = Start();
        small.Progress(smallId, FileName, Generation, 75, allBytesReceived: true);
        Assert.Empty(Drain(small));
    }

    [Fact]
    public void Finish_EmitsOneOutcomeAndSummarizesCompanions()
    {
        var (notices, id) = Start();
        notices.Progress(id, FileName, Generation, 75, allBytesReceived: false);
        notices.Finish(id, FileName, Generation, VShareNoticeOutcome.Ready, configsApplied: 2, configsKept: 1);
        notices.Finish(id, FileName, Generation, VShareNoticeOutcome.Failed);
        notices.Start(id, FileName, Generation);
        string[] messages = Drain(notices);
        Assert.Equal(2, messages.Length);
        Assert.Contains("75%", messages[0]);
        Assert.Contains("<color=#A9DF8F>Ready.</color>", messages[1]);
        Assert.Contains("2 configs applied.", messages[1]);
        Assert.Contains("<color=#F3CA75>Local config kept.</color>", messages[1]);
    }

    [Fact]
    public void Finish_DistinguishesInstallSkipAndFailureOutcomes()
    {
        foreach (var (outcome, label) in new[]
        {
            (VShareNoticeOutcome.Installed, "<color=#A9DF8F>Installed.</color>"),
            (VShareNoticeOutcome.ExistingFilesKept, "<color=#F3CA75>Existing files kept.</color>"),
            (VShareNoticeOutcome.TimedOut, "<color=#FF9A94>Timed out.</color>"),
            (VShareNoticeOutcome.Failed, "<color=#FF9A94>Download failed.</color>"),
            (VShareNoticeOutcome.ConfigFailed, "Installed; not loaded. Config setup failed."),
            (VShareNoticeOutcome.ConfigTimedOut, "Installed; not loaded. Config setup timed out."),
            (VShareNoticeOutcome.LoadFailed, "Installed; could not load.")
        })
        {
            var (notices, id) = Start();
            notices.Finish(id, FileName, Generation, outcome);
            string message = Assert.Single(Drain(notices));
            Assert.Contains(label, message);
            Assert.DoesNotContain("Ready.", message);
        }
    }

    [Fact]
    public void Reset_DropsQueuedMessagesAndActiveOffers()
    {
        var (notices, id) = Start();
        notices.Progress(id, FileName, Generation, 25, allBytesReceived: false);
        Assert.False(notices.TryDequeue(Generation + 1, out _));
        notices.Progress(id, FileName, Generation, 50, allBytesReceived: false);
        notices.Reset();
        notices.Finish(id, FileName, Generation, VShareNoticeOutcome.Ready);
        Assert.Empty(Drain(notices));
    }

    [Fact]
    public void Formatter_TreatsTheFilenameAsLiteralBoundedText()
    {
        var notices = new VShareClientNotices();
        Guid id = Guid.NewGuid();
        string untrusted = "folder/<color=#000000>\n\u202E" + new string('\u754C', 600) + ".ZIP";
        notices.Accept(id, untrusted, Generation);
        notices.Start(id, untrusted, Generation);
        notices.Finish(id, untrusted, Generation, VShareNoticeOutcome.Ready, int.MaxValue, int.MaxValue);

        string[] messages = Drain(notices);
        Assert.Equal(2, messages.Length);
        foreach (string message in messages)
        {
            Assert.Contains("<color=#44EEDD>[VShare]</color>", message);
            Assert.Contains("[color=#000000] ", message);
            Assert.DoesNotContain("<color=#000000>", message);
            Assert.DoesNotContain("\n", message, StringComparison.Ordinal);
            Assert.DoesNotContain("\u202E", message, StringComparison.Ordinal);
            Assert.DoesNotContain(".ZIP", message);
            Assert.InRange(Encoding.UTF8.GetByteCount(message), 1, VShareClientNotices.MaxUtf8Bytes);
        }
    }

    [Fact]
    public void Queue_KeepsTerminalNoticesWhenDroppingOverflowProgress()
    {
        var (notices, id) = Start();
        notices.Finish(id, FileName, Generation, VShareNoticeOutcome.Ready);
        for (int i = 0; i <= VShareClientNotices.QueueCapacity; i++)
        {
            Guid offer = Guid.NewGuid();
            notices.Accept(offer, "Other.dll", Generation);
            notices.Start(offer, "Other.dll", Generation);
        }

        string[] messages = Drain(notices);
        Assert.Equal(VShareClientNotices.QueueCapacity, messages.Length);
        Assert.Contains("Ready.", messages[0]);
    }

    [Fact]
    public void ChatPatch_UsesNativeLocalSystemMessageWithoutSending()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Emberglass.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string patch = File.ReadAllText(Path.Combine(directory.FullName, "Patches", "Client", "ClientChatSystemPatch.cs"));
        const string localCall = "ClientSystemChatUtils.AddLocalMessage(__instance.EntityManager, message, ServerChatMessageType.System)";
        Assert.Contains(localCall, patch);
        int filterAt = patch.IndexOf("FilterPackets(__instance);", StringComparison.Ordinal);
        Assert.True(filterAt >= 0 && filterAt < patch.IndexOf(localCall, StringComparison.Ordinal));
        Assert.Contains("!VWorld.IsClient || !VNetwork.IsReady", patch);
        Assert.DoesNotContain("SendNetworkEventTag", patch);
        Assert.DoesNotContain("SendSystemMessageToClient", patch);
        Assert.DoesNotContain("SendToServer", patch);
    }

    static (VShareClientNotices Notices, Guid Id) Start()
    {
        var notices = new VShareClientNotices();
        Guid id = Guid.NewGuid();
        notices.Accept(id, FileName, Generation);
        notices.Start(id, FileName, Generation);
        Assert.Single(Drain(notices));
        return (notices, id);
    }

    static string[] Drain(VShareClientNotices notices)
    {
        var messages = new List<string>();
        while (notices.TryDequeue(Generation, out string message))
        {
            messages.Add(message);
        }

        return messages.ToArray();
    }
}
