namespace ControlRoom.Server;

/// <summary>The Ready/Waiting rule shared by the backlogs and tasks views: a dependency is met only when it is a
/// done (<c>[x]</c>) line of the named project, or of the same project when it names none. An unknown project or
/// number counts as not met.</summary>
internal sealed class BacklogWaiting
{
    private readonly Dictionary<string, HashSet<int>> doneNumbers;

    private BacklogWaiting(Dictionary<string, HashSet<int>> doneNumbers)
    {
        this.doneNumbers = doneNumbers;
    }

    /// <summary>The done numbers of every project; an unreadable backlog has none.</summary>
    public static BacklogWaiting From(IEnumerable<(string Project, Backlog? Backlog)> backlogs)
    {
        Dictionary<string, HashSet<int>> done = new(StringComparer.Ordinal);
        foreach ((string project, Backlog? backlog) in backlogs)
        {
            done[project] = (backlog ?? Backlog.Empty).Done
                .Where(entry => entry.Number is not null)
                .Select(entry => entry.Number!.Value)
                .ToHashSet();
        }

        return new BacklogWaiting(done);
    }

    public bool IsMet(BacklogDependency dependency, string projectName)
    {
        return doneNumbers.TryGetValue(dependency.Project ?? projectName, out HashSet<int>? done)
            && done.Contains(dependency.Number);
    }

    /// <summary>The dependencies of an entry of <paramref name="projectName"/> that are not met, in line order.</summary>
    public IReadOnlyList<BacklogDependency> Unmet(BacklogEntry entry, string projectName)
    {
        return entry.After.Where(dependency => !IsMet(dependency, projectName)).ToList();
    }

    public bool IsWaiting(BacklogEntry entry, string projectName)
    {
        return entry.After.Any(dependency => !IsMet(dependency, projectName));
    }
}
