using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>A job folder under <c>factory/jobs</c>: its name (the job id) and full path.</summary>
internal sealed record JobFolder(string Id, string Path);

/// <summary>One job as the board shows it, mapped field by field from its <c>state.json</c>. A field that is
/// missing or of the wrong type is <c>null</c> (or an empty list or map); fields it doesn't know are dropped.
/// <c>CiStatus</c>, <c>CiUrl</c> and <c>CiSha</c> come from <c>ci.status</c>, <c>ci.url</c> and <c>ci.sha</c> (the
/// last two never in a job's JSON), <c>Tokens</c> is the sum of <c>input</c>, <c>cacheRead</c> and
/// <c>output</c> over every agent in <c>tokens</c> (<c>null</c> when no agent gives a valid count). <c>Updated</c> is
/// <c>updated</c> when it is exactly <c>YYYY-MM-DDTHH:MM:SSZ</c> (UTC); it is never sent in a job's JSON. <c>Request</c> is the action of a pending <c>human.json</c>
/// (<c>approve</c>, <c>rework</c> or <c>unreadable</c>), filled by <see cref="JobAction.WithRequest"/> only for a
/// <c>needs-human</c> job.</summary>
internal sealed record JobSummary(
    string Id,
    string? Project,
    string? Title,
    string? Branch,
    string? Stage,
    int? Round,
    string? Mode,
    string? Path,
    string? Risk,
    IReadOnlyDictionary<string, string> Reviews,
    IReadOnlyList<IReadOnlyDictionary<string, string>> PastRounds,
    string? SpecReview,
    bool? SpecRevised,
    string? Decision,
    string? Note,
    string? CiStatus,
    long? Tokens,
    string? Request = null,
    [property: JsonIgnore] DateTimeOffset? Updated = null,
    [property: JsonIgnore] string? CiUrl = null,
    [property: JsonIgnore] string? CiSha = null);

/// <summary>Reads the jobs folder <c>&lt;root&gt;/factory/jobs</c>. The folder is refused when <c>factory</c> or
/// <c>jobs</c> is a symlink; only job folders that are not symlinks and whose name matches
/// <see cref="IdPattern"/> are listed. Each <c>state.json</c> is read through <see cref="SafeFileReader"/>
/// (no-follow, regular file, <see cref="MaxStateBytes"/> cap) and mapped to a <see cref="JobSummary"/>; the raw
/// JSON is never passed on.</summary>
internal static partial class JobReader
{
    public const string StateFileName = "state.json";

    public const long MaxStateBytes = 256 * 1024;

    /// <summary>The reviewer keys, in display order.</summary>
    public static readonly IReadOnlyList<string> ReviewerKeys = ["security", "ux", "ui", "code"];

    /// <summary>The jobs folder, or <c>null</c> when it is missing or <c>factory</c> or <c>jobs</c> is a
    /// symlink.</summary>
    public static string? JobsFolder(string factoryRoot)
    {
        try
        {
            DirectoryInfo factory = new(System.IO.Path.Combine(factoryRoot, "factory"));
            if (!factory.Exists || factory.LinkTarget is not null)
            {
                return null;
            }

            DirectoryInfo jobs = new(System.IO.Path.Combine(factory.FullName, "jobs"));
            if (!jobs.Exists || jobs.LinkTarget is not null)
            {
                return null;
            }

            return jobs.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Every valid job folder, ordered by name (ordinal).</summary>
    public static IReadOnlyList<JobFolder> ListFolders(string factoryRoot)
    {
        List<JobFolder> folders = [];
        string? jobs = JobsFolder(factoryRoot);
        if (jobs is null)
        {
            return folders;
        }

        List<DirectoryInfo> entries;
        try
        {
            entries = new DirectoryInfo(jobs).EnumerateDirectories().ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return folders;
        }

        foreach (DirectoryInfo entry in entries)
        {
            if (!IdPattern().IsMatch(entry.Name))
            {
                continue;
            }

            try
            {
                if (entry.LinkTarget is not null)
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            folders.Add(new JobFolder(entry.Name, entry.FullName));
        }

        folders.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return folders;
    }

    /// <summary>Every job whose <c>state.json</c> can be read and is a JSON object, ordered by folder
    /// name.</summary>
    public static IReadOnlyList<JobSummary> ReadAll(string factoryRoot)
    {
        List<JobSummary> jobs = [];
        foreach (JobFolder folder in ListFolders(factoryRoot))
        {
            JobSummary? job = Read(folder);
            if (job is not null)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    /// <summary>The folder's job, or <c>null</c> when its <c>state.json</c> is missing, refused or not a JSON
    /// object.</summary>
    public static JobSummary? Read(JobFolder folder)
    {
        string? text = SafeFileReader.ReadText(System.IO.Path.Combine(folder.Path, StateFileName), MaxStateBytes);
        return text is null ? null : Parse(folder.Id, text);
    }

    /// <summary>Maps <c>state.json</c> text to a summary with the folder name as id, or <c>null</c> when the text
    /// is not a JSON object.</summary>
    public static JobSummary? Parse(string id, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement state = document.RootElement;
            if (state.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new JobSummary(
                id,
                Text(state, "project"),
                Text(state, "title"),
                Text(state, "branch"),
                Text(state, "stage"),
                Round(state),
                Text(state, "mode"),
                Text(state, "path"),
                Text(state, "risk"),
                Reviews(Property(state, "reviews")),
                PastRounds(state),
                Text(state, "specReview"),
                Flag(state, "specRevised"),
                Text(state, "decision"),
                Text(state, "note"),
                CiStatus(state),
                Tokens(state),
                Request: null,
                Updated: Updated(state),
                CiUrl: CiText(state, "url"),
                CiSha: CiText(state, "sha"));
        }
    }

    private static JsonElement? Property(JsonElement state, string name)
    {
        return state.TryGetProperty(name, out JsonElement value) ? value : null;
    }

    private static string? Text(JsonElement state, string name)
    {
        JsonElement? value = Property(state, name);
        return value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    }

    private static bool? Flag(JsonElement state, string name)
    {
        JsonElement? value = Property(state, name);
        return value?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int? Round(JsonElement state)
    {
        JsonElement? value = Property(state, "round");
        if (value is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out int round) && round >= 0)
        {
            return round;
        }

        return null;
    }

    /// <summary>The known reviewer keys whose value is a string; anything else is dropped.</summary>
    private static Dictionary<string, string> Reviews(JsonElement? value)
    {
        Dictionary<string, string> reviews = new(StringComparer.Ordinal);
        if (value is not { ValueKind: JsonValueKind.Object } map)
        {
            return reviews;
        }

        foreach (string key in ReviewerKeys)
        {
            if (map.TryGetProperty(key, out JsonElement review) && review.ValueKind == JsonValueKind.String)
            {
                reviews[key] = review.GetString()!;
            }
        }

        return reviews;
    }

    /// <summary>Each past round that is an object, as its reviews; other items are dropped.</summary>
    private static List<IReadOnlyDictionary<string, string>> PastRounds(JsonElement state)
    {
        List<IReadOnlyDictionary<string, string>> rounds = [];
        if (Property(state, "pastRounds") is not { ValueKind: JsonValueKind.Array } list)
        {
            return rounds;
        }

        foreach (JsonElement round in list.EnumerateArray())
        {
            if (round.ValueKind == JsonValueKind.Object)
            {
                rounds.Add(Reviews(round));
            }
        }

        return rounds;
    }

    private static string? CiStatus(JsonElement state)
    {
        return CiText(state, "status");
    }

    private static string? CiText(JsonElement state, string name)
    {
        return Property(state, "ci") is { ValueKind: JsonValueKind.Object } ci ? Text(ci, name) : null;
    }

    /// <summary><c>updated</c> as a UTC time, only when it is a string of exactly the form
    /// <c>YYYY-MM-DDTHH:MM:SSZ</c> that is a real date and time; <c>null</c> otherwise.</summary>
    private static DateTimeOffset? Updated(JsonElement state)
    {
        string? text = Text(state, "updated");
        if (text is null || !UpdatedPattern().IsMatch(text))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            text,
            UpdatedFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset updated)
            ? updated
            : null;
    }

    /// <summary>The form of <c>updated</c> (and of a history job's <c>finished</c>).</summary>
    public const string UpdatedFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>The sum of <c>input</c>, <c>cacheRead</c> and <c>output</c> over every agent object; a missing or
    /// non-integer count adds nothing. <c>null</c> when <c>tokens</c> is not an object or no agent gives at least
    /// one valid count (unknown, never 0).</summary>
    private static long? Tokens(JsonElement state)
    {
        if (Property(state, "tokens") is not { ValueKind: JsonValueKind.Object } tokens)
        {
            return null;
        }

        long total = 0;
        bool known = false;
        foreach (JsonProperty agent in tokens.EnumerateObject())
        {
            if (agent.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (string name in new[] { "input", "cacheRead", "output" })
            {
                if (agent.Value.TryGetProperty(name, out JsonElement count)
                    && count.ValueKind == JsonValueKind.Number
                    && count.TryGetInt64(out long value)
                    && value >= 0)
                {
                    known = true;
                    total = total > long.MaxValue - value ? long.MaxValue : total + value;
                }
            }
        }

        return known ? total : null;
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex UpdatedPattern();

    [GeneratedRegex(@"\A[0-9]{1,6}-[a-z0-9-]{1,80}\z", RegexOptions.CultureInvariant)]
    public static partial Regex IdPattern();
}
