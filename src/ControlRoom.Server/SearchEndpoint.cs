using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace ControlRoom.Server;

/// <summary>One item in the <c>GET /api/search</c> response. <c>Mark</c> is <c>open</c>, <c>approval</c>,
/// <c>task</c> or <c>done</c>; <c>Story</c> says whether the line links a numbered story; <c>Match</c> says how the
/// item was found: <c>number</c>, <c>title</c>, <c>story</c> or <c>filter</c>. No story text, path or version.</summary>
internal sealed record SearchItem(
    string Project, int? Number, string Mark, string Title, string Rest, bool Story, string Match);

/// <summary>The <c>GET /api/search</c> response: at most <see cref="SearchEndpoint.MaxItems"/> items, the full
/// count, every discovered project name and the names of the projects whose <c>backlog.md</c> can't be read.</summary>
internal sealed record SearchResponse(
    IReadOnlyList<SearchItem> Items, int Total, IReadOnlyList<string> Projects, IReadOnlyList<string> Unreadable);

/// <summary>The checked request: the trimmed query, and the mark and project filters (<c>null</c> when not
/// set).</summary>
internal sealed record SearchQuery(string Text, string? Mark, string? Project);

/// <summary><c>GET /api/search?q=&amp;mark=&amp;project=</c>: finds backlog items across every discovered project by
/// number (<c>#N</c>, <c>Name#N</c>), by words in the title or rest, or else by words in the story (read only
/// through <see cref="StoryReader.Read"/>, and only for an item that passes the filters and did not match on its
/// title). Read-only; <c>q</c> is only compared as text, never used as a path, pattern or log value.</summary>
internal static partial class SearchEndpoint
{
    public const string Route = "/api/search";

    public const int MaxItems = 100;

    public const int MaxQueryLength = 200;

    public const string InvalidBody = "{\"error\":\"invalid\"}";

    private const string MatchNumber = "number";
    private const string MatchTitle = "title";
    private const string MatchStory = "story";
    private const string MatchFilter = "filter";

    private static readonly Dictionary<string, string> MarkNames = new(StringComparer.Ordinal)
    {
        [" "] = "open",
        ["?"] = "approval",
        ["h"] = "task",
        ["x"] = "done",
    };

    private static readonly HashSet<string> Marks = new(MarkNames.Values, StringComparer.Ordinal);

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            IReadOnlyList<ProjectFolder> projects = ProjectFinder.Find(root.Path);
            SearchQuery? query = Check(context.Request.Query, projects);
            if (query is null)
            {
                return Results.Content(InvalidBody, "application/json", statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Json(Build(projects, query));
        });
    }

    /// <summary>The checked query, or <c>null</c> for a repeated parameter, an unknown mark, a project that is not
    /// a discovered name or a <c>q</c> over <see cref="MaxQueryLength"/> characters. An empty <c>mark</c> or
    /// <c>project</c> means no filter.</summary>
    public static SearchQuery? Check(IQueryCollection query, IReadOnlyList<ProjectFolder> projects)
    {
        foreach (KeyValuePair<string, StringValues> pair in query)
        {
            if (pair.Value.Count != 1)
            {
                return null;
            }
        }

        string text = query.TryGetValue("q", out StringValues q) ? q[0] ?? "" : "";
        if (text.Length > MaxQueryLength)
        {
            return null;
        }

        string? mark = Optional(query, "mark");
        if (mark is not null && !Marks.Contains(mark))
        {
            return null;
        }

        string? project = Optional(query, "project");
        if (project is not null
            && !projects.Any(folder => string.Equals(folder.Name, project, StringComparison.Ordinal)))
        {
            return null;
        }

        return new SearchQuery(text.Trim(), mark, project);
    }

    public static SearchResponse Build(IReadOnlyList<ProjectFolder> projects, SearchQuery query)
    {
        List<string> unreadable = [];
        List<SearchItem> numbers = [];
        List<SearchItem> titles = [];
        List<SearchItem> stories = [];
        List<SearchItem> filtered = [];

        bool hasFilter = query.Mark is not null || query.Project is not null;
        bool empty = query.Text.Length == 0;
        NumberQuery? number = empty ? null : ParseNumber(query.Text);
        // A repeated word (ignoring case) changes no result, so it is scanned only once.
        string[] words = empty || number is not null
            ? []
            : query.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (ProjectFolder project in projects)
        {
            BacklogFile? file = BacklogReader.ReadFile(project);
            if (file is null)
            {
                unreadable.Add(project.Name);
                continue;
            }

            if ((empty && !hasFilter)
                || (query.Project is not null && !string.Equals(project.Name, query.Project, StringComparison.Ordinal))
                || (number?.Project is not null
                    && !string.Equals(project.Name, number.Project, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (BacklogEntry entry in file.Backlog.All.OrderBy(line => line.Line))
            {
                string mark = MarkNames[entry.Mark];
                if (query.Mark is not null && !string.Equals(mark, query.Mark, StringComparison.Ordinal))
                {
                    continue;
                }

                if (empty)
                {
                    filtered.Add(ToItem(project, entry, mark, MatchFilter));
                }
                else if (number is not null)
                {
                    if (entry.Number == number.Number)
                    {
                        numbers.Add(ToItem(project, entry, mark, MatchNumber));
                    }
                }
                else if (words.All(word => Contains(entry.Title, word) || Contains(entry.Rest, word)))
                {
                    titles.Add(ToItem(project, entry, mark, MatchTitle));
                }
                else if (StoryMatches(project, entry, words))
                {
                    stories.Add(ToItem(project, entry, mark, MatchStory));
                }
            }
        }

        List<SearchItem> all = [.. numbers, .. titles, .. stories, .. filtered];
        return new SearchResponse(
            all.Take(MaxItems).ToList(), all.Count, projects.Select(project => project.Name).ToList(), unreadable);
    }

    // Only a numbered line with a story link is read, and only through StoryReader (fenced to backlog/, no-follow,
    // 1 MiB cap); a refused story matches nothing.
    private static bool StoryMatches(ProjectFolder project, BacklogEntry entry, string[] words)
    {
        if (entry.Number is null || entry.Story is null)
        {
            return false;
        }

        string? text = StoryReader.Read(project, entry);
        return text is not null && words.All(word => Contains(text, word));
    }

    private static bool Contains(string text, string word)
    {
        return text.Contains(word, StringComparison.OrdinalIgnoreCase);
    }

    private static SearchItem ToItem(ProjectFolder project, BacklogEntry entry, string mark, string match)
    {
        return new SearchItem(
            project.Name,
            entry.Number,
            mark,
            entry.Title,
            entry.Rest,
            entry.Number is not null && entry.Story is not null,
            match);
    }

    private static string? Optional(IQueryCollection query, string name)
    {
        if (!query.TryGetValue(name, out StringValues values))
        {
            return null;
        }

        string value = values[0] ?? "";
        return value.Length == 0 ? null : value;
    }

    /// <summary><c>#N</c> or <c>Name#N</c> (spaces allowed around <c>#</c>), or <c>null</c> for a word
    /// search.</summary>
    private static NumberQuery? ParseNumber(string text)
    {
        Match match = NumberPattern().Match(text);
        if (!match.Success || !BacklogParser.TryParseNumber(match.Groups["number"].Value, out int number))
        {
            return null;
        }

        string name = match.Groups["name"].Value;
        return new NumberQuery(name.Length == 0 ? null : name, number);
    }

    private sealed record NumberQuery(string? Project, int Number);

    [GeneratedRegex(@"\A[ \t]*(?<name>[^\s#]*)[ \t]*#[ \t]*(?<number>[0-9]{1,9})[ \t]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
