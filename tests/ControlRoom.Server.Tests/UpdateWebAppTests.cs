namespace ControlRoom.Server.Tests;

/// <summary>The update banner (<c>index.html</c>, <c>board.js</c>, <c>app.css</c>), the second notification switch
/// (<c>push.js</c>) and the <c>update</c> notification (<c>sw.js</c>), read as served text. The iPhone itself is
/// checked by hand.</summary>
public sealed class UpdateWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheHiddenBannerFirstInTheBoardView()
    {
        string html = await Fetch("/index.html");

        const string Board = "<main id=\"board\" hidden>\n    <section class=\"update\" id=\"update\" role=\"status\" hidden></section>\n";
        Assert.Contains(Board, html.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_LoadsTheUpdateOncePerPageLoadNextToTheVersion()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("    loadVersion();\n    loadUpdate();\n", js, StringComparison.Ordinal);
        Assert.Contains("authFetch(\"/api/update\", { cache: \"no-store\" })", js, StringComparison.Ordinal);
        Assert.Contains("if (updateRequested || !hasToken()) {", js, StringComparison.Ordinal);
        Assert.Contains("data.available !== true", js, StringComparison.Ordinal);
        Assert.Contains("section.hidden = true;", js, StringComparison.Ordinal);
        int render = js.IndexOf("function renderUpdate(", StringComparison.Ordinal);
        int shown = js.IndexOf("section.hidden = false;", render, StringComparison.Ordinal);
        Assert.True(render >= 0 && shown > render);
    }

    [Fact]
    public async Task BoardJs_FillsTheBannerWithFixedTextsAndTextContentOnly()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("updateAvailable: \"Update available: ControlRoom \",", js, StringComparison.Ordinal);
        Assert.Contains("updateThis: \" (this is \",", js, StringComparison.Ordinal);
        Assert.Contains("updateEnd: \").\",", js, StringComparison.Ordinal);
        Assert.Contains("BoardTexts.updateAvailable + latest + BoardTexts.updateThis + current + BoardTexts.updateEnd",
            js, StringComparison.Ordinal);
        Assert.Contains("element(\"code\", \"update-command\", command)", js, StringComparison.Ordinal);
        Assert.Contains("copy: \"Copy\",", js, StringComparison.Ordinal);
        Assert.Contains("copied: \"Copied.\",", js, StringComparison.Ordinal);
        Assert.Contains("await navigator.clipboard.writeText(command);", js, StringComparison.Ordinal);
        Assert.Contains("note.textContent = BoardTexts.copied;", js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_SetsTheReleaseLinkOnlyForTheFixedPrefix()
    {
        string js = await Fetch("/board.js");

        Assert.Contains(
            "const UpdateReleasePrefix = \"https://github.com/nothisisnotdaniel/controlroom/releases/tag/\";",
            js, StringComparison.Ordinal);
        Assert.Contains("releaseNotes: \"Release notes\",", js, StringComparison.Ordinal);
        int check = js.IndexOf("if (url.startsWith(UpdateReleasePrefix)) {", StringComparison.Ordinal);
        int href = js.IndexOf("link.href = url;", StringComparison.Ordinal);
        Assert.True(check >= 0 && href > check);
        Assert.Contains("link.rel = \"noopener noreferrer\";", js, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(js, "link\\.href = "));
    }

    [Fact]
    public async Task PushJs_HasTheUpdateSwitchThatPostsNotifyUpdate()
    {
        string js = await Fetch("/push.js");

        Assert.Contains("notifyUpdate: \"Notify when an update is available\",", js, StringComparison.Ordinal);
        Assert.Contains("updateSwitch(info.notifyUpdate !== false),", js, StringComparison.Ordinal);
        Assert.Contains("return settingSwitch(PushTexts.notifyUpdate, checked, setNotifyUpdate);", js, StringComparison.Ordinal);
        Assert.Contains("pushJson(\"/api/push/settings\", { notifyUpdate: on })", js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_MapsTheUpdateKind()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("update: \"Update available\",", js, StringComparison.Ordinal);
        Assert.Contains("} else if (kind === \"update\") {", js, StringComparison.Ordinal);
        Assert.Contains(
            "const version = PushVersionPattern.test(pushField(data, \"version\")) ? pushField(data, \"version\") : \"\";",
            js, StringComparison.Ordinal);
        Assert.Contains("body = version ? \"ControlRoom \" + version : \"\";", js, StringComparison.Ordinal);
        Assert.Contains("tag = \"update:\" + version;", js, StringComparison.Ordinal);
        Assert.Contains("let url = \"/#jobs\";", js, StringComparison.Ordinal);
        Assert.Contains("const PushVersionPattern = /^(0|[1-9][0-9]{0,8})\\.", js, StringComparison.Ordinal);
        int update = js.IndexOf("} else if (kind === \"update\") {", StringComparison.Ordinal);
        int next = js.IndexOf("} else if", update + 1, StringComparison.Ordinal);
        Assert.DoesNotContain("url =", js[update..next], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheBannerWithThemeColours()
    {
        string css = await Fetch("/app.css");

        int start = css.IndexOf(".update {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string block = css[start..css.IndexOf('}', start)];
        Assert.Contains("background: var(--card);", block, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid var(--accent);", block, StringComparison.Ordinal);
        Assert.DoesNotContain("display:", block, StringComparison.Ordinal);
        Assert.Contains(".update-command {", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", css, StringComparison.Ordinal);
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }
}
