using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>doctor.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as the served text (there is no JS test runtime, as in
/// <see cref="HistoryWebAppTests"/>). The look is checked by hand at 375 px in light and dark mode.</summary>
public sealed partial class DoctorWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheHealthLinkLastWithNoCountAndTheDoctorView()
    {
        string html = await Fetch("/index.html");

        const string Link = "<a href=\"#doctor\" data-view=\"doctor\">";
        const string Tail = "<span class=\"tab-label\">Health</span>\n    </a>\n  ";
        int history = html.IndexOf("<a href=\"#history\" data-view=\"history\">", StringComparison.Ordinal);
        int health = html.IndexOf(Link, StringComparison.Ordinal);
        int navEnd = html.IndexOf("</nav>", StringComparison.Ordinal);
        Assert.True(history >= 0 && health > history && health < navEnd);
        Assert.EndsWith(Tail, html[health..navEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("nav-count", html[health..navEnd], StringComparison.Ordinal);
        Assert.Contains("<main id=\"doctor\" hidden></main>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("doctor-count", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsDoctorJsAfterHistoryJsAndBeforeLiveJs()
    {
        string html = await Fetch("/index.html");

        Assert.Contains(
            "<script src=\"/history.js\" defer></script>\n  <script src=\"/doctor.js\" defer></script>\n"
            + "  <script src=\"/status.js\" defer></script>\n  <script src=\"/releases.js\" defer></script>\n  <script src=\"/live.js\" defer></script>", html, StringComparison.Ordinal);
        Assert.Equal(15, ScriptTag().Matches(html).Count);
    }

    [Fact]
    public async Task AppJs_RoutesHashDoctorToTheDoctorViewTitledHealth()
    {
        string app = await Fetch("/app.js");
        string js = await Fetch("/doctor.js");

        Assert.Contains("\"history\", \"doctor\", \"job\", \"story\"];", app, StringComparison.Ordinal);
        Assert.Contains("history: \"History\", doctor: \"Health\" };", app, StringComparison.Ordinal);
        Assert.Contains("  if (hash === DoctorRoute) {\n    return \"doctor\";\n  }", app, StringComparison.Ordinal);
        Assert.Contains("  if (next === \"doctor\") {\n    showDoctor();\n    return;\n  }", app, StringComparison.Ordinal);
        Assert.Contains("const DoctorRoute = \"#doctor\";", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorJs_FetchesOnlyThroughAuthFetchWithNoStoreOnEveryShowAndNeverOnATimer()
    {
        string js = await Fetch("/doctor.js");

        Assert.Contains("const DoctorApi = \"/api/doctor\";", js, StringComparison.Ordinal);
        Assert.Contains("await authFetch(DoctorApi, { cache: \"no-store\" });", js, StringComparison.Ordinal);
        Assert.Single(AuthFetchCall().Matches(js));
        Assert.DoesNotContain(" fetch(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("method:", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
        string show = Between(js, "function showDoctor() {", "\n}\n");
        Assert.Contains("loadDoctor();", show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorJs_HasTheHeadingsAndTheFixedTextsOfWhatDoctorWouldDo()
    {
        string js = await Fetch("/doctor.js");
        string texts = Between(js, "const DoctorTexts = {", "\n};\n") + "\n";

        Assert.Contains("  stuck: \"Stuck jobs\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  stuckHelp: \"Doctor sets it to needs-human so you can approve or rework it.\",\n", texts,
            StringComparison.Ordinal);
        Assert.Contains("  uncommitted: \"Uncommitted job records\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  uncommittedHelp: \"Doctor commits and pushes the job folder.\",\n", texts,
            StringComparison.Ordinal);
        Assert.Contains("  branches: \"Merged leftover branches\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  branchesHelp: \"Doctor deletes the local branch with git branch -d.\",\n", texts,
            StringComparison.Ordinal);
        Assert.Contains("  stories: \"Story problems\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  storiesHelp: \"Doctor lists these only; fix the backlog by hand.\",\n", texts,
            StringComparison.Ordinal);
        Assert.Contains("  healthy: \"All healthy.\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  unreadable: \"Could not check: \",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  failed: \"Could not load the health check.\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  noUpdate: \"no update time\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  hours: \" h\",\n", texts, StringComparison.Ordinal);
        Assert.DoesNotContain("data.error", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorJs_ShowsOneSectionPerNonEmptyListInOrder()
    {
        string js = await Fetch("/doctor.js");
        string render = Between(js, "function renderDoctor() {", "\n}\n");

        string[] sections =
        [
            "if (data.stuck.length > 0) {\n    sections.push(renderDoctorSection(\"doctor-stuck\", DoctorTexts.stuck, DoctorTexts.stuckHelp,",
            "if (data.uncommitted.length > 0) {\n    sections.push(renderDoctorSection(\"doctor-uncommitted\", DoctorTexts.uncommitted, DoctorTexts.uncommittedHelp,",
            "if (data.branches.length > 0) {\n    sections.push(renderDoctorSection(\"doctor-branches\", DoctorTexts.branches, DoctorTexts.branchesHelp,",
            "if (data.stories.length > 0) {\n    sections.push(renderDoctorSection(\"doctor-stories\", DoctorTexts.stories, DoctorTexts.storiesHelp,",
        ];
        int[] positions = sections.Select(part => render.IndexOf(part, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(position => position), positions);
        string section = Between(js, "function renderDoctorSection(id, title, help, items) {", "\n}\n");
        Assert.Contains("element(\"h2\", \"doctor-heading\", title)", section, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"doctor-help\", help)", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorJs_ShowsOnlyAllHealthyWhenEveryListIsEmpty_AndTheUnreadableNames()
    {
        string js = await Fetch("/doctor.js");
        string render = Between(js, "function renderDoctor() {", "\n}\n");

        Assert.Contains(
            "if (sections.length === 0 && data.unreadable.length === 0) {\n    showDoctorStatus(DoctorTexts.healthy, \"empty\");",
            render, StringComparison.Ordinal);
        Assert.Contains("document.getElementById(\"doctor-content\").replaceChildren(...sections);", render,
            StringComparison.Ordinal);
        Assert.Contains("DoctorTexts.unreadable + data.unreadable.join(\", \")", render, StringComparison.Ordinal);
        Assert.Contains("unreadable.hidden = data.unreadable.length === 0;", render, StringComparison.Ordinal);
        string load = Between(js, "async function loadDoctor() {", "\n}\n");
        Assert.Contains("showDoctorStatus(DoctorTexts.failed, \"error\");", load, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorJs_LinksStuckJobsWithStageAndHours_AndNumberedStoryProblems()
    {
        string js = await Fetch("/doctor.js");
        string stuck = Between(js, "function renderDoctorStuck(job) {", "\n}\n");
        string story = Between(js, "function renderDoctorStory(story) {", "\n}\n");

        Assert.Contains("link.href = jobHash(job.id);", stuck, StringComparison.Ordinal);
        Assert.Contains(
            "const age = job.hours === null ? DoctorTexts.noUpdate : job.hours + DoctorTexts.hours;", stuck,
            StringComparison.Ordinal);
        Assert.Contains("job.stage + \", \" + age", stuck, StringComparison.Ordinal);
        Assert.Contains("if (story.number !== null) {", story, StringComparison.Ordinal);
        Assert.Contains("link.href = storyHash(story.project, story.number);", story, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"doctor-line\", story.line)", story, StringComparison.Ordinal);
        Assert.Contains("DoctorProblems[story.problem]", story, StringComparison.Ordinal);
        Assert.Contains("Object.hasOwn(DoctorProblems, item.problem)", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorJs_SetsFileTextOnlyAsTextContent_SoATagInATitleIsText()
    {
        string js = await Fetch("/doctor.js");

        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", js, StringComparison.Ordinal);
        Assert.DoesNotContain("DOMParser", js, StringComparison.Ordinal);
        Assert.DoesNotContain("createContextualFragment", js, StringComparison.Ordinal);
        Assert.Equal(
            [".textContent = text;", ".textContent = data.unreadable.length > 0 ? DoctorTexts.unreadable + data.unreadable.join(\", \") : \"\";"],
            TextAssignment().Matches(js).Select(match => match.Value.Trim()));
        Assert.Contains("element(\"a\", \"doctor-title\", \"#\" + story.number + \" \" + story.line)", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_RefetchesTheDoctorViewWhileShownWhenJobsOrBacklogsChange()
    {
        string live = await Fetch("/live.js");

        Assert.Contains("if (recovered || doctorInputsChanged(previous, next)) {\n    doctorStale = true;\n  }", live,
            StringComparison.Ordinal);
        string changed = Between(live, "function doctorInputsChanged(previous, next) {", "\n}\n");
        Assert.Contains("if (previous === null) {\n    return false;\n  }", changed, StringComparison.Ordinal);
        Assert.Contains("previous.jobs !== next.jobs", changed, StringComparison.Ordinal);
        Assert.Contains("before.backlog !== after.backlog", changed, StringComparison.Ordinal);
        Assert.Contains("before.stories !== after.stories", changed, StringComparison.Ordinal);
        Assert.Contains("await refreshStaleDoctor();", live, StringComparison.Ordinal);
        string refresh = Between(live, "async function refreshStaleDoctor() {", "\n}\n");
        Assert.Contains("if (document.getElementById(\"doctor\").hidden) {\n    return;\n  }", refresh,
            StringComparison.Ordinal);
        Assert.Contains("if (!(await loadDoctor())) {\n    doctorStale = true;\n  }", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("api/doctor", live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_FitsEightTabsInOneRowAndStylesTheView()
    {
        string css = await Fetch("/app.css");

        Assert.DoesNotContain("two rows of four", css, StringComparison.Ordinal);
        foreach (string rule in new[]
                 {
                     ".doctor-section {", ".doctor-heading {", ".doctor-help {", ".doctor-list {", ".doctor-item {",
                     ".doctor-title {", ".doctor-unreadable {",
                 })
        {
            Assert.Contains(rule, css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ServiceWorker_CachesDoctorJsWithVersion15AndNeverTheApi()
    {
        string js = await Fetch("/sw.js");
        string room = await Fetch("/room.js");

        Assert.Contains("const CacheVersion = \"v23\";", room, StringComparison.Ordinal);
        Assert.Contains("  \"/history.js\",\n  \"/doctor.js\",\n  \"/status.js\",\n  \"/releases.js\",\n  \"/live.js\",\n", room, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/doctor", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/doctor", room, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorJs_IsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/doctor.js");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to >= 0, end);
        return text[(from + start.Length)..to];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex("<script ")]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"authFetch\(")]
    private static partial Regex AuthFetchCall();

    [GeneratedRegex(@"\.textContent\s*=[^;]*;")]
    private static partial Regex TextAssignment();
}
