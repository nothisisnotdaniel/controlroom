namespace ControlRoom.Server.Tests;

/// <summary><see cref="BacklogPlan"/> and the dependency rules of <see cref="BacklogWaiting"/> it uses: next,
/// blocked, missing and stopped for the project <c>Alpha</c>, next to <c>Times</c> and the unreadable
/// <c>Locked</c>.</summary>
public sealed class BacklogPlanTests
{
    private const string TimesBacklog =
        "- [x] [#5](backlog/005-t.md) Five\n"
        + "- [ ] [#6](backlog/006-t.md) Six\n"
        + "- [?] [#7](backlog/007-t.md) Seven\n";

    /// <summary>Each row: Alpha's backlog and its plan as <c>next=N[ spike]; stopped; blocked=N[P#N, P#N!]</c>
    /// (<c>!</c> marks a missing dependency).</summary>
    [Theory]
    [InlineData(
        "- [ ] [#1](backlog/001-a.md) One (after: #2)\n- [x] [#2](backlog/002-a.md) Two\n",
        "next=1; blocked=")]
    [InlineData(
        "- [ ] [#1](backlog/001-a.md) One (after: #3)\n- [ ] [#2](backlog/002-a.md) Two\n- [ ] [#3](backlog/003-a.md) Three\n",
        "next=2; blocked=1[Alpha#3]")]
    [InlineData(
        "- [ ] Old line: no number\n- [?] [#1](backlog/001-a.md) Proposal\n- [h] [#2](backlog/002-a.md) Hand\n"
        + "- [x] [#3](backlog/003-a.md) Done\n- [ ] [#4](backlog/004-a.md) Four\n",
        "next=4; blocked=")]
    [InlineData(
        "- [ ] [#1](backlog/001-a.md) Spike: find out (after: Times#5)\n- [ ] [#2](backlog/002-a.md) Two\n",
        "next=1 spike; blocked=")]
    [InlineData("- [ ] [#1](backlog/001-a.md) Spikes are not a word match\n", "next=1; blocked=")]
    [InlineData("- [ ] [#1](backlog/001-a.md) A quick SPIKE\n", "next=1 spike; blocked=")]
    [InlineData(
        "- [ ] [#1](backlog/001-a.md) One (after: Times#6)\n- [?] [#2](backlog/002-a.md) Two\n",
        "next=-; blocked=1[Times#6]")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: times#5)\n", "next=1; blocked=")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: TIMES#7)\n", "next=-; blocked=1[Times#7]")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: Nope#1)\n- [ ] [#2](backlog/002-a.md) Two\n",
        "next=2; stopped; blocked=1[Nope#1!]")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: Times#99; #2)\n- [ ] [#2](backlog/002-a.md) Two\n",
        "next=2; stopped; blocked=1[Times#99!, Alpha#2]")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: #9)\n", "next=-; stopped; blocked=1[Alpha#9!]")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: Locked#1)\n", "next=-; blocked=1[Locked#1]")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: factory#3)\n", "next=-; blocked=1[factory#3]")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One (after: Factory#3)\n", "next=-; blocked=1[Factory#3]")]
    [InlineData(
        "- [ ] [#1](backlog/001-a.md) One (after: #4; Times#6)\n- [ ] [#2](backlog/002-a.md) Two (after: Times#5)\n"
        + "- [ ] [#3](backlog/003-a.md) Three (after: Times#6)\n- [x] [#4](backlog/004-a.md) Four\n",
        "next=2; blocked=1[Times#6], 3[Times#6]")]
    [InlineData("", "next=-; blocked=")]
    public void Plan_FollowsTheRuleOfJobPyNext(string alphaBacklog, string expected)
    {
        Backlog alpha = Parse(alphaBacklog);
        BacklogWaiting waiting = BacklogWaiting.From([("Alpha", alpha), ("Locked", null), ("Times", Parse(TimesBacklog))]);

        ProjectPlan plan = BacklogPlan.For(alpha, "Alpha", waiting);

        Assert.Equal(expected, Describe(plan));
    }

    [Fact]
    public void Plan_NextAndBlockedCarryTitleAndStory()
    {
        Backlog alpha = Parse(
            "- [ ] [#1](backlog/001-a.md) Blocked one (after: Times#6)\n- [ ] [#2](backlog/002-b.md) Ready <b>two</b>\n");
        BacklogWaiting waiting = BacklogWaiting.From([("Alpha", alpha), ("Times", Parse(TimesBacklog))]);

        ProjectPlan plan = BacklogPlan.For(alpha, "Alpha", waiting);

        Assert.Equal(new PlanItem(2, "Ready <b>two</b>", "backlog/002-b.md", false), plan.Next);
        BlockedItem blocked = Assert.Single(plan.Blocked);
        Assert.Equal((1, "Blocked one", "backlog/001-a.md"), (blocked.Number, blocked.Title, blocked.Story));
        Assert.Equal([new PlanDependency("Times", 6, false)], blocked.After);
    }

    [Fact]
    public void Plan_OfAnUnreadableProjectIsEmpty()
    {
        BacklogWaiting waiting = BacklogWaiting.From([("Locked", null)]);

        ProjectPlan plan = BacklogPlan.For(null, "Locked", waiting);

        Assert.Null(plan.Next);
        Assert.False(plan.Stopped);
        Assert.Empty(plan.Blocked);
    }

    [Theory]
    [InlineData("Times", 5, true, false)]
    [InlineData("times", 5, true, false)]
    [InlineData("TIMES", 6, false, false)]
    [InlineData("Times", 7, false, false)]
    [InlineData("Times", 99, false, true)]
    [InlineData(null, 1, false, false)]
    [InlineData(null, 2, false, true)]
    [InlineData("Nope", 1, false, true)]
    [InlineData("Locked", 1, false, false)]
    [InlineData("factory", 3, false, false)]
    [InlineData("FACTORY", 3, false, false)]
    public void Waiting_MetAndMissing(string? project, int number, bool met, bool missing)
    {
        BacklogWaiting waiting = BacklogWaiting.From(
            [("Alpha", Parse("- [ ] [#1](backlog/001-a.md) One\n")), ("Locked", null), ("Times", Parse(TimesBacklog))]);
        BacklogDependency dependency = new(project, number);

        Assert.Equal(met, waiting.IsMet(dependency, "Alpha"));
        Assert.Equal(missing, waiting.IsMissing(dependency, "Alpha"));
    }

    [Fact]
    public void Waiting_AnExactNameWinsOverACaseInsensitiveOne()
    {
        BacklogWaiting waiting = BacklogWaiting.From(
            [("Times", Parse("- [x] [#5](backlog/005-t.md) Five\n")), ("times", Parse("- [ ] [#5](backlog/005-t.md) Five\n"))]);

        Assert.True(waiting.IsMet(new BacklogDependency("Times", 5), "Alpha"));
        Assert.False(waiting.IsMet(new BacklogDependency("times", 5), "Alpha"));
        Assert.False(waiting.IsMet(new BacklogDependency("TIMES", 5), "Alpha"));
        Assert.True(waiting.IsMissing(new BacklogDependency("TIMES", 5), "Alpha"));
        Assert.Equal("TIMES", waiting.ProjectName(new BacklogDependency("TIMES", 5), "Alpha"));
    }

    private static Backlog Parse(string text)
    {
        return BacklogParser.Parse(text.Split('\n'));
    }

    private static string Describe(ProjectPlan plan)
    {
        string next = plan.Next is null ? "-" : plan.Next.Number + (plan.Next.Spike ? " spike" : "");
        string blocked = string.Join(", ", plan.Blocked.Select(item =>
            item.Number + "[" + string.Join(", ", item.After.Select(dependency =>
                dependency.Project + "#" + dependency.Number + (dependency.Missing ? "!" : ""))) + "]"));
        return "next=" + next + (plan.Stopped ? "; stopped" : "") + "; blocked=" + blocked;
    }

    [Theory]
    [InlineData("- [ ] [#1](backlog/001-a.md) One\n- [stop] Auto stops here\n- [ ] [#2](backlog/002-a.md) Two\n", 1, false)]
    [InlineData("- [stop] Auto stops here\n- [ ] [#1](backlog/001-a.md) One\n", 1, true)]
    [InlineData(
        "- [ ] [#1](backlog/001-a.md) One (after: #3)\n- [stop] Auto stops here\n- [ ] [#2](backlog/002-a.md) Two\n"
        + "- [ ] [#3](backlog/003-a.md) Three\n",
        2, true)]
    [InlineData("- [ ] [#1](backlog/001-a.md) One\n- [ ] [#2](backlog/002-a.md) Two\n", 1, false)]
    [InlineData("- [ ] [#1](backlog/001-a.md) One\n- [STOP] Auto stops here\n", 1, false)]
    public void Plan_NextIgnoresTheStopLineAndSaysWhetherItIsBelowIt(string alphaBacklog, int next, bool belowStop)
    {
        Backlog alpha = Parse(alphaBacklog);
        BacklogWaiting waiting = BacklogWaiting.From([("Alpha", alpha)]);

        ProjectPlan plan = BacklogPlan.For(alpha, "Alpha", waiting);

        Assert.Equal(next, plan.Next!.Number);
        Assert.Equal(belowStop, plan.Next.BelowStop);
    }
}
