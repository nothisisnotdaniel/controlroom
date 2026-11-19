using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>A user's request on a <c>needs-human</c> job, as held in its <c>human.json</c>: the action
/// (<c>approve</c> or <c>rework</c>), the job's round at the time, the cleaned note (rework only) and the UTC time
/// it was asked for (<c>YYYY-MM-DDTHH:MM:SSZ</c>).</summary>
internal sealed record JobRequest(string Action, int Round, string? Note, string Requested);

/// <summary>A request as the API shows it: the parsed request, or only <c>action</c> <c>unreadable</c>.</summary>
internal sealed record JobRequestView(
    string Action,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Round,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Requested)
{
    public static JobRequestView Unreadable { get; } = new(JobAction.Unreadable, null, null, null);

    public static JobRequestView Of(JobRequest request)
    {
        return new JobRequestView(request.Action, request.Round, request.Note, request.Requested);
    }
}

/// <summary>The request file <c>factory/jobs/&lt;id&gt;/human.json</c>: its schema, <see cref="Parse"/> (exact keys,
/// at most <see cref="MaxFileBytes"/>), <see cref="Render"/> (one JSON object plus one line break) and a no-follow
/// <see cref="Read"/>. The server only writes it; the next <c>/factory</c> session acts on it.</summary>
internal static partial class JobAction
{
    public const string FileName = "human.json";

    public const int MaxFileBytes = 4 * 1024;

    public const string Approve = "approve";

    public const string Rework = "rework";

    public const string Unreadable = "unreadable";

    public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);


    /// <summary>The request in a <c>human.json</c> file's bytes, or <c>null</c> when the bytes are over the cap, not
    /// UTF-8, not one JSON object with exactly the keys of its action, or a value is of the wrong type or form.</summary>
    public static JobRequest? Parse(byte[] bytes)
    {
        if (bytes.Length > MaxFileBytes)
        {
            return null;
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!fields.TryAdd(property.Name, property.Value.Clone()))
                {
                    return null;
                }
            }

            return Check(fields);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException
                                              or DecoderFallbackException)
        {
            return null;
        }
    }

    // A JSON string that escapes only what JSON needs (quote, backslash, control characters) and keeps every other
    // character as itself, so a 1000-rune note (at most 4 UTF-8 bytes per rune) keeps the file under MaxFileBytes.
    // JsonSerializer's encoders write non-ASCII as \uXXXX, up to 12 bytes for a rune outside the BMP.
    private static string JsonString(string value)
    {
        StringBuilder text = new(value.Length + 2);
        text.Append('"');
        foreach (char c in value)
        {
            if (c == '"' || c == '\\')
            {
                text.Append('\\').Append(c);
            }
            else if (c < ' ')
            {
                text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.Append('"').ToString();
    }

    /// <summary>The file bytes of a request: the approve or rework form, UTF-8, followed by one line break. Non-ASCII
    /// characters are written as UTF-8, not escaped; callers check the length against <see cref="MaxFileBytes"/>.</summary>
    public static byte[] Render(JobRequest request)
    {
        StringBuilder text = new();
        text.Append("{\"action\":").Append(JsonString(request.Action));
        text.Append(",\"round\":").Append(request.Round.ToString(CultureInfo.InvariantCulture));
        if (request.Note is not null)
        {
            text.Append(",\"note\":").Append(JsonString(request.Note));
        }

        text.Append(",\"requested\":").Append(JsonString(request.Requested)).Append("}\n");
        return StrictUtf8.GetBytes(text.ToString());
    }

    /// <summary>The <c>requested</c> text of a UTC time.</summary>
    public static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The job folder's <c>human.json</c>: <c>null</c> when nothing is there, the parsed request, or
    /// <see cref="JobRequestView.Unreadable"/> for a symlink, a non-regular file, a file over the cap or a bad
    /// schema. Checked with <c>lstat</c> and read only through <see cref="SafeFileReader"/>.</summary>
    public static JobRequestView? Read(JobFolder folder)
    {
        string path = Path.Combine(folder.Path, FileName);
        PathStat stat = NoFollowStat.Of(path);
        if (stat.Kind == PathKind.Missing)
        {
            return null;
        }

        if (stat.Kind != PathKind.File)
        {
            return JobRequestView.Unreadable;
        }

        byte[]? bytes = SafeFileReader.ReadBytes(path, MaxFileBytes);
        JobRequest? request = bytes is null ? null : Parse(bytes);
        return request is null ? JobRequestView.Unreadable : JobRequestView.Of(request);
    }

    /// <summary>The job with its <c>Request</c> action when it is <c>needs-human</c>; any other stage gets
    /// <c>null</c>.</summary>
    public static JobSummary WithRequest(JobSummary job, JobFolder folder)
    {
        string? action = string.Equals(job.Stage, JobActEndpoint.NeedsHuman, StringComparison.Ordinal)
            ? Read(folder)?.Action
            : null;
        return job with { Request = action };
    }

    private static JobRequest? Check(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("action", out JsonElement actionValue) || actionValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? action = actionValue.GetString();
        string[] keys = action switch
        {
            Approve => ["action", "round", "requested"],
            Rework => ["action", "round", "note", "requested"],
            _ => [],
        };
        if (keys.Length == 0 || fields.Count != keys.Length || !keys.All(fields.ContainsKey))
        {
            return null;
        }

        JsonElement roundValue = fields["round"];
        if (roundValue.ValueKind != JsonValueKind.Number || !roundValue.TryGetInt32(out int round) || round < 0)
        {
            return null;
        }

        JsonElement requestedValue = fields["requested"];
        string? requested = requestedValue.ValueKind == JsonValueKind.String ? requestedValue.GetString() : null;
        if (requested is null || !IsTime(requested))
        {
            return null;
        }

        string? note = null;
        if (action == Rework)
        {
            JsonElement noteValue = fields["note"];
            string? raw = noteValue.ValueKind == JsonValueKind.String ? noteValue.GetString() : null;
            note = BacklogEditor.CleanNote(raw);
            if (note is null || !string.Equals(note, raw, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return new JobRequest(action!, round, note, requested);
    }

    private static bool IsTime(string text)
    {
        return TimePattern().IsMatch(text)
            && DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();
}
