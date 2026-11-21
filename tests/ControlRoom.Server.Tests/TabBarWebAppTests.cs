using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the bottom tab bar in <c>index.html</c>, <c>app.css</c>, <c>app.js</c> and <c>token.js</c> as
/// text (there is no JS test runtime). The layout itself (all six tabs in one row at 375 px, the last item clear of
/// the bar, light and dark mode, the home indicator inset in the installed web app) is checked by hand.</summary>
public sealed partial class TabBarWebAppTests
{
    private const string NavOpen = "<nav class=\"nav tab-bar\" aria-label=\"Sections\">";

    private static readonly (string Href, string View, string Label)[] Tabs =
    [
        ("#jobs", "board", "Jobs"), ("#backlogs", "backlogs", "Backlogs"), ("#inbox", "inbox", "Inbox"),
        ("#tasks", "tasks", "Tasks"),
        ("#history", "history", "History"), ("#doctor", "doctor", "Health"),
    ];

    [Fact]
    public async Task IndexHtml_HasOneTabBarAfterTheLiveNoticeOutsideTheHeaderWithTheSixLinksInOrder()
    {
        string html = Normalize(await Fetch("/index.html"));

        Assert.Single(Regex.Matches(html, Regex.Escape(NavOpen)));
        Assert.Single(Regex.Matches(html, "<nav[ >]"));
        int header = html.IndexOf("<header class=\"top\" id=\"top\">", StringComparison.Ordinal);
        int headerEnd = html.IndexOf("</header>", StringComparison.Ordinal);
        int nav = html.IndexOf(NavOpen, StringComparison.Ordinal);
        Assert.True(header >= 0 && headerEnd > header && nav > headerEnd);
        Assert.Contains("<h1 id=\"top-title\">", html[header..headerEnd], StringComparison.Ordinal);
        string afterHeader = html[(headerEnd + "</header>".Length)..].TrimStart();
        Assert.StartsWith("<p class=\"live-notice\" id=\"live-notice\"", afterHeader, StringComparison.Ordinal);
        int notice = html.IndexOf("<p class=\"live-notice\"", StringComparison.Ordinal);
        Assert.True(notice < nav);

        MatchCollection links = TabLink().Matches(NavSlice(html));
        Assert.Equal(Tabs.Length, links.Count);
        for (int i = 0; i < Tabs.Length; i++)
        {
            Assert.Equal(Tabs[i].Href, links[i].Groups["href"].Value);
            Assert.Equal(Tabs[i].View, links[i].Groups["view"].Value);
        }
    }

    [Fact]
    public async Task IndexHtml_EveryTabHasOneInlineAriaHiddenIconAndOneLabelAndNoExternalFile()
    {
        string nav = NavSlice(Normalize(await Fetch("/index.html")));

        string[] bodies = LinkBodies(nav);
        Assert.Equal(Tabs.Length, bodies.Length);
        for (int i = 0; i < Tabs.Length; i++)
        {
            Assert.Single(Regex.Matches(bodies[i], "<svg "));
            Assert.Single(Regex.Matches(bodies[i],
                Regex.Escape("<svg class=\"tab-icon\" aria-hidden=\"true\" focusable=\"false\"")));
            Assert.Single(Regex.Matches(bodies[i], "class=\"tab-label\""));
            Assert.Contains("<span class=\"tab-label\">" + Tabs[i].Label + "</span>", bodies[i], StringComparison.Ordinal);
        }
        Assert.DoesNotContain("<img", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("<image", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("style=", nav, StringComparison.Ordinal);
        foreach (Match svg in SvgElement().Matches(nav))
        {
            Assert.DoesNotContain("href=", svg.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("url(", svg.Value, StringComparison.Ordinal);
        }
        Assert.Equal(Tabs.Length, SvgElement().Matches(nav).Count);
    }

    [Fact]
    public async Task IndexHtml_InboxAndTasksTabsKeepTheirCountSpans()
    {
        string[] bodies = LinkBodies(NavSlice(Normalize(await Fetch("/index.html"))));

        Assert.Contains("<span class=\"nav-count\" id=\"inbox-count\" hidden></span>", bodies[2], StringComparison.Ordinal);
        Assert.Contains("<span class=\"nav-count\" id=\"tasks-count\" hidden></span>", bodies[3], StringComparison.Ordinal);
        for (int i = 0; i < bodies.Length; i++)
        {
            Assert.Equal(i is 2 or 3 ? 1 : 0, Regex.Matches(bodies[i], "nav-count").Count);
        }
    }

    [Fact]
    public async Task AppCss_PlacesTheCountAsABadgeOnTheIconAndStylesItInTheCurrentTab()
    {
        string css = Normalize(await Fetch("/app.css"));

        Assert.Contains("  position: relative;\n", Block(css, ".tab-bar a {"), StringComparison.Ordinal);
        string badge = Block(css, ".nav-count {");
        Assert.Contains("  position: absolute;\n", badge, StringComparison.Ordinal);
        Assert.Contains("  top: ", badge, StringComparison.Ordinal);
        Assert.Contains("  left: ", badge, StringComparison.Ordinal);
        string current = Block(css, ".tab-bar a[aria-current=\"page\"] .nav-count {");
        Assert.Contains("  background: var(--", current, StringComparison.Ordinal);
        Assert.Contains("  color: var(--", current, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtmlAndAppCss_TabsAreKeyboardReachableLinksWithAVisibleFocusOutline()
    {
        string nav = NavSlice(Normalize(await Fetch("/index.html")));
        string css = Normalize(await Fetch("/app.css"));

        Assert.DoesNotContain("tabindex", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=", nav[NavOpen.Length..], StringComparison.Ordinal);
        Assert.Equal(Tabs.Length, Regex.Matches(nav, "<a href=\"#").Count);
        string focus = Block(css, ".tab-bar a:focus-visible {");
        Assert.Matches(new Regex(@"  outline: \d+px solid var\(--[a-z-]+\);\n"), focus);
    }

    [Fact]
    public async Task AppCss_TabBarIsFixedToTheBottomAboveTheEditNoticeWithThemeColours()
    {
        string css = Normalize(await Fetch("/app.css"));

        string bar = Block(css, ".tab-bar {");
        foreach (string declaration in new[]
                 {
                     "  position: fixed;\n", "  left: 0;\n", "  right: 0;\n", "  bottom: 0;\n",
                     "  background: var(--card);\n", "  border-top: 1px solid var(--line);\n",
                 })
        {
            Assert.Contains(declaration, bar, StringComparison.Ordinal);
        }
        Assert.Matches(new Regex(@"  padding: [^;]*env\(safe-area-inset-bottom\)"), bar);
        Assert.DoesNotMatch(new Regex("#[0-9a-fA-F]{3,8}|rgb"), bar);
        int barZ = ZIndex(bar);
        Assert.True(barZ > ZIndex(Block(css, ".edit-notice {")));
        Assert.True(barZ > ZIndex(Block(css, ".story-bar {")));
    }

    [Fact]
    public async Task AppCss_AllSixTabsFitInOneRowWithTinyLabelsUnderTheIcons()
    {
        string css = Normalize(await Fetch("/app.css"));

        string bar = Block(css, ".tab-bar {");
        Assert.Contains("  display: flex;\n", bar, StringComparison.Ordinal);
        Assert.Contains("  flex-wrap: nowrap;\n", bar, StringComparison.Ordinal);
        string link = Block(css, ".tab-bar a {");
        Assert.Contains("  flex: 1 1 0;\n", link, StringComparison.Ordinal);
        Assert.Contains("  min-width: 0;\n", link, StringComparison.Ordinal);
        Assert.Contains("  flex-direction: column;\n", link, StringComparison.Ordinal);
        string label = Block(css, ".tab-label {");
        Assert.Contains("  white-space: nowrap;\n", label, StringComparison.Ordinal);
        Match size = Regex.Match(label, @"  font-size: (?<value>[0-9.]+)(?<unit>rem|px);\n");
        Assert.True(size.Success);
        double px = double.Parse(size.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture)
                    * (size.Groups["unit"].Value == "rem" ? 16 : 1);
        Assert.True(px <= 11, "label font size " + px + "px");

        foreach (Match block in TabBarBlock().Matches(css))
        {
            Assert.DoesNotContain("flex-wrap: wrap", block.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("overflow-x", block.Value, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("two rows of four", css, StringComparison.Ordinal);
        Assert.DoesNotContain("(100% - 12px) / 4", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".nav {", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".nav a", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_MarksTheCurrentTabInADifferentColour()
    {
        string css = Normalize(await Fetch("/app.css"));

        string normal = Declaration(Block(css, ".tab-bar a {"), "color");
        string current = Declaration(Block(css, ".tab-bar a[aria-current=\"page\"] {"), "color");
        Assert.StartsWith("var(--", normal, StringComparison.Ordinal);
        Assert.StartsWith("var(--", current, StringComparison.Ordinal);
        Assert.NotEqual(normal, current);
    }

    [Fact]
    public async Task AppJs_ShowsTheBarOnEveryViewButTheTokenViewAndMarksOneTab()
    {
        string js = Normalize(await Fetch("/app.js"));

        Assert.Contains("const TabForView = { job: \"board\", story: \"backlogs\" };", js, StringComparison.Ordinal);
        string show = FunctionBody(js, "function showView(name) {");
        Assert.Contains("  const blocked = tokenViewShown();\n", show, StringComparison.Ordinal);
        Assert.Contains("  document.querySelector(\".tab-bar\").hidden = blocked;\n", show, StringComparison.Ordinal);
        Assert.DoesNotContain("top.hidden;", show, StringComparison.Ordinal);
        Assert.Contains("  const current = TabForView[name] || name;\n", show, StringComparison.Ordinal);
        Assert.Contains(
            "  for (const link of document.querySelectorAll(\".tab-bar a\")) {\n"
            + "    if (link.dataset.view === current) {\n"
            + "      link.setAttribute(\"aria-current\", \"page\");\n"
            + "    } else {\n"
            + "      link.removeAttribute(\"aria-current\");\n"
            + "    }\n"
            + "  }", show, StringComparison.Ordinal);
        // The marking runs for every view, not only those with a header.
        int marking = show.IndexOf("for (const link", StringComparison.Ordinal);
        int headerIf = show.IndexOf("if (!top.hidden) {", StringComparison.Ordinal);
        int headerEnd = show.IndexOf("\n  }\n", headerIf, StringComparison.Ordinal);
        Assert.True(headerIf >= 0 && headerEnd > headerIf && marking > headerEnd);
        Assert.Single(Regex.Matches(js, Regex.Escape("setAttribute(\"aria-current\", \"page\")")));
    }

    [Fact]
    public async Task TokenJs_HidesTheTabBarWithTheHeader()
    {
        string js = Normalize(await Fetch("/token.js"));

        string show = FunctionBody(js, "function showTokenView(refused) {");
        Assert.Contains(
            "  document.getElementById(\"top\").hidden = true;\n  document.querySelector(\".tab-bar\").hidden = true;\n",
            show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_BodyAndEditNoticeKeepContentClearOfTheBar()
    {
        string css = Normalize(await Fetch("/app.css"));

        Assert.Single(Regex.Matches(css, @"--tab-bar-height:"));
        Assert.Matches(new Regex(@"  --tab-bar-height: \d+px;\n"), Block(css, ":root {"));
        const string Clear = "calc(var(--tab-bar-height) + env(safe-area-inset-bottom) + 12px";
        Assert.Matches(new Regex(@"  padding: 0 [^;]*" + Regex.Escape(Clear) + @"\)"), Block(css, "body {"));
        Assert.Contains("  padding-bottom: " + Clear + " + 72px);\n",
            Block(css, "body:has(#edit-notice:not([hidden])) {"), StringComparison.Ordinal);
        Assert.Contains("  bottom: " + Clear + ");\n", Block(css, ".edit-notice {"), StringComparison.Ordinal);
        Assert.Contains("  padding-bottom: max(24px, env(safe-area-inset-bottom));\n",
            Block(css, "body:has(#token:not([hidden])) {"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_TapTargetsAreAtLeast44PxAndTheBarIsAtLeastThatTall()
    {
        string css = Normalize(await Fetch("/app.css"));

        Match minHeight = Regex.Match(Block(css, ".tab-bar a {"), @"  min-height: (?<px>\d+)px;\n");
        Assert.True(minHeight.Success && int.Parse(minHeight.Groups["px"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 44);
        Match height = Regex.Match(Block(css, ":root {"), @"  --tab-bar-height: (?<px>\d+)px;\n");
        Assert.True(height.Success && int.Parse(height.Groups["px"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 44);
        Assert.Contains("  min-height: var(--tab-bar-height);\n", Block(css, ".tab-bar {"), StringComparison.Ordinal);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string NavSlice(string html)
    {
        int from = html.IndexOf(NavOpen, StringComparison.Ordinal);
        Assert.True(from >= 0, "tab bar not found");
        int end = html.IndexOf("</nav>", from, StringComparison.Ordinal);
        Assert.True(end > from);
        return html[from..end];
    }

    private static string[] LinkBodies(string nav) =>
        LinkBody().Matches(nav).Select(match => match.Groups["body"].Value).ToArray();

    // The rule block that starts exactly at a line with this selector, up to its closing brace.
    private static string Block(string css, string selector)
    {
        int from = css.StartsWith(selector, StringComparison.Ordinal)
            ? 0
            : css.IndexOf("\n" + selector, StringComparison.Ordinal);
        Assert.True(from >= 0, selector + " not found");
        int end = css.IndexOf("\n}", from + 1, StringComparison.Ordinal);
        Assert.True(end > from);
        return css[from..(end + 2)];
    }

    private static string FunctionBody(string js, string start)
    {
        int from = js.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " not found");
        int end = js.IndexOf("\n}\n", from, StringComparison.Ordinal);
        Assert.True(end > from);
        return js[from..end];
    }

    private static int ZIndex(string block)
    {
        Match match = Regex.Match(block, @"  z-index: (?<value>\d+);\n");
        Assert.True(match.Success, "no z-index in " + block);
        return int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Declaration(string block, string property)
    {
        Match match = Regex.Match(block, "\n  " + Regex.Escape(property) + ": (?<value>[^;]+);\n");
        Assert.True(match.Success, property + " not found");
        return match.Groups["value"].Value;
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex("<a href=\"(?<href>#[a-z]+)\" data-view=\"(?<view>[a-z]+)\">")]
    private static partial Regex TabLink();

    [GeneratedRegex("<a href=\"#[a-z]+\" data-view=\"[a-z]+\">(?<body>.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex LinkBody();

    [GeneratedRegex("<svg[^>]*>.*?</svg>", RegexOptions.Singleline)]
    private static partial Regex SvgElement();

    // Every rule block whose selector mentions the tab bar, in or out of a media query.
    [GeneratedRegex(@"[^{}\n]*\.tab-bar[^{}\n]*\{[^}]*\}")]
    private static partial Regex TabBarBlock();
}
