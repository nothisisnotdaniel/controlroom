namespace ControlRoom.Server;

/// <summary>One backlog line: the text before the first <c>": "</c> and the text after it.</summary>
internal sealed record BacklogEntry(string Title, string Rest);

/// <summary>A backlog's lines grouped by mark, each group in file order.</summary>
internal sealed record Backlog(
    IReadOnlyList<BacklogEntry> Open,
    IReadOnlyList<BacklogEntry> Approval,
    IReadOnlyList<BacklogEntry> User,
    IReadOnlyList<BacklogEntry> Done)
{
    public static readonly Backlog Empty = new([], [], [], []);
}

/// <summary>Turns <c>backlog.md</c> lines into entries. Only lines starting with <c>- [ ] </c>, <c>- [?] </c>,
/// <c>- [h] </c> and <c>- [x] </c> / <c>- [X] </c> count; everything else is ignored.</summary>
internal static class BacklogParser
{
    private const string Separator = ": ";

    public static Backlog Parse(IEnumerable<string> lines)
    {
        List<BacklogEntry> open = [];
        List<BacklogEntry> approval = [];
        List<BacklogEntry> user = [];
        List<BacklogEntry> done = [];

        foreach (string line in lines)
        {
            List<BacklogEntry>? group = GroupFor(line, open, approval, user, done);
            if (group is not null)
            {
                group.Add(ToEntry(line[6..]));
            }
        }

        return new Backlog(open, approval, user, done);
    }

    public static BacklogEntry ToEntry(string text)
    {
        int index = text.IndexOf(Separator, StringComparison.Ordinal);
        if (index < 0)
        {
            return new BacklogEntry(text, "");
        }

        return new BacklogEntry(text[..index], text[(index + Separator.Length)..]);
    }

    private static List<BacklogEntry>? GroupFor(
        string line,
        List<BacklogEntry> open,
        List<BacklogEntry> approval,
        List<BacklogEntry> user,
        List<BacklogEntry> done)
    {
        if (line.Length < 6 || !line.StartsWith("- [", StringComparison.Ordinal) || line[4] != ']' || line[5] != ' ')
        {
            return null;
        }

        return line[3] switch
        {
            ' ' => open,
            '?' => approval,
            'h' => user,
            'x' or 'X' => done,
            _ => null,
        };
    }
}
