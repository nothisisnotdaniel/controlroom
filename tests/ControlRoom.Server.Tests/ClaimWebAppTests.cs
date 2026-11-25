using System.Globalization;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the claim badge in <c>app.js</c>, its colours in <c>app.css</c> and the <c>claims</c> fingerprint in
/// <c>live.js</c> as text (there is no JS test runtime). The look on the iPhone is checked by hand.</summary>
public sealed class ClaimWebAppTests
{
    [Fact]
    public async Task AppJs_TheBadgeTextsAreFixed()
    {
        string app = await Fetch("/app.js");
        string texts = Between(app, "const Texts = {", "\n};\n");

        Assert.Contains("  claimOn: \"In progress on \",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  claimAnother: \"another machine\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  claimSince: \" since \",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  claimChecked: \" · checked \",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  claimMinAgo: \" min ago\",", texts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_BadgeIsTextOnlyWithFallbacksAndTheStaleAge()
    {
        string app = await Fetch("/app.js");
        string badge = Between(app, "function claimBadge(projectName, number) {", "\n}\n");

        Assert.Contains("badge.className = \"claim-badge\";", badge, StringComparison.Ordinal);
        Assert.Contains("badge.textContent = text;", badge, StringComparison.Ordinal);
        Assert.Contains("Texts.claimOn + (machine !== \"\" ? machine : Texts.claimAnother)", badge, StringComparison.Ordinal);
        int valid = badge.IndexOf("if (claimed !== null && !Number.isNaN(claimed.getTime())) {", StringComparison.Ordinal);
        int since = badge.IndexOf("text += Texts.claimSince + claimed.toLocaleString(", StringComparison.Ordinal);
        Assert.True(valid >= 0 && valid < since);
        int stale = badge.IndexOf("if (claims.stale && claims.checked !== null) {", StringComparison.Ordinal);
        int age = badge.IndexOf("text += Texts.claimChecked + Math.max(0, Math.floor((Date.now() - claims.checked) / 60000)) + Texts.claimMinAgo;", StringComparison.Ordinal);
        Assert.True(stale > since && age > stale);
        Assert.DoesNotContain("innerHTML", app, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_BadgeComesAfterTheCategoryChipOnNumberedRows()
    {
        string app = await Fetch("/app.js");
        string line = Between(app, "function renderNumberedLine(projectName, version, entry, number, position) {", "\n}\n");

        int chip = line.IndexOf("head.appendChild(chip);", StringComparison.Ordinal);
        int claim = line.IndexOf("const claim = claimBadge(projectName, number);", StringComparison.Ordinal);
        int append = line.IndexOf("head.appendChild(claim);", StringComparison.Ordinal);
        int waiting = line.IndexOf("Texts.waiting", StringComparison.Ordinal);
        Assert.True(chip >= 0 && chip < claim && claim < append && append < waiting);
        Assert.Contains("if (claim !== null) {", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ClaimsAreInTheSectionSignatureAndReadPerProject()
    {
        string app = await Fetch("/app.js");
        string signature = Between(app, "function planSignature(project) {", "\n}\n");
        string render = Between(app, "function renderProject(project) {", "\n}\n");

        Assert.Contains("asList(project.claims)", signature, StringComparison.Ordinal);
        Assert.Contains("project.claimsStale === true", signature, StringComparison.Ordinal);
        Assert.Contains("stop, claims]);", signature, StringComparison.Ordinal);
        Assert.Contains("projectClaims.set(name, readClaims(project));", render, StringComparison.Ordinal);
        string read = Between(app, "function readClaims(project) {", "\n}\n");
        Assert.Contains("const item = asNumber(claim && claim.item);", read, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_AChangedClaimsFingerprintRefreshesTheSection()
    {
        string live = await Fetch("/live.js");

        Assert.Contains("claims: value.claims || \"\" });", live, StringComparison.Ordinal);
        Assert.Contains(
            "    if (!backlogChanged && before.claims !== after.claims) {\n      staleBacklogs.add(name);\n    }",
            live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_BadgeHasAReadableColourPairInLightAndDarkAndWraps()
    {
        string css = await Fetch("/app.css");
        string badge = Between(css, "\n.claim-badge {", "\n}\n");

        Assert.Contains("  background: var(--claim-bg);\n  color: var(--claim-fg);", badge, StringComparison.Ordinal);
        Assert.Contains("  white-space: normal;", badge, StringComparison.Ordinal);
        Assert.Contains("  max-width: 100%;", badge, StringComparison.Ordinal);
        string light = Between(css, ":root {", "\n}\n");
        string dark = Between(Between(css, "@media (prefers-color-scheme: dark) {", "\n}\n"), ":root {", "\n  }");
        foreach (string scheme in new[] { light, dark })
        {
            double ratio = Contrast(Variable(scheme, "--claim-bg"), Variable(scheme, "--claim-fg"));
            Assert.True(ratio >= 4.5, "claim contrast " + ratio.ToString("F2", CultureInfo.InvariantCulture));
        }
    }

    private static double Luminance(string hex)
    {
        static double Channel(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        double r = int.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        double g = int.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        double b = int.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static string Variable(string block, string name)
    {
        Match match = Regex.Match(block, "\n\\s+" + Regex.Escape(name) + ": (?<value>#[0-9a-fA-F]{6});\n");
        Assert.True(match.Success, name + " not found");
        return match.Groups["value"].Value;
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " not found");
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end + " not found after " + start);
        return text[from..to];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        string text = await client.GetStringAsync(path);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
