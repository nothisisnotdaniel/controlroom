using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace ControlRoom.Server.Tests;

/// <summary>The web files split between ControlRoom's and RoomKit's <c>wwwroot</c> (386): every path develop
/// served is still served at its URL with the same bytes and headers, in Development and Production, and the only
/// changed files differ by the lines moved to <c>room.js</c>.</summary>
public sealed class WebAssetSplitTests
{
    private static readonly string[] AppFiles =
    [
        "index.html", "manifest.webmanifest", "icons/apple-touch-icon.png", "icons/icon-192.png", "icons/icon-512.png",
        "app.js", "board.js", "act.js", "status.js", "releases.js", "room.js",
    ];

    private static readonly string[] KitFiles =
    [
        "app.css", "sw.js", "token.js", "push.js", "edit.js", "counts.js", "search.js", "capture.js", "history.js",
        "doctor.js", "live.js",
    ];

    // Each moved line, by the RoomKit file it came from; it is in room.js verbatim.
    private static readonly Dictionary<string, string[]> MovedLines = new(StringComparer.Ordinal)
    {
        ["token.js"] = ["const TokenKey = \"controlroom-token\";"],
        ["capture.js"] = ["const CaptureProjectKey = \"controlroom-capture-project\";"],
        ["history.js"] = ["const HistoryProjectKey = \"controlroom-history-project\";"],
        ["push.js"] =
        [
            "  homeScreen: \"To get notifications on iPhone, add Control Room to the Home Screen and open it from there.\",",
            "  denied: \"Notifications are blocked for Control Room. Allow them in Settings, then check again.\",",
        ],
        ["sw.js"] = ["const CacheName = \"controlroom-shell-\" + CacheVersion;", "  updated: \"ControlRoom updated\","],
    };

    private static readonly string[] KitGlobals =
        ["TokenKey", "CaptureProjectKey", "HistoryProjectKey", "CacheVersion", "CacheName", "Shell"];

    [Fact]
    public void Folders_HoldEveryDevelopPathInExactlyOneOfThem()
    {
        string[] app = Listing(AppFolder());
        string[] kit = Listing(KitFolder());

        Assert.Equal(AppFiles.Order(StringComparer.Ordinal), app);
        Assert.Equal(KitFiles.Order(StringComparer.Ordinal), kit);
        Assert.Empty(app.Intersect(kit, StringComparer.Ordinal));
        string[] develop = [.. DevelopHashes().Keys.Order(StringComparer.Ordinal)];
        Assert.Equal(develop, app.Concat(kit).Where(path => path != "room.js").Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task EveryPath_IsServedWithItsBytesTypeAndCacheHeader(string environment)
    {
        using TempFactoryRoot root = new();
        using ServerFactory server = new(root.Path);
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory =
            server.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        HttpClient client = factory.CreateClient();
        client.BaseAddress = new Uri("http://127.0.0.1:8100/");

        foreach ((string folder, string[] files) in new[] { (AppFolder(), AppFiles), (KitFolder(), KitFiles) })
        {
            foreach (string path in files)
            {
                using HttpResponseMessage response = await client.GetAsync("/" + path);
                Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK, path + " gave " + response.StatusCode);
                Assert.Equal(ContentType(path), response.Content.Headers.ContentType?.ToString());
                Assert.Equal(path == "sw.js" ? "no-cache" : null, response.Headers.CacheControl?.ToString());
                byte[] body = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(File.ReadAllBytes(Path.Combine(folder, path)), body);
            }
        }
    }

    [Fact]
    public void UnchangedFiles_AreByteIdenticalToDevelop()
    {
        string[] changed = ["sw.js", "token.js", "push.js", "capture.js", "history.js", "index.html"];
        // act.js gained rework note validation after the split. ActWebAppTests checks the served script.
        foreach ((string path, string hash) in DevelopHashes().Where(file => file.Key != "act.js"))
        {
            if (changed.Contains(path))
            {
                Assert.Equal(hash, Sha256(File.ReadAllBytes(Path.Combine(DevelopFolder(), path + ".develop"))));
                continue;
            }

            string folder = AppFiles.Contains(path) ? AppFolder() : KitFolder();
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, path));
            if (path == "board.js")
            {
                // Undo the reviewed decision refresh hunks on a private copy, then compare to the original hash.
                using TempFactoryRoot copy = new();
                string board = Path.Combine(copy.Path, path);
                File.WriteAllBytes(board, bytes);
                TempFactoryRoot.Git(copy.Path, "init", "-q");
                TempFactoryRoot.Git(copy.Path, "-c", "core.autocrlf=false", "apply", "--reverse",
                    Path.Combine(DevelopFolder(), "board-refresh.patch"));
                const string WindowsHint = """
                      if (command.startsWith(".\\launch-agent.ps1 ")) {
                        text.append(" Run this command in PowerShell from the extracted release folder.");
                      }
                    """;
                string script = Encoding.UTF8.GetString(File.ReadAllBytes(board));
                Assert.Contains(WindowsHint, script, StringComparison.Ordinal);
                bytes = Encoding.UTF8.GetBytes(script.Replace(WindowsHint + "\n", "", StringComparison.Ordinal));
            }
            else if (path == "counts.js")
            {
                const string BoardObservation = "    if (typeof refreshOpenJob === \"function\") {\n"
                    + "      refreshOpenJob(data.jobs);\n    }\n";
                string script = Encoding.UTF8.GetString(bytes);
                Assert.Contains(BoardObservation, script, StringComparison.Ordinal);
                bytes = Encoding.UTF8.GetBytes(script.Replace(BoardObservation, "", StringComparison.Ordinal));
            }
            else if (path == "live.js")
            {
                const string Fingerprint = "    const previousJobs = liveLast?.jobs;\n";
                const string PendingCheck = "    if (boardViewsVisible() && typeof recheckPendingCancel === \"function\") {\n"
                    + "      await recheckPendingCancel(previousJobs !== next.jobs || recovered, { includeForm: previousJobs === undefined });\n    }\n";
                string script = Encoding.UTF8.GetString(bytes);
                Assert.Contains(Fingerprint, script, StringComparison.Ordinal);
                Assert.Contains(PendingCheck, script, StringComparison.Ordinal);
                bytes = Encoding.UTF8.GetBytes(script.Replace(Fingerprint, "", StringComparison.Ordinal)
                    .Replace(PendingCheck, "", StringComparison.Ordinal));
            }

            Assert.True(hash == Sha256(bytes), path + " changed");
        }
    }

    [Theory]
    [InlineData("token.js")]
    [InlineData("capture.js")]
    [InlineData("history.js")]
    public void SimpleFiles_LoseOnlyTheirMovedLine(string file)
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), file + ".develop"));
        Assert.True(develop.Remove(MovedLines[file][0]));

        Assert.Equal(develop, Lines(Path.Combine(KitFolder(), file)));
    }

    [Fact]
    public void Push_ReplacesItsTwoTextsWithTheAppTexts()
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), "push.js.develop"));
        int home = develop.IndexOf(MovedLines["push.js"][0]);
        Assert.True(home >= 0);
        develop[home] = "  ...PushAppTexts,";
        Assert.True(develop.Remove(MovedLines["push.js"][1]));

        Assert.Equal(develop, Lines(Path.Combine(KitFolder(), "push.js")));
    }

    [Fact]
    public void ServiceWorker_LosesOnlyTheMovedLinesAndReadsTheirNames()
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), "sw.js.develop"));
        List<string> room = Lines(Path.Combine(AppFolder(), "room.js"));
        int start = develop.FindIndex(line => line.StartsWith("const CacheVersion = ", StringComparison.Ordinal));
        int end = develop.IndexOf("];", start);
        Assert.True(start >= 0 && end > start);
        // The shell list block moved, with "/room.js" added and the version raised.
        List<string> block = develop.GetRange(start, end - start + 1);
        block[0] = "const CacheVersion = \"v23\";";
        block.Insert(block.IndexOf("  \"/token.js\",") + 1, "  \"/room.js\",");
        int roomStart = room.IndexOf("const CacheVersion = \"v23\";");
        Assert.Equal(block, room.GetRange(roomStart, block.Count));
        develop.RemoveRange(start, end - start + 1);

        int updated = develop.IndexOf(MovedLines["sw.js"][1]);
        develop[updated] = "  ...PushAppTitles,";
        int fallback = develop.FindIndex(line => line.StartsWith("const PushFallbackTitle = ", StringComparison.Ordinal));
        Assert.Contains(develop[fallback], room);
        develop.RemoveAt(fallback);

        for (int i = 0; i < develop.Count; i++)
        {
            develop[i] = develop[i]
                .Replace("let tag = \"controlroom\";", "let tag = PushDefaultTag;", StringComparison.Ordinal)
                .Replace("\"ControlRoom \" + version", "PushUpdatePrefix + version", StringComparison.Ordinal)
                .Replace("The old version didn't come back. Check the Mac.",
                    "The old version didn't restart. Check the computer running the server.", StringComparison.Ordinal);
        }

        develop.Insert(0, "importScripts(\"/room.js\");");
        Assert.Equal(develop, Lines(Path.Combine(KitFolder(), "sw.js")));
    }

    [Fact]
    public void Index_GainsOnlyTheRoomScriptAsItsFirstScript()
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), "index.html.develop"));
        List<string> index = Lines(Path.Combine(AppFolder(), "index.html"));
        const string Room = "  <script src=\"/room.js\" defer></script>";

        Assert.Equal(index.FindIndex(line => line.Contains("<script", StringComparison.Ordinal)), index.IndexOf(Room));
        Assert.Equal(index.IndexOf(Room) + 1, index.IndexOf("  <script src=\"/token.js\" defer></script>"));
        index.Remove(Room);
        Assert.True(index.Remove("""      <p class="token-help">On Windows, open PowerShell in the server folder and run <code>.\controlroom.exe token</code>.</p>"""));
        Assert.Equal(develop, index);
    }

    [Fact]
    public void RoomScript_HoldsEveryMovedLineAndNoKitFileDeclaresTheGlobals()
    {
        List<string> room = Lines(Path.Combine(AppFolder(), "room.js"));
        foreach (string line in MovedLines.Values.SelectMany(lines => lines))
        {
            Assert.Contains(line, room);
        }

        Assert.Contains("const PushDefaultTag = \"controlroom\";", room);
        Assert.Contains("const PushUpdatePrefix = \"ControlRoom \";", room);
        foreach (string file in KitFiles)
        {
            string text = File.ReadAllText(Path.Combine(KitFolder(), file));
            foreach (string name in KitGlobals)
            {
                Assert.DoesNotContain("const " + name + " ", text, StringComparison.Ordinal);
                Assert.DoesNotContain("let " + name + " ", text, StringComparison.Ordinal);
            }
        }

        string sw = File.ReadAllText(Path.Combine(KitFolder(), "sw.js"));
        Assert.StartsWith("importScripts(\"/room.js\");\n", sw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/index.html\"", sw, StringComparison.Ordinal);
    }

    private static string ContentType(string path)
    {
        return Path.GetExtension(path) switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".png" => "image/png",
            ".webmanifest" => "application/manifest+json",
            _ => throw new ArgumentException(path),
        };
    }

    private static List<string> Lines(string path)
    {
        return [.. File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
    }

    private static string[] Listing(string folder)
    {
        return [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    private static Dictionary<string, string> DevelopHashes()
    {
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(DevelopFolder(), "wwwroot.sha256")))
        {
            hashes.Add(line[66..], line[..64]);
        }

        return hashes;
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string AppFolder() => Path.Combine(AccessTokenTests.SourceFolder(), "wwwroot");

    private static string KitFolder() => Path.Combine(AccessTokenTests.KitSourceFolder(), "wwwroot");

    private static string DevelopFolder() =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(AccessTokenTests.SourceFolder())!)!,
            "tests", "ControlRoom.Server.Tests", "Develop");
}
