namespace ControlRoom.Server.Tests;

/// <summary>The Jobs view shows only jobs (#129): no [h] task cards, and a Stuck section for needs-human jobs that is
/// left out when there are none. Checked in <c>board.js</c>, <c>app.css</c> and <c>tasks.js</c> as text (there is no
/// JS test runtime yet, see #59); the board API still returning tasks is checked in
/// <c>BoardApiTests.Board_TasksAreTheHandLinesOfEveryProjectInOrder</c>.</summary>
public sealed class StuckJobsWebAppTests
{
    [Fact]
    public async Task BoardJs_RendersNoTasks()
    {
        string js = await Fetch("/board.js");

        Assert.DoesNotContain("renderTask", js, StringComparison.Ordinal);
        Assert.DoesNotContain("boardTasks", js, StringComparison.Ordinal);
        Assert.DoesNotContain("card-task", js, StringComparison.Ordinal);
        Assert.DoesNotContain("data.tasks", js, StringComparison.Ordinal);
        Assert.DoesNotContain("renderNeedsYou", js, StringComparison.Ordinal);
        Assert.DoesNotContain("\"needs-you\"", js, StringComparison.Ordinal);
        string stuck = Between(js, "function renderStuck(jobs) {", "\n}\n");
        Assert.DoesNotContain("task", stuck, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_AddsTheStuckSectionOnlyWithAStuckShownJob_BeforeTheStages()
    {
        string js = await Fetch("/board.js");
        string board = Between(js, "function renderBoard() {", "\n}\n");

        int filter = board.IndexOf("const stuck = shown.filter((job) => job.stage === \"needs-human\");",
            StringComparison.Ordinal);
        int nodes = board.IndexOf("const nodes = stuck.length > 0 ? [renderStuck(stuck)] : [];", StringComparison.Ordinal);
        int stages = board.IndexOf("const stages = element(\"div\", \"stages\");", StringComparison.Ordinal);
        Assert.True(filter >= 0 && filter < nodes && nodes < stages);
        Assert.DoesNotContain("hidden", Between(js, "function renderStuck(jobs) {", "\n}\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_StuckSectionIsHeadedStuckWithItsCountAndHoldsTheJobCards()
    {
        string js = await Fetch("/board.js");
        string stuck = Between(js, "function renderStuck(jobs) {", "\n}\n");

        Assert.Contains("  stuck: \"Stuck\",\n", js, StringComparison.Ordinal);
        Assert.Contains("const section = element(\"section\", \"stuck\");", stuck, StringComparison.Ordinal);
        Assert.Contains("element(\"h2\", null, sectionHeading(BoardTexts.stuck, jobs.length))", stuck,
            StringComparison.Ordinal);
        Assert.Contains("for (const job of jobs) {\n    cards.appendChild(renderCard(job));\n  }", stuck,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BoardTexts.needsYou, count", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_StuckCardIsMarkedInTextAndStillOpensTheJobPage()
    {
        string js = await Fetch("/board.js");
        string card = Between(js, "function renderCard(job) {", "\n}\n");

        Assert.Contains("element(\"a\", job.stage === \"needs-human\" ? \"card card-human\" : \"card\")", card,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (job.stage === \"needs-human\") {\n    card.appendChild(element(\"span\", \"chip chip-human card-request\", BoardTexts.needsYou));\n  }",
            card, StringComparison.Ordinal);
        Assert.Contains("card.href = jobHash(id);", card, StringComparison.Ordinal);
        int human = card.IndexOf("chip chip-human", StringComparison.Ordinal);
        int sent = card.IndexOf("\"chip chip-sent card-request\", sent", StringComparison.Ordinal);
        Assert.True(human >= 0 && human < sent);
        Assert.Contains("approveSent: \"Approve sent\",", js, StringComparison.Ordinal);
        Assert.Contains("reworkSent: \"Rework sent\",", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheStuckSectionAndTheHumanChip_AndDropsTheTaskCardRule()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".stuck {\n  margin: 0 0 16px;", css, StringComparison.Ordinal);
        Assert.Contains(".stuck > h2 {\n  color: var(--human);\n}", css, StringComparison.Ordinal);
        Assert.Contains(".chip-human {\n  background: var(--human-bg);\n  color: var(--human);", css,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".needs-you", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".card-rest", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_CountStillComesFromTheTasksApi()
    {
        string tasks = await Fetch("/tasks.js");

        Assert.Contains("const TasksApi = \"/api/tasks\";", tasks, StringComparison.Ordinal);
        Assert.Contains("const badge = document.getElementById(\"tasks-count\");", tasks, StringComparison.Ordinal);
        Assert.Contains("badge.textContent = shown ? String(tasksCount) : \"\";", tasks, StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text[from..to];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }
}
