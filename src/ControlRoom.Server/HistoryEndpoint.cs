using System.Globalization;

namespace ControlRoom.Server;

/// <summary>One finished job in the <c>GET /api/history</c> list. <c>Outcome</c> is <c>merged</c>,
/// <c>findings</c>, <c>escalated</c> or <c>reverted</c>; <c>Tokens</c> is <c>null</c> when unknown;
/// <c>Finished</c> is <c>updated</c> as <c>YYYY-MM-DDTHH:MM:SSZ</c>, or <c>null</c>.</summary>
internal sealed record HistoryJob(
    string Id,
    string? Project,
    string? Title,
    string? Mode,
    string? Path,
    int? Round,
    string Outcome,
    long? Tokens,
    string? Finished);

/// <summary>The jobs of one name in one window: how many, the sum of their known tokens and how many have unknown
/// tokens.</summary>
internal sealed record HistoryWindow(int Jobs, long Tokens, int Unknown);

/// <summary>One project or mode with its last 7 and last 30 days; <c>Name</c> is <c>null</c> for jobs without
/// one.</summary>
internal sealed record HistoryTotal(string? Name, HistoryWindow Last7, HistoryWindow Last30);

/// <summary>The totals per project and per mode.</summary>
internal sealed record HistoryTotals(IReadOnlyList<HistoryTotal> Projects, IReadOnlyList<HistoryTotal> Modes);

/// <summary>One mode in the chart: its known tokens on each of the 30 days.</summary>
internal sealed record HistoryChartMode(string? Name, IReadOnlyList<long> Tokens);

/// <summary>The chart data: one row per mode, in the order of the mode totals.</summary>
internal sealed record HistoryChart(IReadOnlyList<HistoryChartMode> Modes);

/// <summary>The <c>GET /api/history</c> response.</summary>
internal sealed record HistoryResponse(
    IReadOnlyList<HistoryJob> Jobs, IReadOnlyList<string> Days, HistoryTotals Totals, HistoryChart Chart);

/// <summary><c>GET /api/history</c>: the finished jobs (stage <c>done</c>, <c>needs-human</c> or <c>reverted</c>)
/// from <see cref="JobReader.ReadAll"/>, newest first, with token totals per project and per mode for the last 7
/// and 30 UTC days (from <see cref="TimeProvider"/>) and the daily tokens per mode. A job without token data counts
/// as unknown, never as 0. Read-only, no parameters.</summary>
internal static class HistoryEndpoint
{
    public const string Route = "/api/history";

    public const int DayCount = 30;

    public const int WeekCount = 7;

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, TimeProvider time, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(Build(root.Path, time.GetUtcNow()));
        });
    }

    public static HistoryResponse Build(string factoryRoot, DateTimeOffset now)
    {
        List<(JobSummary Job, string Outcome)> finished = [];
        foreach (JobSummary job in JobReader.ReadAll(factoryRoot))
        {
            string? outcome = Outcome(job);
            if (outcome is not null)
            {
                finished.Add((job, outcome));
            }
        }

        finished.Sort((a, b) => CompareNewestFirst(a.Job, b.Job));

        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        DateOnly first = today.AddDays(1 - DayCount);
        List<string> days = Enumerable.Range(0, DayCount)
            .Select(offset => first.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();

        List<HistoryJob> jobs = [];
        List<(JobSummary Job, int Day)> inWindow = [];
        foreach ((JobSummary job, string outcome) in finished)
        {
            jobs.Add(new HistoryJob(
                job.Id,
                job.Project,
                job.Title,
                job.Mode,
                job.Path,
                job.Round,
                outcome,
                job.Tokens,
                job.Updated?.ToString(JobReader.UpdatedFormat, CultureInfo.InvariantCulture)));
            int? day = DayIndex(job, first);
            if (day is not null)
            {
                inWindow.Add((job, day.Value));
            }
        }

        List<HistoryTotal> projects = Totals(inWindow, job => job.Project);
        List<HistoryTotal> modes = Totals(inWindow, job => job.Mode);
        List<HistoryChartMode> chart = modes
            .Select(mode => new HistoryChartMode(mode.Name, Daily(inWindow, mode.Name)))
            .ToList();

        return new HistoryResponse(jobs, days, new HistoryTotals(projects, modes), new HistoryChart(chart));
    }

    /// <summary>The outcome of a finished job, or <c>null</c> for a job in any other stage.</summary>
    private static string? Outcome(JobSummary job)
    {
        return job.Stage switch
        {
            "reverted" => "reverted",
            "needs-human" => "escalated",
            "done" => string.Equals(job.Decision, "FINDINGS", StringComparison.Ordinal) ? "findings" : "merged",
            _ => null,
        };
    }

    /// <summary>Newest <c>Updated</c> first, then id descending (ordinal); jobs without <c>Updated</c> last.</summary>
    private static int CompareNewestFirst(JobSummary a, JobSummary b)
    {
        if (a.Updated is not null && b.Updated is null)
        {
            return -1;
        }

        if (a.Updated is null && b.Updated is not null)
        {
            return 1;
        }

        if (a.Updated is DateTimeOffset left && b.Updated is DateTimeOffset right)
        {
            int byTime = right.CompareTo(left);
            if (byTime != 0)
            {
                return byTime;
            }
        }

        return string.CompareOrdinal(b.Id, a.Id);
    }

    /// <summary>The index (0 to 29) of the job's UTC finish date among the 30 days starting at <paramref name="first"/>,
    /// or <c>null</c> when it has no finish time or falls outside them.</summary>
    private static int? DayIndex(JobSummary job, DateOnly first)
    {
        if (job.Updated is not DateTimeOffset updated)
        {
            return null;
        }

        int index = DateOnly.FromDateTime(updated.UtcDateTime).DayNumber - first.DayNumber;
        return index is >= 0 and < DayCount ? index : null;
    }

    /// <summary>One total per name with at least one job in the 30 days, sorted ordinal with <c>null</c> last.</summary>
    private static List<HistoryTotal> Totals(List<(JobSummary Job, int Day)> inWindow, Func<JobSummary, string?> name)
    {
        List<string?> names = inWindow.Select(entry => name(entry.Job)).Distinct().ToList();
        names.Sort(CompareNames);
        return names
            .Select(current =>
            {
                List<(JobSummary Job, int Day)> own = inWindow
                    .Where(entry => string.Equals(name(entry.Job), current, StringComparison.Ordinal))
                    .ToList();
                return new HistoryTotal(
                    current,
                    Window(own.Where(entry => entry.Day >= DayCount - WeekCount).Select(entry => entry.Job)),
                    Window(own.Select(entry => entry.Job)));
            })
            .ToList();
    }

    private static int CompareNames(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return (a is null ? 1 : 0) - (b is null ? 1 : 0);
        }

        return string.CompareOrdinal(a, b);
    }

    private static HistoryWindow Window(IEnumerable<JobSummary> jobs)
    {
        int count = 0;
        int unknown = 0;
        long tokens = 0;
        foreach (JobSummary job in jobs)
        {
            count += 1;
            if (job.Tokens is long known)
            {
                tokens = Add(tokens, known);
            }
            else
            {
                unknown += 1;
            }
        }

        return new HistoryWindow(count, tokens, unknown);
    }

    /// <summary>The known tokens of the mode's jobs on each of the 30 days.</summary>
    private static long[] Daily(List<(JobSummary Job, int Day)> inWindow, string? mode)
    {
        long[] tokens = new long[DayCount];
        foreach ((JobSummary job, int day) in inWindow)
        {
            if (string.Equals(job.Mode, mode, StringComparison.Ordinal) && job.Tokens is long known)
            {
                tokens[day] = Add(tokens[day], known);
            }
        }

        return tokens;
    }

    private static long Add(long total, long value)
    {
        return total > long.MaxValue - value ? long.MaxValue : total + value;
    }
}
