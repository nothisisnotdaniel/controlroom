namespace ControlRoom.Server.Tests;

/// <summary>The "Install updates automatically" switch (<c>index.html</c>, <c>board.js</c>) and the <c>updated</c> /
/// <c>update_failed</c> notifications (<c>sw.js</c>), read as served text. The iPhone itself is checked by
/// hand.</summary>
public sealed class AutoUpdateWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheHiddenSwitchBoxAfterTheVersionLine()
    {
        string html = (await Fetch("/index.html")).Replace("\r\n", "\n", StringComparison.Ordinal);

        int version = html.IndexOf("id=\"app-version\"", StringComparison.Ordinal);
        int box = html.IndexOf("<div class=\"auto-update\" id=\"auto-update\" hidden></div>", StringComparison.Ordinal);
        Assert.True(version >= 0 && box > version);
    }

    [Fact]
    public async Task BoardJs_ShowsTheSwitchOnlyForAReleaseWithFixedTexts()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("autoUpdate: \"Install updates automatically\",", js, StringComparison.Ordinal);
        Assert.Contains("autoUpdateUnreadable: \"Can't read the update setting.\",", js, StringComparison.Ordinal);
        Assert.Contains("autoUpdateFailed: \"Can't change the update setting. Try again.\",", js, StringComparison.Ordinal);
        Assert.Contains(
            "renderAutoUpdate(asText(data && data.current), data && typeof data.autoUpdate === \"boolean\" ? data.autoUpdate : null);",
            js, StringComparison.Ordinal);
        string render = Body(js, "function renderAutoUpdate(");
        Assert.Contains("if (current === \"\" || current === \"dev\") {", render, StringComparison.Ordinal);
        int nullCheck = render.IndexOf("if (autoUpdate === null) {", StringComparison.Ordinal);
        int unreadable = render.IndexOf("BoardTexts.autoUpdateUnreadable", StringComparison.Ordinal);
        int switchStart = render.IndexOf("input.setAttribute(\"role\", \"switch\");", StringComparison.Ordinal);
        Assert.True(nullCheck >= 0 && unreadable > nullCheck && switchStart > unreadable);
        Assert.Contains("return;\n  }\n  const label", render, StringComparison.Ordinal);
        Assert.Contains("input.checked = autoUpdate;", render, StringComparison.Ordinal);
        Assert.Contains("input.addEventListener(\"change\", () => setAutoUpdate(input, note));", render, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", null, BoardTexts.autoUpdate)", render, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_PostsTheTappedValueAndRevertsOnAnyError()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("const AutoUpdateRoute = \"/api/update/settings\";", js, StringComparison.Ordinal);
        string set = Body(js, "async function setAutoUpdate(");
        Assert.Contains("const wanted = input.checked;", set, StringComparison.Ordinal);
        Assert.Contains("authFetch(AutoUpdateRoute, {", set, StringComparison.Ordinal);
        Assert.Contains("method: \"POST\",", set, StringComparison.Ordinal);
        Assert.Contains("body: JSON.stringify({ autoUpdate: wanted }),", set, StringComparison.Ordinal);
        Assert.Contains("if (!response.ok) {", set, StringComparison.Ordinal);
        Assert.Contains("data.autoUpdate !== wanted", set, StringComparison.Ordinal);
        int catchAt = set.IndexOf("} catch {", StringComparison.Ordinal);
        int revert = set.IndexOf("input.checked = !wanted;", StringComparison.Ordinal);
        int text = set.IndexOf("note.textContent = BoardTexts.autoUpdateFailed;", StringComparison.Ordinal);
        int shown = set.IndexOf("note.hidden = false;", StringComparison.Ordinal);
        Assert.True(catchAt >= 0 && revert > catchAt && text > revert && shown > text);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_ShowsTheChannelChoiceOnlyForAReleaseWithReadableSettings()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("  updateChannel: \"Update channel\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  channelStable: \"Stable\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  channelDevelop: \"Develop (prereleases)\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  channelFailed: \"Can't change the update channel. Try again.\",\n", js, StringComparison.Ordinal);
        int autoUpdate = js.IndexOf("    renderAutoUpdate(asText(data && data.current),", StringComparison.Ordinal);
        int channel = js.IndexOf(
            "    renderUpdateChannel(asText(data && data.current),\n"
            + "      data && (data.channel === \"stable\" || data.channel === \"develop\") ? data.channel : null);",
            StringComparison.Ordinal);
        Assert.True(autoUpdate >= 0 && channel > autoUpdate);
        string render = Body(js, "function renderUpdateChannel(");
        int guard = render.IndexOf(
            "if (current === \"\" || current === \"dev\" || channel === null || box.hidden) {\n    return;\n  }",
            StringComparison.Ordinal);
        int select = render.IndexOf("document.createElement(\"select\")", StringComparison.Ordinal);
        Assert.True(guard >= 0 && select > guard);
        Assert.Contains("[[\"stable\", BoardTexts.channelStable], [\"develop\", BoardTexts.channelDevelop]]", render,
            StringComparison.Ordinal);
        Assert.Contains("select.value = channel;", render, StringComparison.Ordinal);
        Assert.Contains("select.addEventListener(\"change\", () => setUpdateChannel(select, note));", render,
            StringComparison.Ordinal);
        Assert.Contains("element(\"span\", null, BoardTexts.updateChannel)", render, StringComparison.Ordinal);
        Assert.Contains("box.append(label, note);", render, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_PostsTheChosenChannelAndRevertsOnAnyError()
    {
        string js = await Fetch("/board.js");

        string set = Body(js, "async function setUpdateChannel(");
        Assert.Contains("const wanted = select.value;", set, StringComparison.Ordinal);
        Assert.Contains("const saved = select.dataset.saved;", set, StringComparison.Ordinal);
        Assert.Contains("authFetch(AutoUpdateRoute, {", set, StringComparison.Ordinal);
        Assert.Contains("method: \"POST\",", set, StringComparison.Ordinal);
        Assert.Contains("body: JSON.stringify({ channel: wanted }),", set, StringComparison.Ordinal);
        Assert.DoesNotContain("autoUpdate", set, StringComparison.Ordinal);
        Assert.Contains("if (!response.ok) {", set, StringComparison.Ordinal);
        Assert.Contains("data.channel !== wanted", set, StringComparison.Ordinal);
        int saved = set.IndexOf("select.dataset.saved = wanted;", StringComparison.Ordinal);
        int catchAt = set.IndexOf("} catch {", StringComparison.Ordinal);
        int revert = set.IndexOf("select.value = saved;", StringComparison.Ordinal);
        int text = set.IndexOf("note.textContent = BoardTexts.channelFailed;", StringComparison.Ordinal);
        int shown = set.IndexOf("note.hidden = false;", StringComparison.Ordinal);
        Assert.True(saved >= 0 && catchAt > saved && revert > catchAt && text > revert && shown > text);
    }

    [Fact]
    public async Task BoardJs_ShowsTheIntervalChoiceAfterTheChannelChoiceWithFixedTexts()
    {
        string js = (await Fetch("/board.js")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("  updateInterval: \"Check for updates\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  interval30: \"Every 30 minutes\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  interval60: \"Every hour\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  interval120: \"Every 2 hours\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  interval360: \"Every 6 hours\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  interval720: \"Every 12 hours\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  intervalFailed: \"Can't change the update interval. Try again.\",\n", js, StringComparison.Ordinal);
        Assert.Contains("const UpdateIntervals = [30, 60, 120, 360, 720];", js, StringComparison.Ordinal);
        int channel = js.IndexOf("    renderUpdateChannel(asText(data && data.current),", StringComparison.Ordinal);
        int interval = js.IndexOf(
            "    renderUpdateInterval(asText(data && data.current),\n"
            + "      data && (data.channel === \"stable\" || data.channel === \"develop\") ? data.channel : null,\n"
            + "      data && UpdateIntervals.includes(data.updateInterval) ? data.updateInterval : null);",
            StringComparison.Ordinal);
        Assert.True(channel >= 0 && interval > channel);
        string render = Body(js, "function renderUpdateInterval(");
        int guard = render.IndexOf(
            "if (current === \"\" || current === \"dev\" || channel === null || interval === null || box.hidden) {\n"
            + "    return;\n  }",
            StringComparison.Ordinal);
        int select = render.IndexOf("document.createElement(\"select\")", StringComparison.Ordinal);
        Assert.True(guard >= 0 && select > guard);
        Assert.Contains("const texts = [BoardTexts.interval30, BoardTexts.interval60, BoardTexts.interval120, "
            + "BoardTexts.interval360,\n    BoardTexts.interval720];", render, StringComparison.Ordinal);
        Assert.Contains("UpdateIntervals.forEach((minutes, index) => {", render, StringComparison.Ordinal);
        Assert.Contains("option.value = String(minutes);", render, StringComparison.Ordinal);
        Assert.Contains("select.value = String(interval);", render, StringComparison.Ordinal);
        Assert.Contains("select.dataset.saved = String(interval);", render, StringComparison.Ordinal);
        Assert.Contains("select.addEventListener(\"change\", () => setUpdateInterval(select, note));", render,
            StringComparison.Ordinal);
        Assert.Contains("element(\"span\", null, BoardTexts.updateInterval)", render, StringComparison.Ordinal);
        Assert.Contains("box.append(label, note);", render, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_PostsOnlyTheChosenIntervalAndRevertsOnAnyError()
    {
        string js = await Fetch("/board.js");

        string set = Body(js, "async function setUpdateInterval(");
        Assert.Contains("const wanted = Number(select.value);", set, StringComparison.Ordinal);
        Assert.Contains("const saved = select.dataset.saved;", set, StringComparison.Ordinal);
        Assert.Contains("authFetch(AutoUpdateRoute, {", set, StringComparison.Ordinal);
        Assert.Contains("method: \"POST\",", set, StringComparison.Ordinal);
        Assert.Contains("body: JSON.stringify({ updateInterval: wanted }),", set, StringComparison.Ordinal);
        Assert.DoesNotContain("autoUpdate", set, StringComparison.Ordinal);
        Assert.DoesNotContain("channel", set, StringComparison.Ordinal);
        Assert.Contains("if (!response.ok) {", set, StringComparison.Ordinal);
        Assert.Contains("data.updateInterval !== wanted", set, StringComparison.Ordinal);
        int saved = set.IndexOf("select.dataset.saved = String(wanted);", StringComparison.Ordinal);
        int catchAt = set.IndexOf("} catch {", StringComparison.Ordinal);
        int revert = set.IndexOf("select.value = saved;", StringComparison.Ordinal);
        int text = set.IndexOf("note.textContent = BoardTexts.intervalFailed;", StringComparison.Ordinal);
        int shown = set.IndexOf("note.hidden = false;", StringComparison.Ordinal);
        Assert.True(saved >= 0 && catchAt > saved && revert > catchAt && text > revert && shown > text);
    }

    [Fact]
    public async Task SwJs_HasFixedTitlesAndBodiesForTheUpdateResults()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("  updated: \"ControlRoom updated\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  update_failed: \"Update failed\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  check: \"The download didn't check out. Nothing was changed.\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  no_answer: \"The new version didn't start, so the old one is back.\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  swap: \"The new version couldn't be put in place, so the old one is back.\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  bootstrap_failed: \"The new version couldn't be started, so the old one is back.\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  rollback_failed: \"The old version didn't come back. Check the Mac.\",\n", js, StringComparison.Ordinal);
        Assert.Contains(
            "body = version ? \"Now \" + version + (previous ? \" (was \" + previous + \").\" : \".\") : \"\";",
            js, StringComparison.Ordinal);
        Assert.Contains("const version = pushVersion(data, \"version\");", js, StringComparison.Ordinal);
        Assert.Contains("const previous = pushVersion(data, \"previous\");", js, StringComparison.Ordinal);
        Assert.Contains("return PushVersionPattern.test(pushField(data, name)) ? pushField(data, name) : \"\";", js,
            StringComparison.Ordinal);
        Assert.Contains(
            "body = Object.prototype.hasOwnProperty.call(UpdateFailedBodies, reason) ? UpdateFailedBodies[reason] : \"\";",
            js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwJs_OpensJobsForTheUpdateResultsAndNeverCachesHealth()
    {
        string js = await Fetch("/sw.js");

        string notification = Body(js, "function pushNotification(");
        Assert.Contains("let url = \"/#jobs\";", notification, StringComparison.Ordinal);
        int updated = notification.IndexOf("} else if (kind === \"updated\") {", StringComparison.Ordinal);
        int test = notification.IndexOf("} else if (kind === \"test\") {", StringComparison.Ordinal);
        Assert.True(updated >= 0 && test > updated);
        Assert.DoesNotContain("url =", notification[updated..test], StringComparison.Ordinal);
        Assert.Contains("if (url.pathname.startsWith(\"/api/\") || url.pathname === \"/health\") {", js,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"/health\",", js, StringComparison.Ordinal);
    }

    /// <summary>The text from <paramref name="start"/> to the first closing brace at the start of a line.</summary>
    private static string Body(string js, string start)
    {
        string text = js.Replace("\r\n", "\n", StringComparison.Ordinal);
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " not found");
        int end = text.IndexOf("\n}\n", from, StringComparison.Ordinal);
        Assert.True(end > from);
        return text[from..end];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }
}
