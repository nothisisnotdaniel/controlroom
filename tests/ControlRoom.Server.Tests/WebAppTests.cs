using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the served web app files (<c>index.html</c>, <c>app.js</c>, <c>app.css</c>). Layout at 375 px and
/// the look in light and dark mode are checked by hand.</summary>
public sealed partial class WebAppTests
{
    [Fact]
    public async Task IndexHtml_HasViewportColorSchemeAndOnlySameOriginFiles()
    {
        string html = await Fetch("/index.html");

        Assert.Contains("<meta name=\"viewport\" content=\"width=device-width", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\">", html, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/app.js\" defer></script>", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/app.css\">", html, StringComparison.Ordinal);
        Assert.Single(ScriptTag().Matches(html));
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" on", AttributeStarts(html), StringComparison.Ordinal);
        Assert.DoesNotContain("//", string.Concat(SourceAttribute().Matches(html).Select(match => match.Value)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_FetchesBacklogsAndRendersGroupsInOrder()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("fetch(\"/api/backlogs\"", js, StringComparison.Ordinal);
        int open = js.IndexOf("label: \"Open\"", StringComparison.Ordinal);
        int approval = js.IndexOf("label: \"Waiting for approval\"", StringComparison.Ordinal);
        int user = js.IndexOf("label: \"For you\"", StringComparison.Ordinal);
        int done = js.IndexOf("label: \"Done\", collapsed: true", StringComparison.Ordinal);
        Assert.True(open >= 0 && open < approval && approval < user && user < done);
        Assert.Contains("element(\"details\", \"group\")", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".open = true", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setAttribute(\"open\"", js, StringComparison.Ordinal);
        Assert.Contains("group.label + \" (\" + entries.length + \")\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"strong\", \"title\"", js, StringComparison.Ordinal);
        Assert.Contains("if (rest !== \"\")", js, StringComparison.Ordinal);
        Assert.Contains("element(\"h2\", null, asText(project && project.name))", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_UsesOnlyFixedErrorTexts()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("unreachable: \"Can't reach the server.\"", js, StringComparison.Ordinal);
        Assert.Contains("unreadable: \"Can't read backlog.md.\"", js, StringComparison.Ordinal);
        Assert.Contains("noLines: \"No backlog lines.\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", Texts.unreadable)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", Texts.unreachable)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"empty\", Texts.noLines)", js, StringComparison.Ordinal);
        Assert.Contains("catch {", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
        Assert.DoesNotContain("statusText", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RendersNumberedItemsWithMarkWaitingAndDependencyLinks()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("const Marks = { \" \": \"[ ]\", \"?\": \"[?]\", h: \"[h]\", x: \"[x]\" };", js,
            StringComparison.Ordinal);
        Assert.Contains("waiting: \"Waiting\"", js, StringComparison.Ordinal);
        Assert.Contains("if (entry.waiting === true)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"badge waiting\", Texts.waiting)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"number\", \"#\" + number)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"strong\", \"title\", asText(entry.title))", js, StringComparison.Ordinal);
        Assert.Contains("const StoryRoute = \"#story/\";", js, StringComparison.Ordinal);
        Assert.Contains("return StoryRoute + encodeURIComponent(project) + \"/\" + number;", js,
            StringComparison.Ordinal);
        Assert.Contains("link.href = storyHash(target, number);", js, StringComparison.Ordinal);
        Assert.Contains("const target = asText(dependency.project) || projectName;", js, StringComparison.Ordinal);
        Assert.Contains("head.href = storyHash(projectName, number);", js, StringComparison.Ordinal);
        // Lines without a number keep the old rendering and get no story link.
        Assert.Contains("if (number !== null) {", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ShowsTheStoryViewWithBackAndAFixedErrorText()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("storyFailed: \"Can't load this story.\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", Texts.storyFailed)", js, StringComparison.Ordinal);
        Assert.Contains(
            "\"/api/story?project=\" + encodeURIComponent(target.project) + \"&number=\" + "
            + "encodeURIComponent(target.number)",
            js,
            StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"hashchange\", route);", js, StringComparison.Ordinal);
        Assert.Contains("element(\"button\", \"back\", Texts.back)", js, StringComparison.Ordinal);
        Assert.Contains("history.back();", js, StringComparison.Ordinal);
        Assert.Contains(
            "asText(data && data.project) + \" #\" + asNumber(data && data.number) + \" \" + "
            + "asText(data && data.title)",
            js,
            StringComparison.Ordinal);
        Assert.Contains("parent.appendChild(element(\"code\", null, text));", js, StringComparison.Ordinal);
        Assert.Contains("parent.appendChild(element(\"span\", null, text));", js, StringComparison.Ordinal);
        Assert.Contains(
            "pre.appendChild(element(\"code\", null, asText(block.text)));", js, StringComparison.Ordinal);
        Assert.DoesNotContain("createTextNode", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".href = asText", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_HasAHiddenStoryView()
    {
        string html = await Fetch("/index.html");

        Assert.Contains("<main id=\"story\" hidden></main>", html, StringComparison.Ordinal);
        Assert.Contains("<header class=\"top\" id=\"top\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesBadgesStoryAndAThumbSizedBack()
    {
        string css = await Fetch("/app.css");

        Assert.Contains("--waiting-bg:", css, StringComparison.Ordinal);
        Assert.Contains(".waiting {", css, StringComparison.Ordinal);
        Assert.Contains(".story-bar {", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", css, StringComparison.Ordinal);
        Assert.Contains("[hidden] {", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("http://")]
    [InlineData("https://")]
    public async Task AppJs_AvoidsHtmlInjectionApis(string forbidden)
    {
        string js = await Fetch("/app.js");

        Assert.DoesNotContain(forbidden, js, StringComparison.Ordinal);
        Assert.Contains("textContent", js, StringComparison.Ordinal);
        Assert.Contains("document.createElement", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_SupportsLightAndDarkAndWrapsLongText()
    {
        string css = await Fetch("/app.css");

        Assert.Contains("@media (prefers-color-scheme: dark)", css, StringComparison.Ordinal);
        Assert.Contains("color-scheme: light dark;", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", css, StringComparison.Ordinal);
        Assert.Contains("box-sizing: border-box;", css, StringComparison.Ordinal);
        Assert.Contains("--error:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@import", css, StringComparison.Ordinal);
        Assert.DoesNotContain("url(", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(FixedWideWidth(), css);
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    private static string AttributeStarts(string html)
    {
        return string.Concat(AttributeName().Matches(html).Select(match => match.Value.ToLowerInvariant()));
    }

    [GeneratedRegex("<script\\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex("(src|href)=\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex SourceAttribute();

    [GeneratedRegex("\\s[a-z-]+=", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeName();

    [GeneratedRegex("(?<!max-)width:\\s*([4-9]\\d\\d|\\d{4,})px")]
    private static partial Regex FixedWideWidth();
}
