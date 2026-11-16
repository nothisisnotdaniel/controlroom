using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>One <c>(after: …)</c> dependency: an item number, in another project when <c>Project</c> is set and in
/// the same project when it is <c>null</c>.</summary>
internal sealed record BacklogDependency(string? Project, int Number);

/// <summary>One backlog line. Old free-text lines: the text before the first <c>": "</c> and the text after it,
/// with no number. Numbered lines (<c>[#N](target) Title (after: …) → job</c>): the number, the story link
/// target, the title, the dependencies and the <c>→ job</c> tail as <c>Rest</c>. <c>Waiting</c> is filled in by
/// the backlogs endpoint.</summary>
internal sealed record BacklogEntry(
    string Title,
    string Rest,
    int? Number = null,
    string Mark = " ",
    string? Story = null,
    IReadOnlyList<BacklogDependency>? After = null)
{
    public IReadOnlyList<BacklogDependency> After { get; init; } = After ?? [];

    public bool Waiting { get; init; }

    [JsonIgnore]
    public bool IsDone => Mark == "x";

    public bool Equals(BacklogEntry? other)
    {
        return other is not null
            && Title == other.Title
            && Rest == other.Rest
            && Number == other.Number
            && Mark == other.Mark
            && Story == other.Story
            && Waiting == other.Waiting
            && After.SequenceEqual(other.After);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Title, Rest, Number, Mark, Story, Waiting, After.Count);
    }
}

/// <summary>A backlog's lines grouped by mark, each group in file order.</summary>
internal sealed record Backlog(
    IReadOnlyList<BacklogEntry> Open,
    IReadOnlyList<BacklogEntry> Approval,
    IReadOnlyList<BacklogEntry> User,
    IReadOnlyList<BacklogEntry> Done)
{
    public static readonly Backlog Empty = new([], [], [], []);

    /// <summary>Every entry, open, approval, user and done in that order.</summary>
    public IEnumerable<BacklogEntry> All => Open.Concat(Approval).Concat(User).Concat(Done);
}

/// <summary>Turns <c>backlog.md</c> lines into entries. Only lines starting with <c>- [ ] </c>, <c>- [?] </c>,
/// <c>- [h] </c> and <c>- [x] </c> / <c>- [X] </c> count; everything else is ignored.</summary>
internal static partial class BacklogParser
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
                string mark = line[3] == 'X' ? "x" : line[3].ToString();
                group.Add(ToEntry(line[6..], mark));
            }
        }

        return new Backlog(open, approval, user, done);
    }

    /// <summary>Parses the text after the mark: a numbered line when it starts with a well-formed
    /// <c>[#N](target)</c>, the old title/rest split otherwise.</summary>
    public static BacklogEntry ToEntry(string text, string mark = " ")
    {
        BacklogEntry? numbered = ToNumberedEntry(text, mark);
        if (numbered is not null)
        {
            return numbered;
        }

        int index = text.IndexOf(Separator, StringComparison.Ordinal);
        if (index < 0)
        {
            return new BacklogEntry(text, "", Mark: mark);
        }

        return new BacklogEntry(text[..index], text[(index + Separator.Length)..], Mark: mark);
    }

    private static BacklogEntry? ToNumberedEntry(string text, string mark)
    {
        Match link = NumberedLink().Match(text);
        if (!link.Success || !TryParseNumber(link.Groups["number"].Value, out int number))
        {
            return null;
        }

        Match body = Body().Match(link.Groups["body"].Value);
        List<BacklogDependency> after = [];
        if (body.Groups["after"].Success)
        {
            foreach (string part in body.Groups["after"].Value.Split(';'))
            {
                Match dependency = Dependency().Match(part.Trim());
                if (dependency.Success && TryParseNumber(dependency.Groups["number"].Value, out int dependencyNumber))
                {
                    string project = dependency.Groups["project"].Value;
                    after.Add(new BacklogDependency(project.Length == 0 ? null : project, dependencyNumber));
                }
            }
        }

        string rest = body.Groups["rest"].Success ? body.Groups["rest"].Value : "";
        string title = body.Groups["title"].Value.Trim();
        return new BacklogEntry(title, rest, number, mark, link.Groups["target"].Value, after);
    }

    /// <summary>Parses ASCII digits only (no sign, no spaces, no other scripts' digits).</summary>
    public static bool TryParseNumber(string digits, out int number)
    {
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
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

    [GeneratedRegex(@"\A\[#(?<number>[0-9]{1,9})\]\((?<target>[^()\s]+)\)(?:[ \t]+(?<body>.*))?\z",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex NumberedLink();

    // Non-backtracking: a lazy title followed by optional space runs would backtrack quadratically.
    [GeneratedRegex(@"\A(?<title>.*?)(?:[ \t]*\(after:[ \t]*(?<after>[^()]*)\))?(?:[ \t]*(?<rest>→.*))?\z",
        RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex Body();

    [GeneratedRegex(@"\A(?<project>[A-Za-z0-9._-]*)#(?<number>[0-9]{1,9})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Dependency();
}
