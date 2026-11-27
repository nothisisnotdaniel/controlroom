namespace ControlRoom.Server.Tests;

/// <summary>The updater's last check line under the <c>Check for updates</c> select (<c>board.js</c>) and its notes in
/// <c>CLAUDE.md</c>, read as served or committed text. The line itself is checked on the iPhone by hand.</summary>
public sealed class UpdateLastCheckWebAppTests
{
    private const string LoadCall =
        "    renderUpdateInterval(asText(data && data.current),\n"
        + "      data && (data.channel === \"stable\" || data.channel === \"develop\") ? data.channel : null,\n"
        + "      data && UpdateIntervals.includes(data.updateInterval) ? data.updateInterval : null,\n"
        + "      data && typeof data.lastCheck === \"string\" && LastCheckPattern.test(data.lastCheck) ? data.lastCheck : null);";

    [Fact]
    public async Task BoardJs_HasTheFixedTextsAndPassesOnlyAValidLastCheck()
    {
        string js = await BoardJs();

        Assert.Contains("  lastChecked: \"Last checked \",\n", js, StringComparison.Ordinal);
        Assert.Contains("  notChecked: \"Not checked yet\",\n", js, StringComparison.Ordinal);
        Assert.Contains("const LastCheckPattern = /^\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ$/;", js,
            StringComparison.Ordinal);
        Assert.Contains(LoadCall, js, StringComparison.Ordinal);
        Assert.Contains("function renderUpdateInterval(current, channel, interval, lastCheck) {", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderUpdateInterval_BuildsTheLineAfterTheGuardBetweenTheLabelAndTheNote()
    {
        string js = await BoardJs();
        string render = Body(js, "function renderUpdateInterval(");

        int guard = render.IndexOf(
            "if (current === \"\" || current === \"dev\" || channel === null || interval === null || box.hidden) {\n"
            + "    return;\n  }",
            StringComparison.Ordinal);
        int line = render.IndexOf("const lastLine = element(\"p\", \"auto-update-text update-last-check\");",
            StringComparison.Ordinal);
        int text = render.IndexOf("lastLine.textContent = ", StringComparison.Ordinal);
        int append = render.IndexOf("box.append(label, lastLine, note);", StringComparison.Ordinal);
        Assert.True(guard >= 0 && line > guard && text > line && append > text);
        Assert.Equal(1, Count(render, "return;"));
        Assert.Contains("const checkedAt = lastCheck === null ? null : new Date(lastCheck);", render, StringComparison.Ordinal);
        Assert.Contains(
            "lastLine.textContent = checkedAt === null || Number.isNaN(checkedAt.getTime())\n"
            + "    ? BoardTexts.notChecked\n"
            + "    : BoardTexts.lastChecked + formatTime(checkedAt, false);",
            render, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMd_NamesThePinTheFieldAndTheStamp()
    {
        string text = File.ReadAllText(Path.Combine(RepoCopy.RepoFolder(), "CLAUDE.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("(`<PackageReference Include=\"RoomKit\" Version=\"0.3.0\" GeneratePathProperty=\"true\" />`)", text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Include=\"RoomKit\" Version=\"0.2.0\"", text, StringComparison.Ordinal);
        Assert.Contains("command, autoUpdate, channel, updateInterval, lastCheck}`", text, StringComparison.Ordinal);
        Assert.Contains("RoomKit's `last-check` stamp", text, StringComparison.Ordinal);
        Assert.Contains("`last-check` is written only by the updater (`scripts/updater.sh`, `updater.ps1`)", text, StringComparison.Ordinal);
        Assert.Contains("`p.update-last-check`", text, StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
            at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Body(string js, string start)
    {
        int from = js.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " not found");
        int end = js.IndexOf("\n}\n", from, StringComparison.Ordinal);
        Assert.True(end > from);
        return js[from..end];
    }

    private static async Task<string> BoardJs()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return (await client.GetStringAsync("/board.js")).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
