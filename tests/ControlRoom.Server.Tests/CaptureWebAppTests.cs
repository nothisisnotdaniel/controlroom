namespace ControlRoom.Server.Tests;

/// <summary>The capture form in <c>capture.js</c>, back in its own view behind a Capture tab (#135), and its hooks in
/// <c>index.html</c>, <c>app.js</c>, <c>app.css</c> and <c>sw.js</c>, checked as text (there is no JS test runtime):
/// the tab and view, fixed texts, request fields, the stored project, the Save rule and text-only rendering. Sending from the phone is checked by hand.</summary>
public sealed class CaptureWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheCaptureTabAfterBacklogsAndItsOwnViewAndLoadsCaptureJsAfterSearchJs()
    {
        string html = await Fetch("/index.html");

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "href=\"#capture\""));
        Assert.Contains("<a href=\"#capture\" data-view=\"capture\">", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tab-label\">Capture</span>", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("data-view=\"backlogs\"", StringComparison.Ordinal)
                    < html.IndexOf("data-view=\"capture\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("data-view=\"capture\"", StringComparison.Ordinal)
                    < html.IndexOf("data-view=\"history\"", StringComparison.Ordinal));
        Assert.Contains("<main id=\"capture\" hidden></main>", html, StringComparison.Ordinal);
        Assert.Contains(
            "<script src=\"/search.js\" defer></script>\n  <script src=\"/capture.js\" defer></script>\n"
            + "  <script src=\"/history.js\" defer></script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_HashCaptureOpensTheCaptureViewTitledCaptureWithItsTabCurrent()
    {
        string app = await Fetch("/app.js");

        Assert.Contains("  if (hash === CaptureRoute) {\n    return \"capture\";\n  }", app, StringComparison.Ordinal);
        Assert.Contains("capture: \"Capture\"", Between(app, "const ViewTitles = {", "};"), StringComparison.Ordinal);
        Assert.Contains("\"capture\"", Between(app, "const Views = [", "];"), StringComparison.Ordinal);
        string route = Between(app, "function route() {", "\n}\n");
        Assert.Contains("  if (next === \"capture\") {\n    showCapture();\n    window.scrollTo(0, 0);\n    return;\n  }",
            route, StringComparison.Ordinal);
        // No redirect any more: the hash stays #capture, so showView marks the tab whose data-view is "capture".
        Assert.DoesNotContain("fromCapture", app, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureRoute)", route, StringComparison.Ordinal);
        Assert.DoesNotContain("TabForView = { capture", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_BuildsTheFormIntoItsOwnViewWithoutAHeading()
    {
        string js = await Fetch("/capture.js");
        string build = Between(js, "function buildCaptureView() {", "\n}\n");

        Assert.Contains("const main = document.getElementById(\"capture\");", build, StringComparison.Ordinal);
        Assert.Contains("main.replaceChildren(form, status);", build, StringComparison.Ordinal);
        Assert.DoesNotContain("\"h2\"", build, StringComparison.Ordinal);
        Assert.DoesNotContain("inbox-capture", js, StringComparison.Ordinal);
        Assert.Equal(1, Count(js, "replaceChildren(form, status)"));
    }

    [Fact]
    public async Task AppCss_HasNoInboxCaptureCard()
    {
        string css = await Fetch("/app.css");

        Assert.DoesNotContain("\n.capture {", css, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.capture-title {", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".inbox-", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".tasks-", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_TextsAreFixedStringsInCaptureTexts()
    {
        string js = await Fetch("/capture.js");
        string texts = Between(js, "const CaptureTexts = {", "\n};\n") + "\n";

        Assert.Contains("  projectLabel: \"Project\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  ideaLabel: \"Idea\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  save: \"Save\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  saved: \"Saved as a draft. It waits for approval in the backlog.\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("const CaptureRoute = \"#capture\";", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_HasALabelledIdeaTextareaOf2000AndAProjectSelect()
    {
        string js = await Fetch("/capture.js");
        string build = Between(js, "function buildCaptureView() {", "\n}\n");

        Assert.Contains("element(\"label\", \"capture-label\", CaptureTexts.ideaLabel)", build, StringComparison.Ordinal);
        Assert.Contains("ideaLabel.htmlFor = \"capture-text\";", build, StringComparison.Ordinal);
        Assert.Contains("element(\"textarea\", \"capture-text\")", build, StringComparison.Ordinal);
        Assert.Contains("area.setAttribute(\"maxlength\", \"2000\");", build, StringComparison.Ordinal);
        Assert.Contains("projectLabel.htmlFor = \"capture-project\";", build, StringComparison.Ordinal);
        Assert.Contains("element(\"select\", \"capture-select\")", build, StringComparison.Ordinal);
        Assert.Contains("save.disabled = true;", build, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_SaveIsDisabledWhileTheTextIsBlankOrAChangeIsBusy()
    {
        string js = await Fetch("/capture.js");
        string rule = Between(js, "function updateCaptureSave() {", "\n}\n");

        Assert.Contains("captureText().value.trim() === \"\"", rule, StringComparison.Ordinal);
        Assert.Contains("|| changeBusy", rule, StringComparison.Ordinal);
        Assert.Contains("captureProject().value === \"\"", rule, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_ListsReadableProjectsFromBacklogsAndPreselectsTheStoredOne()
    {
        string js = await Fetch("/capture.js");

        Assert.Contains("const CaptureProjectKey = \"controlroom-capture-project\";", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem(CaptureProjectKey)", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem(CaptureProjectKey, name);", js, StringComparison.Ordinal);
        string show = Between(js, "async function showCapture() {", "\n}\n");
        Assert.Contains("data = await fetchBacklogs();", show, StringComparison.Ordinal);
        Assert.Contains("fillCaptureProjects(names);", show, StringComparison.Ordinal);
        string names = Between(js, "function captureProjectNames(data) {", "\n}\n");
        Assert.Contains("asText(project.version) !== \"\"", names, StringComparison.Ordinal);
        string fill = Between(js, "function fillCaptureProjects(names) {", "\n}\n");
        Assert.Contains("storedCaptureProject()", fill, StringComparison.Ordinal);
        Assert.Contains("if (names.includes(wanted)) {\n    select.value = wanted;", fill, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_SaveFetchesTheVersionThenSendsCaptureAndClearsTheTextOnlyOnSuccess()
    {
        string js = await Fetch("/capture.js");
        string save = Between(js, "async function saveCapture() {", "\n}\n");

        int fetch = save.IndexOf("const data = await fetchBacklogs();", StringComparison.Ordinal);
        int send = save.IndexOf("const ok = await sendChange({", StringComparison.Ordinal);
        Assert.True(fetch >= 0 && send > fetch);
        Assert.Contains("version = found ? asText(found.version) : \"\";", save, StringComparison.Ordinal);
        Assert.Contains("    project: project,\n    version: version,\n    action: \"capture\",\n    text: area.value,\n",
            save, StringComparison.Ordinal);
        Assert.Contains(
            "  if (ok) {\n    storeCaptureProject(project);\n    area.value = \"\";\n"
            + "    showCaptureStatus(CaptureTexts.saved, \"capture-saved\");\n  }",
            save, StringComparison.Ordinal);
        Assert.Contains("if (changeBusy || captureSaving || project === \"\" || area.value.trim() === \"\") {", save,
            StringComparison.Ordinal);
        Assert.Contains("showEditNotice(ChangeErrors.unreachable, true);", save, StringComparison.Ordinal);
        Assert.Equal(1, Count(save, "area.value = \"\";"));
        Assert.DoesNotContain("loadInbox", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_NeverUsesInnerHtml()
    {
        string js = await Fetch("/capture.js");

        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_CaptureFieldsAre16Px()
    {
        string css = await Fetch("/app.css");
        string fields = Between(css, ".capture-select,\n.capture-text {", "}");

        Assert.Contains("  font-size: 16px;\n", fields, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_CachesCaptureJsWithTheCurrentVersion()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("const CacheVersion = \"v20\";", js, StringComparison.Ordinal);
        Assert.Contains("  \"/search.js\",\n  \"/capture.js\",\n  \"/history.js\",\n  \"/doctor.js\",\n  \"/status.js\",\n  \"/live.js\",\n", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureJs_IsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/capture.js");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        int at = text.IndexOf(part, StringComparison.Ordinal);
        while (at >= 0)
        {
            count += 1;
            at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal);
        }

        return count;
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
