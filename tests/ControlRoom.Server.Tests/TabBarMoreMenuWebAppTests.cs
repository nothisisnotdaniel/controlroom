using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>The More button at the end of the tab bar and its menu with History and Health (#475), checked as text in
/// <c>index.html</c>, <c>app.js</c> and <c>room.css</c>.</summary>
public sealed partial class TabBarWebAppTests
{
    /// <summary>The More button and its menu.</summary>
    public sealed partial class MoreMenu
    {
        private const string MenuOpen = "<div class=\"tab-more-menu\" id=\"tab-more-menu\" hidden>";

        private static readonly string[] MenuSelectors =
        [
            ".tab-more {", ".tab-more[aria-current=\"page\"] {", ".tab-more:focus-visible {", ".tab-more-menu {",
            ".tab-more-menu[hidden] {", ".tab-more-menu a {", ".tab-more-menu a[aria-current=\"page\"] {",
            ".tab-more-menu a:focus-visible {",
        ];

        [Fact]
        public async Task IndexHtml_NavHasNoHistoryOrHealthLinkAndEndsWithTheMoreButton()
        {
            string nav = NavSlice(Normalize(await Fetch("/index.html")));

            foreach (string gone in new[] { "href=\"#history\"", "href=\"#doctor\"", "data-view=\"history\"", "data-view=\"doctor\"" })
            {
                Assert.DoesNotContain(gone, nav, StringComparison.Ordinal);
            }
            Assert.Equal(3, Regex.Matches(nav, "<a ").Count);
            Assert.Contains(
                "    " + MoreButton + "\n"
                + "      <svg class=\"tab-icon\" aria-hidden=\"true\" focusable=\"false\" viewBox=\"0 0 24 24\"><path d=\"",
                nav, StringComparison.Ordinal);
            string body = MoreBody(nav);
            Match svg = SvgElement().Match(body);
            Assert.True(svg.Success);
            // Paths only: the three dots.
            Assert.Single(Regex.Matches(svg.Value, "<path "));
            Assert.Equal(["svg", "path"], Regex.Matches(svg.Value, "<(?<tag>[a-z]+)[ >]").Select(match => match.Groups["tag"].Value));
            Assert.Equal(3, Regex.Matches(svg.Value, @"h\.01").Count);
            Assert.EndsWith("<span class=\"tab-label\">More</span>\n    ", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task IndexHtml_MenuFollowsTheNavHiddenWithHistoryThenHealthAsPlainLinks()
        {
            string html = Normalize(await Fetch("/index.html"));

            Assert.Single(Regex.Matches(html, "id=\"tab-more-menu\""));
            Assert.Contains("  </nav>\n  " + MenuOpen + "\n", html, StringComparison.Ordinal);
            int from = html.IndexOf(MenuOpen, StringComparison.Ordinal);
            int end = html.IndexOf("</div>", from, StringComparison.Ordinal);
            string menu = html[from..(end + "</div>".Length)];
            Assert.Equal(
                MenuOpen + "\n"
                + "    <a href=\"#history\" data-view=\"history\">History</a>\n"
                + "    <a href=\"#doctor\" data-view=\"doctor\">Health</a>\n"
                + "  </div>", menu);
            Assert.DoesNotContain("role=", menu, StringComparison.Ordinal);
            Assert.DoesNotContain("style=", menu, StringComparison.Ordinal);
            Assert.DoesNotContain("<img", menu, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(html, "href=\"#history\""));
            Assert.Single(Regex.Matches(html, "href=\"#doctor\""));
        }

        [Fact]
        public async Task AppJs_TheButtonTogglesTheMenuAndItsExpandedState()
        {
            string js = Normalize(await Fetch("/app.js"));

            Assert.Contains(
                "function openMoreMenu() {\n"
                + "  document.getElementById(\"tab-more-menu\").hidden = false;\n"
                + "  document.getElementById(\"tab-more\").setAttribute(\"aria-expanded\", \"true\");\n", js,
                StringComparison.Ordinal);
            Assert.Contains(
                "function closeMoreMenu() {\n"
                + "  document.getElementById(\"tab-more-menu\").hidden = true;\n"
                + "  document.getElementById(\"tab-more\").setAttribute(\"aria-expanded\", \"false\");\n", js,
                StringComparison.Ordinal);
            Assert.Contains("function moreMenuOpen() {\n  return !document.getElementById(\"tab-more-menu\").hidden;\n", js,
                StringComparison.Ordinal);
            Assert.Contains(
                "function toggleMoreMenu() {\n  if (moreMenuOpen()) {\n    closeMoreMenu();\n  } else {\n    openMoreMenu();\n  }\n",
                js, StringComparison.Ordinal);
            string setUp = FunctionBody(js, "function setUpMoreMenu() {");
            Assert.Contains("  const button = document.getElementById(\"tab-more\");\n", setUp, StringComparison.Ordinal);
            Assert.Contains("  button.addEventListener(\"click\", toggleMoreMenu);\n", setUp, StringComparison.Ordinal);
            // Set up once, at load time, before the first route.
            Assert.Contains("\nsetUpMoreMenu();\nwindow.addEventListener(\"hashchange\", route);\nroute();\n", js,
                StringComparison.Ordinal);
            Assert.Single(Regex.Matches(js, @"\bsetUpMoreMenu\(\);"));
        }

        [Fact]
        public async Task AppJs_TheMenuClosesOnALinkAnOutsideTapEscapeAndEveryShowView()
        {
            string js = Normalize(await Fetch("/app.js"));

            string setUp = FunctionBody(js, "function setUpMoreMenu() {");
            Assert.Contains(
                "  for (const link of menu.querySelectorAll(\"a\")) {\n    link.addEventListener(\"click\", closeMoreMenu);\n  }\n",
                setUp, StringComparison.Ordinal);
            Assert.Contains("  const menu = document.getElementById(\"tab-more-menu\");\n", setUp, StringComparison.Ordinal);
            Assert.Contains(
                "  document.addEventListener(\"pointerdown\", (event) => {\n"
                + "    if (moreMenuOpen() && !button.contains(event.target) && !menu.contains(event.target)) {\n"
                + "      closeMoreMenu();\n"
                + "    }\n"
                + "  });\n", setUp, StringComparison.Ordinal);
            Assert.Contains(
                "  document.addEventListener(\"keydown\", (event) => {\n"
                + "    if (event.key === \"Escape\" && moreMenuOpen()) {\n"
                + "      closeMoreMenu();\n"
                + "      button.focus();\n"
                + "    }\n"
                + "  });", setUp, StringComparison.Ordinal);
            // showView closes it first, so the token view and every route change leave it closed.
            string show = FunctionBody(js, "function showView(name) {");
            Assert.StartsWith("function showView(name) {\n  // ", show, StringComparison.Ordinal);
            int close = show.IndexOf("\n  closeMoreMenu();\n", StringComparison.Ordinal);
            Assert.True(close >= 0 && close < show.IndexOf("tokenViewShown()", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AppJs_ShowViewMarksTheMoreButtonAndTheMenuLinkOnlyForHistoryAndHealth()
        {
            string js = Normalize(await Fetch("/app.js"));

            Assert.Contains("const MoreViews = new Set([\"history\", \"doctor\"]);\n", js, StringComparison.Ordinal);
            Assert.Contains("const TabForView = { job: \"board\", story: \"backlogs\" };", js, StringComparison.Ordinal);
            string show = FunctionBody(js, "function showView(name) {");
            const string Marking =
                "  const more = MoreViews.has(current);\n"
                + "  const button = document.getElementById(\"tab-more\");\n"
                + "  if (more) {\n"
                + "    button.setAttribute(\"aria-current\", \"page\");\n"
                + "  } else {\n"
                + "    button.removeAttribute(\"aria-current\");\n"
                + "  }\n"
                + "  for (const link of document.querySelectorAll(\"#tab-more-menu a\")) {\n"
                + "    if (more && link.dataset.view === current) {\n"
                + "      link.setAttribute(\"aria-current\", \"page\");\n"
                + "    } else {\n"
                + "      link.removeAttribute(\"aria-current\");\n"
                + "    }\n"
                + "  }";
            Assert.EndsWith(Marking, show, StringComparison.Ordinal);
            // The tab links' own loop comes first and stays as it was; the nav holds no history or doctor link, so
            // on History and Health no tab link is marked.
            int tabs = show.IndexOf("  for (const link of document.querySelectorAll(\".tab-bar a\")) {\n", StringComparison.Ordinal);
            Assert.True(tabs >= 0 && tabs < show.IndexOf(Marking, StringComparison.Ordinal));
            Assert.Equal(3, Regex.Matches(show, Regex.Escape("setAttribute(\"aria-current\", \"page\")")).Count);
        }

        [Fact]
        public async Task AppJs_HashHealthOpensHealthAndIsReplacedByDoctorInPlace()
        {
            string js = Normalize(await Fetch("/app.js"));

            Assert.Contains("const HealthRoute = \"#health\";\n", js, StringComparison.Ordinal);
            string viewFor = FunctionBody(js, "function viewFor(hash) {");
            Assert.Contains("  if (hash === HistoryRoute) {\n    return \"history\";\n  }\n"
                            + "  if (hash === DoctorRoute) {\n    return \"doctor\";\n  }\n"
                            + "  if (hash === HealthRoute) {\n    return \"doctor\";\n  }\n", viewFor, StringComparison.Ordinal);
            string route = FunctionBody(js, "function route() {");
            Assert.Contains(
                "  if (location.hash === HealthRoute) {\n    history.replaceState(null, \"\", DoctorRoute);\n  }\n",
                route, StringComparison.Ordinal);
            Assert.DoesNotContain("pushState", js, StringComparison.Ordinal);
            Assert.DoesNotContain("location.hash = DoctorRoute", js, StringComparison.Ordinal);
            Assert.Contains("history: \"History\", doctor: \"Health\" };", js, StringComparison.Ordinal);
            Assert.Contains("  if (next === \"doctor\") {\n    showDoctor();\n    return;\n  }", route, StringComparison.Ordinal);
            Assert.Contains("  if (next === \"history\") {\n    showHistory();\n    return;\n  }", route, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RoomCss_TheMoreButtonLooksLikeATabWithoutButtonChrome()
        {
            string css = Normalize(await Fetch("/room.css"));
            string kit = Normalize(await Fetch("/app.css"));

            string button = Block(css, ".tab-more {");
            string link = Block(kit, ".tab-bar a {");
            foreach (string property in new[] { "display", "flex", "flex-direction", "align-items", "justify-content", "min-width", "max-width", "color" })
            {
                Assert.Equal(Declaration(link, property), Declaration(button, property));
            }
            Assert.Equal("var(--muted)", Declaration(button, "color"));
            Assert.Equal("0", Declaration(button, "border"));
            Assert.Equal("none", Declaration(button, "background"));
            Assert.Equal("var(--link)", Declaration(Block(css, ".tab-more[aria-current=\"page\"] {"), "color"));
            Assert.Equal("var(--link)", Declaration(Block(css, ".tab-more-menu a[aria-current=\"page\"] {"), "color"));
            Assert.Matches(new Regex(@"^\d+px solid var\(--link\)$"), Declaration(Block(css, ".tab-more:focus-visible {"), "outline"));
            Assert.Matches(new Regex(@"^\d+px solid var\(--link\)$"),
                Declaration(Block(css, ".tab-more-menu a:focus-visible {"), "outline"));
            foreach (string selector in new[] { ".tab-more {", ".tab-more-menu a {" })
            {
                Match px = Regex.Match(Declaration(Block(css, selector), "min-height"), @"^(?<px>\d+)px$");
                Assert.True(px.Success && int.Parse(px.Groups["px"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 44,
                    selector + " is under 44 px");
            }
        }

        [Fact]
        public async Task RoomCss_TheMenuIsFixedAboveTheBarInThemeColoursAndStaysHiddenWhenHidden()
        {
            string css = Normalize(await Fetch("/room.css"));
            string kit = Normalize(await Fetch("/app.css"));

            string menu = Block(css, ".tab-more-menu {");
            Assert.Equal("fixed", Declaration(menu, "position"));
            Assert.StartsWith("calc(var(--tab-bar-height) + env(safe-area-inset-bottom) ", Declaration(menu, "bottom"),
                StringComparison.Ordinal);
            Assert.True(ZIndex(menu) > ZIndex(Block(kit, ".tab-bar {")));
            Assert.Equal("var(--card)", Declaration(menu, "background"));
            Assert.Equal("1px solid var(--line)", Declaration(menu, "border"));
            Assert.Equal("none", Declaration(Block(css, ".tab-more-menu[hidden] {"), "display"));
            // The hidden rule comes after the menu's own display, so the menu's display never wins over hidden.
            Assert.True(css.IndexOf("\n.tab-more-menu[hidden] {", StringComparison.Ordinal)
                        > css.IndexOf("\n.tab-more-menu {", StringComparison.Ordinal));

            string[] allowed = ["--card", "--line", "--link", "--muted", "--fg", "--tab-bar-height"];
            foreach (string selector in MenuSelectors)
            {
                string block = Block(css, selector);
                Assert.DoesNotMatch(new Regex("#[0-9a-fA-F]{3,8}\\b|rgb|hsl"), block);
                foreach (Match variable in CssVariable().Matches(block))
                {
                    Assert.Contains(variable.Groups["name"].Value, allowed);
                }
            }
        }

        [GeneratedRegex(@"var\((?<name>--[a-z-]+)\)")]
        private static partial Regex CssVariable();
    }
}
