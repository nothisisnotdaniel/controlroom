namespace ControlRoom.Server.Tests;

/// <summary>Checks the position numbers on Backlogs in <c>app.js</c> and <c>app.css</c> as text (there is no JS test
/// runtime): every line of every group and every row of the plan's Waiting group starts with its 1-based place, read
/// as "position N" by a screen reader and styled apart from <c>#N</c>. The look and VoiceOver are checked by
/// hand.</summary>
public sealed class PositionNumbersWebAppTests
{
    [Fact]
    public async Task AppJs_RenderLinesCountsEveryEntryFromOneInOrder()
    {
        string app = await Fetch("/app.js");
        string lines = Between(app, "function renderLines(projectName, version, entries) {", "\n}\n");

        int start = lines.IndexOf("let position = 0;", StringComparison.Ordinal);
        int loop = lines.IndexOf("for (const entry of entries) {", StringComparison.Ordinal);
        int step = lines.IndexOf("position += 1;", StringComparison.Ordinal);
        int number = lines.IndexOf("const number = asNumber(entry && entry.number);", StringComparison.Ordinal);
        Assert.True(start >= 0 && start < loop && loop < step && step < number);
        Assert.Equal(1, Count(lines, "position += 1;"));
        Assert.Contains("list.appendChild(renderNumberedLine(projectName, version, entry, number, position));", lines,
            StringComparison.Ordinal);
        Assert.Contains("item.appendChild(positionElement(position));", lines, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_NumberedLineStartsItsHeadWithThePosition()
    {
        string app = await Fetch("/app.js");
        string line = Between(app, "function renderNumberedLine(projectName, version, entry, number, position) {", "\n}\n");

        int position = line.IndexOf("head.appendChild(positionElement(position));", StringComparison.Ordinal);
        int firstAppend = line.IndexOf("head.appendChild(", StringComparison.Ordinal);
        int mark = line.IndexOf("head.appendChild(element(\"span\", \"mark\"", StringComparison.Ordinal);
        int hash = line.IndexOf("head.appendChild(element(\"span\", \"number\", \"#\" + number));", StringComparison.Ordinal);
        Assert.True(position >= 0 && position == firstAppend);
        Assert.True(position < mark && mark < hash);
    }

    [Fact]
    public async Task AppJs_UnnumberedLineStartsWithThePosition()
    {
        string app = await Fetch("/app.js");
        string lines = Between(app, "function renderLines(projectName, version, entries) {", "\n}\n");
        string unnumbered = lines[lines.IndexOf("const item = element(\"li\");", StringComparison.Ordinal)..];

        int position = unnumbered.IndexOf("item.appendChild(positionElement(position));", StringComparison.Ordinal);
        int firstAppend = unnumbered.IndexOf("item.appendChild(", StringComparison.Ordinal);
        int title = unnumbered.IndexOf("item.appendChild(titleElement(entry && entry.title));", StringComparison.Ordinal);
        Assert.True(position >= 0 && position == firstAppend && position < title);
    }

    [Fact]
    public async Task AppJs_PositionElementIsTextOnlyWithAHiddenWord()
    {
        string app = await Fetch("/app.js");
        string texts = Between(app, "const Texts = {", "};");
        string position = Between(app, "function positionElement(position) {", "\n}\n");

        Assert.Contains("  position: \"position \",\n", texts, StringComparison.Ordinal);
        int node = position.IndexOf("const node = element(\"span\", \"position\");", StringComparison.Ordinal);
        int hidden = position.IndexOf("node.appendChild(element(\"span\", \"visually-hidden\", Texts.position));",
            StringComparison.Ordinal);
        // append(string) adds a Text node; app.js never calls createTextNode (WebAppTests).
        int digit = position.IndexOf("node.append(String(position));", StringComparison.Ordinal);
        int back = position.IndexOf("return node;", StringComparison.Ordinal);
        Assert.True(node >= 0 && node < hidden && hidden < digit && digit < back);
        Assert.Equal(1, Count(position, "appendChild("));
        Assert.Equal(1, Count(position, "node.append("));
        Assert.DoesNotContain("innerHTML", position, StringComparison.Ordinal);
        Assert.DoesNotContain("#", position, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_WaitingRowsStartWithTheirOwnPosition()
    {
        string app = await Fetch("/app.js");
        string plan = Between(app, "function renderPlan(projectName, project) {", "\nfunction planSignature(");

        int filter = plan.IndexOf("asList(project.blocked).filter((entry) => asNumber(entry && entry.number) !== null);",
            StringComparison.Ordinal);
        int start = plan.IndexOf("let position = 0;", StringComparison.Ordinal);
        int loop = plan.IndexOf("for (const entry of blocked) {", StringComparison.Ordinal);
        int step = plan.IndexOf("position += 1;", StringComparison.Ordinal);
        int row = plan.IndexOf("const row = element(\"li\");", StringComparison.Ordinal);
        int position = plan.IndexOf("row.appendChild(positionElement(position));", StringComparison.Ordinal);
        int link = plan.IndexOf("row.appendChild(planItemLink(projectName, entry.number, entry.title));",
            StringComparison.Ordinal);
        Assert.True(filter >= 0 && filter < start && start < loop && loop < step && step < row);
        Assert.True(row < position && position < link);
        Assert.Equal(position, plan.IndexOf("row.appendChild(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppCss_PositionHasItsOwnRuleAndVisuallyHiddenClips()
    {
        string css = await Fetch("/app.css");
        string position = Between(css, "\n.position {", "\n}\n");
        string hidden = Between(css, "\n.visually-hidden {", "\n}\n");
        string number = Between(css, "\n.number {", "\n}\n");

        Assert.Contains("font-variant-numeric: tabular-nums;", position, StringComparison.Ordinal);
        Assert.Contains("min-width:", position, StringComparison.Ordinal);
        Assert.Contains("text-align: right;", position, StringComparison.Ordinal);
        Assert.Contains("color: var(--muted);", position, StringComparison.Ordinal);
        Assert.DoesNotContain("font-variant-numeric", number, StringComparison.Ordinal);
        Assert.DoesNotContain(".position,", css, StringComparison.Ordinal);
        Assert.DoesNotContain(",\n.position", css, StringComparison.Ordinal);
        Assert.Contains("position: absolute;", hidden, StringComparison.Ordinal);
        Assert.Contains("width: 1px;", hidden, StringComparison.Ordinal);
        Assert.Contains("height: 1px;", hidden, StringComparison.Ordinal);
        Assert.Contains("clip", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("display: none", hidden, StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count += 1;
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
