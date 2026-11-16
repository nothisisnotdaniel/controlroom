using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>The Notifications section (<c>index.html</c>, <c>push.js</c>, <c>app.css</c>): fixed texts per state,
/// the permission prompt only from the "Turn on" tap, the switch and the buttons. The iPhone itself is checked by
/// hand.</summary>
public sealed partial class PushWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheSectionInTheBoardViewAndPushJsBeforeAppJs()
    {
        string html = await Fetch("/index.html");

        int board = html.IndexOf("<main id=\"board\" hidden>", StringComparison.Ordinal);
        int section = html.IndexOf("<section class=\"push\" id=\"push\" aria-labelledby=\"push-title\">", StringComparison.Ordinal);
        int boardEnd = html.IndexOf("</main>", board, StringComparison.Ordinal);
        Assert.True(board >= 0 && board < section && section < boardEnd);
        Assert.Contains("<h2 class=\"push-title\" id=\"push-title\">Notifications</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"push-actions\" id=\"push-actions\"></div>", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"push-note\" id=\"push-note\" role=\"status\" aria-live=\"polite\" hidden></p>", html,
            StringComparison.Ordinal);
        int boardJs = html.IndexOf("<script src=\"/board.js\" defer></script>", StringComparison.Ordinal);
        int pushJs = html.IndexOf("<script src=\"/push.js\" defer></script>", StringComparison.Ordinal);
        int appJs = html.IndexOf("<script src=\"/app.js\" defer></script>", StringComparison.Ordinal);
        Assert.True(boardJs >= 0 && boardJs < pushJs && pushJs < appJs);
    }

    [Fact]
    public async Task PushJs_HasAFixedTextForEveryState()
    {
        string js = await Fetch("/push.js");

        Assert.Contains("unsupported: \"This browser can't show notifications.\"", js, StringComparison.Ordinal);
        Assert.Contains(
            "homeScreen: \"To get notifications on iPhone, add Control Room to the Home Screen and open it from there.\"",
            js, StringComparison.Ordinal);
        Assert.Contains("off: \"Get a notification when a job needs you or new items wait for approval.\"", js,
            StringComparison.Ordinal);
        Assert.Contains("on: \"Notifications are on for this device.\"", js, StringComparison.Ordinal);
        Assert.Contains("denied: \"Notifications are blocked for Control Room.", js, StringComparison.Ordinal);
        Assert.Contains("turnOn: \"Turn on\"", js, StringComparison.Ordinal);
        Assert.Contains("turnOff: \"Turn off\"", js, StringComparison.Ordinal);
        Assert.Contains("sendTest: \"Send test\"", js, StringComparison.Ordinal);
        Assert.Contains("notifyDone: \"Notify when a job is done\"", js, StringComparison.Ordinal);
        Assert.Contains("failed: \"Can't change notifications. Try again.\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PushJs_DetectsTheStatesInOrder()
    {
        string js = await Fetch("/push.js");

        Assert.Contains("if (ios && !standalone) {\n    return \"homeScreen\";", js, StringComparison.Ordinal);
        Assert.Contains("window.matchMedia(\"(display-mode: standalone)\").matches || navigator.standalone === true",
            js, StringComparison.Ordinal);
        Assert.Contains("!(\"PushManager\" in window)", js, StringComparison.Ordinal);
        Assert.Contains("if (Notification.permission === \"denied\") {", js, StringComparison.Ordinal);
        Assert.Contains("showPush(PushTexts.on, [", js, StringComparison.Ordinal);
        Assert.Contains("doneSwitch(info.notifyDone === true),", js, StringComparison.Ordinal);
        Assert.Contains("pushButton(PushTexts.sendTest, sendTestPush, false),", js, StringComparison.Ordinal);
        Assert.Contains("pushButton(PushTexts.turnOff, turnOffPush, false),", js, StringComparison.Ordinal);
        Assert.Contains("showPush(PushTexts.off, [pushButton(PushTexts.turnOn, turnOnPush, true)]);", js,
            StringComparison.Ordinal);
        Assert.Contains("input.setAttribute(\"role\", \"switch\");", js, StringComparison.Ordinal);
        int support = js.IndexOf("const support = pushSupport();", StringComparison.Ordinal);
        int denied = js.IndexOf("if (Notification.permission === \"denied\") {", StringComparison.Ordinal);
        int on = js.IndexOf("showPush(PushTexts.on, [", StringComparison.Ordinal);
        Assert.True(support >= 0 && support < denied && denied < on);
    }

    [Fact]
    public async Task PushJs_AsksForPermissionOnlyInTheTurnOnTapThenSubscribesWithTheServerKey()
    {
        string js = await Fetch("/push.js");

        Assert.Single(RequestPermission().Matches(js));
        int handler = js.IndexOf("function turnOnPush() {", StringComparison.Ordinal);
        int next = js.IndexOf("\nfunction ", handler + 1, StringComparison.Ordinal);
        int ask = js.IndexOf("Notification.requestPermission()", StringComparison.Ordinal);
        Assert.True(handler >= 0 && handler < ask && ask < next);
        // The prompt is the first call, before any await, so it is still part of the tap.
        string body = js[handler..ask];
        Assert.DoesNotContain("await", body, StringComparison.Ordinal);
        string turnOn = js[handler..next];
        Assert.Contains("userVisibleOnly: true,", turnOn, StringComparison.Ordinal);
        Assert.Contains("applicationServerKey: base64UrlBytes(info.publicKey),", turnOn, StringComparison.Ordinal);
        Assert.Contains("await pushJson(\"/api/push/subscribe\", subscription.toJSON());", turnOn, StringComparison.Ordinal);
        // turnOnPush is only ever used as the click handler of the Turn on button.
        Assert.Equal(2, TurnOnName().Matches(js).Count);
        Assert.Contains("pushButton(PushTexts.turnOn, turnOnPush, true)", js, StringComparison.Ordinal);
        Assert.Contains("button.addEventListener(\"click\", onClick);", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PushJs_UsesAuthFetchForTheApiAndTheOtherRoutes()
    {
        string js = await Fetch("/push.js");

        Assert.Contains("authFetch(\"/api/push\", { cache: \"no-store\" })", js, StringComparison.Ordinal);
        Assert.Contains("pushJson(\"/api/push/unsubscribe\", { endpoint: endpoint })", js, StringComparison.Ordinal);
        Assert.Contains("pushJson(\"/api/push/test\")", js, StringComparison.Ordinal);
        Assert.Contains("pushJson(\"/api/push/settings\", { notifyDone: on })", js, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(\"/api", js.Replace("authFetch(\"/api", "", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_ShowsTheSectionWithTheBoard()
    {
        string js = await Fetch("/board.js");

        int show = js.IndexOf("function showBoard() {", StringComparison.Ordinal);
        int call = js.IndexOf("showPushSection();", StringComparison.Ordinal);
        Assert.True(show >= 0 && show < call);
    }

    [Fact]
    public async Task AppCss_StylesTheSectionWithThemeColours()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".push {", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--card);", css, StringComparison.Ordinal);
        Assert.Contains(".push-switch {", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", css, StringComparison.Ordinal);
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex("requestPermission")]
    private static partial Regex RequestPermission();

    [GeneratedRegex("\\bturnOnPush\\b")]
    private static partial Regex TurnOnName();
}
