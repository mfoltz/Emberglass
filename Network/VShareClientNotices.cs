using System.Globalization;
using System.Text;

namespace Emberglass.Network;

internal enum VShareNoticeOutcome
{
    Ready,
    Installed,
    ExistingFilesKept,
    TimedOut,
    Failed,
    ConfigFailed,
    ConfigTimedOut,
    LoadFailed
}

internal sealed class VShareClientNotices
{
    public static VShareClientNotices Current { get; } = new();

    internal const int QueueCapacity = 32;
    internal const int MaxUtf8Bytes = 509;
    const int MaxNameCharacters = 48;
    const string Teal = "#44EEDD";
    const string White = "#EEEEEE";
    const string Green = "#A9DF8F";
    const string Gold = "#F3CA75";
    const string Coral = "#FF9A94";
    static readonly string prefix = Color(Teal, "[VShare]") + " ";

    readonly Dictionary<Guid, AcceptedOffer> offers = [];
    readonly LinkedList<Notice> queue = [];

    public void Accept(Guid id, string fileName, long generation)
        => offers.TryAdd(id, new AcceptedOffer(fileName, generation));

    public void Start(Guid id, string fileName, long generation)
    {
        if (!TryGet(id, fileName, generation, out var offer) || offer.Started)
        {
            return;
        }

        offer.Started = true;
        Enqueue(generation, Color(White, $"Downloading {offer.DisplayName}..."));
    }

    public void Progress(Guid id, string fileName, long generation, int milestone, bool allBytesReceived)
    {
        if (allBytesReceived || milestone is not (25 or 50 or 75)
            || !TryGet(id, fileName, generation, out var offer) || !offer.Started
            || milestone <= offer.LastMilestone)
        {
            return;
        }

        offer.LastMilestone = milestone;
        Enqueue(generation, Color(White, offer.DisplayName + ":") + " " + Color(Teal, $"{milestone}%"));
    }

    public void Finish(Guid id, string fileName, long generation, VShareNoticeOutcome outcome,
        int configsApplied = 0, int configsKept = 0)
    {
        if (!TryGet(id, fileName, generation, out var offer) || !offer.Started)
        {
            return;
        }

        offers.Remove(id);
        (string color, string status) = outcome switch
        {
            VShareNoticeOutcome.Ready => (Green, "Ready."),
            VShareNoticeOutcome.Installed => (Green, "Installed."),
            VShareNoticeOutcome.ExistingFilesKept => (Gold, "Existing files kept."),
            VShareNoticeOutcome.TimedOut => (Coral, "Timed out."),
            VShareNoticeOutcome.ConfigFailed => (Coral, "Installed; not loaded. Config setup failed."),
            VShareNoticeOutcome.ConfigTimedOut => (Coral, "Installed; not loaded. Config setup timed out."),
            VShareNoticeOutcome.LoadFailed => (Coral, "Installed; could not load."),
            _ => (Coral, "Download failed.")
        };

        string body = Color(White, offer.DisplayName + ":") + " " + Color(color, status);
        if (outcome is VShareNoticeOutcome.Ready or VShareNoticeOutcome.Installed)
        {
            if (configsApplied > 0)
            {
                body += " " + Color(White, configsApplied == 1 ? "1 config applied." : $"{configsApplied} configs applied.");
            }

            if (configsKept > 0)
            {
                body += " " + Color(Gold, configsKept == 1 ? "Local config kept." : $"{configsKept} local configs kept.");
            }
        }

        Enqueue(generation, body, terminal: true);
    }

    public bool TryDequeue(long generation, out string message)
    {
        while (queue.First is { } first)
        {
            queue.RemoveFirst();
            if (first.Value.Generation == generation)
            {
                message = first.Value.Message;
                return true;
            }
        }

        message = string.Empty;
        return false;
    }

    public void Reset()
    {
        offers.Clear();
        queue.Clear();
    }

    bool TryGet(Guid id, string fileName, long generation, out AcceptedOffer offer)
        => offers.TryGetValue(id, out offer) && offer.Generation == generation
            && string.Equals(fileName, offer.FileName, StringComparison.OrdinalIgnoreCase);

    void Enqueue(long generation, string body, bool terminal = false)
    {
        string message = prefix + body;
        if (Encoding.UTF8.GetByteCount(message) > MaxUtf8Bytes)
        {
            return;
        }

        if (queue.Count == QueueCapacity)
        {
            var discard = queue.First;
            while (discard is not null && discard.Value.Terminal)
            {
                discard = discard.Next;
            }

            if (discard is null && !terminal)
            {
                return;
            }

            queue.Remove(discard ?? queue.First);
        }

        queue.AddLast(new Notice(generation, message, terminal));
    }

    static string Color(string color, string value) => $"<color={color}>{value}</color>";

    static string DisplayName(string fileName)
    {
        int separator = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
        string name = fileName[(separator + 1)..];
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var builder = new StringBuilder();
        foreach (Rune rune in name.EnumerateRunes())
        {
            if (builder.Length + rune.Utf16SequenceLength > MaxNameCharacters)
            {
                break;
            }

            switch (rune.Value)
            {
                case '<': builder.Append('['); break;
                case '>': builder.Append(']'); break;
                default:
                    if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format)
                    {
                        if (builder.Length > 0 && builder[^1] != ' ')
                        {
                            builder.Append(' ');
                        }
                    }
                    else
                    {
                        builder.Append(rune.ToString());
                    }
                    break;
            }
        }

        return builder.Length == 0 ? "Shared mod" : builder.ToString().TrimEnd();
    }

    sealed class AcceptedOffer(string fileName, long generation)
    {
        public string FileName { get; } = fileName;
        public string DisplayName { get; } = VShareClientNotices.DisplayName(fileName);
        public long Generation { get; } = generation;
        public bool Started { get; set; }
        public int LastMilestone { get; set; }
    }

    readonly record struct Notice(long Generation, string Message, bool Terminal);
}
