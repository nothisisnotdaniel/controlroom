using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the served <c>token.js</c>, <c>index.html</c>, <c>app.js</c> and <c>board.js</c> text for the
/// access token (no JS engine). The token view in light and dark mode at 375 px is checked by hand.</summary>
public sealed partial class TokenWebAppTests
{
    [Fact]
    public async Task TokenJs_StoresTheTokenUnderItsKeyAndSendsItOnlyAsABearerHeader()
    {
        string js = await Fetch("/token.js");

        Assert.Contains("const TokenKey = \"controlroom-token\";", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem(TokenKey)", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem(TokenKey, token)", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.removeItem(TokenKey)", js, StringComparison.Ordinal);
        Assert.Contains("headers.set(\"Authorization\", \"Bearer \" + token);", js, StringComparison.Ordinal);
        Assert.Contains("const response = await fetch(path, Object.assign({}, options, { headers: headers }));", js,
            StringComparison.Ordinal);
        Assert.Single(FetchCall().Matches(js));
        Assert.DoesNotContain("token=", js, StringComparison.Ordinal);
        Assert.DoesNotContain("?", UrlBuilding(js), StringComparison.Ordinal);
        Assert.DoesNotContain("console", js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", js, StringComparison.Ordinal);
        Assert.DoesNotContain("document.cookie", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppAndBoard_UseAuthFetchForEveryApiCall()
    {
        string app = await Fetch("/app.js");
        string board = await Fetch("/board.js");

        foreach (string js in new[] { app, board })
        {
            Assert.DoesNotContain("fetch(\"/api/", js.Replace("authFetch(\"/api/", "", StringComparison.Ordinal),
                StringComparison.Ordinal);
            Assert.DoesNotMatch(BareFetch(), js);
            Assert.DoesNotContain("localStorage.getItem(\"controlroom-token\")", js, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization", js, StringComparison.Ordinal);
        }

        Assert.Contains("await authFetch(\"/api/backlogs\", { cache: \"no-store\" })", app, StringComparison.Ordinal);
        Assert.Contains("await authFetch(\n    \"/api/story?project=\"", app, StringComparison.Ordinal);
        Assert.Contains("await authFetch(\"/api/board\", { cache: \"no-store\" })", board, StringComparison.Ordinal);
        Assert.Contains("await authFetch(\"/api/job?id=\" + encodeURIComponent(id), { cache: \"no-store\" })", board,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsTokenJsFirstAndHasAHiddenLabelledTokenForm()
    {
        string html = await Fetch("/index.html");

        int token = html.IndexOf("<script src=\"/token.js\" defer></script>", StringComparison.Ordinal);
        int board = html.IndexOf("<script src=\"/board.js\" defer></script>", StringComparison.Ordinal);
        int app = html.IndexOf("<script src=\"/app.js\" defer></script>", StringComparison.Ordinal);
        Assert.True(token >= 0 && token < board && board < app);
        Assert.Contains("<main id=\"token\" hidden>", html, StringComparison.Ordinal);
        Assert.Contains("<form class=\"token-form\" id=\"token-form\" novalidate>", html, StringComparison.Ordinal);
        Assert.Contains("<label class=\"token-label\" for=\"token-input\">Access token</label>", html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<input class=\"token-input\" id=\"token-input\" name=\"token\" type=\"password\" autocomplete=\"off\"",
            html, StringComparison.Ordinal);
        Assert.Contains("<button class=\"token-save\" type=\"submit\">Save</button>", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"error\" id=\"token-refused\" role=\"alert\" hidden></p>", html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("action=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenJs_ShowsTheTokenViewWithoutATokenAndOnRefusal()
    {
        string token = await Fetch("/token.js");
        string app = await Fetch("/app.js");

        Assert.Contains("refused: \"The token was refused. Enter the current token.\"", token,
            StringComparison.Ordinal);
        Assert.Contains("note.textContent = TokenTexts.refused;", token, StringComparison.Ordinal);
        int missing = token.IndexOf("if (token === \"\") {\n    showTokenView(false);", StringComparison.Ordinal);
        Assert.True(missing >= 0);
        int refusedCheck = token.IndexOf("if (response.status === 401) {", StringComparison.Ordinal);
        int forget = token.IndexOf("forgetToken();\n    showTokenView(true);", StringComparison.Ordinal);
        Assert.True(refusedCheck >= 0 && refusedCheck < forget);
        Assert.Contains("main.hidden = main.id !== \"token\";", token, StringComparison.Ordinal);
        Assert.Contains("document.getElementById(\"top\").hidden = true;", token, StringComparison.Ordinal);

        Assert.Contains("if (!hasToken()) {\n  showTokenView(false);\n}", app, StringComparison.Ordinal);
        Assert.Contains("const blocked = tokenViewShown();", app, StringComparison.Ordinal);
        Assert.Contains("document.getElementById(view).hidden = blocked || view !== name;", app,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenJs_SaveTrimsStoresAndReloadsTheCurrentView()
    {
        string token = await Fetch("/token.js");
        string app = await Fetch("/app.js");

        Assert.Contains("document.getElementById(\"token-form\").addEventListener(\"submit\", saveToken);", token,
            StringComparison.Ordinal);
        int prevent = token.IndexOf("event.preventDefault();", StringComparison.Ordinal);
        int trim = token.IndexOf("const token = input.value.trim();", StringComparison.Ordinal);
        int store = token.IndexOf("storeToken(token);", StringComparison.Ordinal);
        int hide = token.IndexOf("hideTokenView();\n  if (tokenSaved) {", StringComparison.Ordinal);
        Assert.True(prevent >= 0 && prevent < trim && trim < store && store < hide);
        Assert.Contains("onTokenSaved(() => {\n  route();\n  load();\n  loadInboxCount();\n  loadTasksCount();\n});", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Board_StopsPollingWhileTheTokenViewIsShown()
    {
        string board = await Fetch("/board.js");

        Assert.Contains(
            "if (document.visibilityState !== \"visible\" || tokenViewShown() || !boardViewsVisible() || boardPolling) {",
            board, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheTokenFormThumbSized()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".token-form {", css, StringComparison.Ordinal);
        Assert.Contains(".token-save {\n  min-height: 48px;", css, StringComparison.Ordinal);
        Assert.Contains(".token-input {\n  width: 100%;\n  min-height: 44px;", css, StringComparison.Ordinal);
    }

    private static string UrlBuilding(string js)
    {
        return string.Concat(FetchCall().Matches(js).Select(match => match.Value));
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex("(?<![A-Za-z])fetch\\([^)]*\\)")]
    private static partial Regex FetchCall();

    [GeneratedRegex("(?<![A-Za-z])fetch\\(")]
    private static partial Regex BareFetch();
}
