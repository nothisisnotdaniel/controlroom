using System.Net;
using System.Runtime.Versioning;
using System.Text;

namespace ControlRoom.Server.Tests;

/// <summary><c>settings.json</c> through <c>GET /api/update</c> and <c>POST /api/update/settings</c>, and the store's
/// file rules.</summary>
public sealed class UpdateSettingsTests
{
    private const string OnFile = "{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":30}\n";

    [Fact]
    public async Task Get_WithoutASettingsFile_IsOffAndStable()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.EndsWith(",\"autoUpdate\":false,\"channel\":\"stable\",\"updateInterval\":30}", json, StringComparison.Ordinal);
        Assert.False(File.Exists(SettingsPath(factory)));
    }

    [Theory]
    [InlineData(true, "{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":30}\n")]
    [InlineData(false, "{\"autoUpdate\":false,\"channel\":\"stable\",\"updateInterval\":30}\n")]
    public async Task Post_StoresTheExactFileAndReturnsTheValue(bool value, string file)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await Post(client, "{\"autoUpdate\":" + (value ? "true" : "false") + "}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("{\"autoUpdate\":" + (value ? "true" : "false") + ",\"channel\":\"stable\",\"updateInterval\":30}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(file, File.ReadAllText(SettingsPath(factory)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(SettingsPath(factory)));
        }

        Assert.EndsWith(",\"autoUpdate\":" + (value ? "true" : "false") + ",\"channel\":\"stable\",\"updateInterval\":30}",
            await client.GetStringAsync(UpdateEndpoint.Route), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("{\"autoUpdate\":\"true\"}")]
    [InlineData("{\"autoUpdate\":1}")]
    [InlineData("{\"autoUpdate\":null}")]
    [InlineData("{\"autoUpdate\":true,\"x\":1}")]
    [InlineData("{\"autoUpdate\":true,\"autoUpdate\":false}")]
    [InlineData("{\"auto\":true}")]
    [InlineData("{\"channel\":\"beta\"}")]
    [InlineData("{\"channel\":\"Develop\"}")]
    [InlineData("{\"channel\":\"develop \"}")]
    [InlineData("{\"channel\":\"\"}")]
    [InlineData("{\"channel\":null}")]
    [InlineData("{\"channel\":true}")]
    [InlineData("{\"channel\":[\"develop\"]}")]
    [InlineData("{\"channel\":\"develop\",\"channel\":\"stable\"}")]
    [InlineData("{\"channel\":\"develop\",\"autoUpdate\":true,\"x\":1}")]
    [InlineData("{\"Channel\":\"develop\"}")]
    [InlineData("[{\"channel\":\"develop\"}]")]
    public async Task Post_AnyOtherBody_Is400AndStoresNothing(string body)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await Post(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid\"}", await response.Content.ReadAsStringAsync());
        Assert.False(File.Exists(SettingsPath(factory)));
    }

    [Fact]
    public async Task Post_InvalidBodyWithABadStore_IsStill400()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        WriteSettings(factory, "nonsense");

        using HttpResponseMessage response = await Post(client, "{\"autoUpdate\":\"yes\"}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string> BadStores => new() { "symlink", "loose-file", "loose-folder", "invalid", "folder" };

    [Theory]
    [MemberData(nameof(BadStores))]
    public async Task BadStore_Is500OnPostAndNullOnGetAndIsNotOverwritten(string bad)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string path = SettingsPath(factory);
        string folder = Path.GetDirectoryName(path)!;
        string target = Path.Combine(root.Path, "elsewhere.json");
        switch (bad)
        {
            case "symlink":
                File.WriteAllText(target, "{\"autoUpdate\":false}\n");
                File.SetUnixFileMode(target, AccessToken.FileMode);
                File.CreateSymbolicLink(path, target);
                break;
            case "loose-file":
                WriteSettings(factory, "{\"autoUpdate\":false}\n");
                File.SetUnixFileMode(path, AccessToken.FileMode | UnixFileMode.GroupRead);
                break;
            case "loose-folder":
                WriteSettings(factory, "{\"autoUpdate\":false}\n");
                File.SetUnixFileMode(folder, AccessToken.FolderMode | UnixFileMode.OtherExecute);
                break;
            case "invalid":
                WriteSettings(factory, "{\"autoUpdate\": false}\n");
                break;
            default:
                Directory.CreateDirectory(path);
                break;
        }

        IReadOnlyList<string> before = Snapshot(folder, target);

        using HttpResponseMessage response = await Post(client, "{\"autoUpdate\":true}");
        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("{\"error\":\"settings_store\"}", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.EndsWith(",\"autoUpdate\":null,\"channel\":null,\"updateInterval\":null}", json, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(folder, target));
        File.SetUnixFileMode(folder, AccessToken.FolderMode);
    }

    [Fact]
    public void Store_RefusesAFolderOrFileOfAnotherOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        string folder = Path.Combine(tokens.Path, "support");
        UpdateSettingsStore own = new(folder, UnixOwner.CurrentUser());
        Assert.True(own.SetAutoUpdate(true));

        UpdateSettingsStore other = new(folder, UnixOwner.CurrentUser() + 1);

        Assert.Throws<PrivateFileException>(() => other.AutoUpdate());
        Assert.Throws<PrivateFileException>(() => other.SetAutoUpdate(false));
        Assert.Equal(OnFile, File.ReadAllText(own.Path));
        Assert.True(own.AutoUpdate());
    }

    [Theory]
    [InlineData("{\"autoUpdate\":true}", "true stable")]
    [InlineData("{\"autoUpdate\":true}\n", "true stable")]
    [InlineData("{\"autoUpdate\":false}", "false stable")]
    [InlineData("{\"autoUpdate\":false}\n", "false stable")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\"}", "true stable")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\"}\n", "true stable")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"stable\"}\n", "false stable")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}", "true develop")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n", "true develop")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\"}", "false develop")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\"}\n", "false develop")]
    [InlineData("{\"autoUpdate\":true}\n\n", null)]
    [InlineData("{\"autoUpdate\": true}", null)]
    [InlineData("{\"autoUpdate\":true}\r\n", null)]
    [InlineData(" {\"autoUpdate\":true}", null)]
    [InlineData("", null)]
    [InlineData("{\"channel\":\"develop\",\"autoUpdate\":true}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"beta\"}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"Develop\"}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\": \"develop\"}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n\n", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\r\n", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\",\"x\":1}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"channel\":\"develop\"}", null)]
    [InlineData("{\"channel\":\"develop\"}", null)]
    public void Parse_AcceptsOnlyTheExactFiles(string content, string? expected)
    {
        UpdateSettings? parsed = UpdateSettingsStore.Parse(Encoding.UTF8.GetBytes(content));

        Assert.Equal(expected, parsed is null ? null : (parsed.AutoUpdate ? "true " : "false ") + parsed.Channel);
    }

    [Theory]
    [InlineData("{\"autoUpdate\":true}", "true stable 30")]
    [InlineData("{\"autoUpdate\":false}\n", "false stable 30")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\"}", "true stable 30")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\"}\n", "false develop 30")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":30}", "true stable 30")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60}\n", "true stable 60")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"stable\",\"updateInterval\":120}", "false stable 120")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":360}\n", "true develop 360")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\",\"updateInterval\":720}\n", "false develop 720")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":45}", "true stable 30")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":1}\n", "true stable 30")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":999999}", "true stable 30")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":0}", "true stable 30")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":00}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":060}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":\"60\"}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":-5}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60.0}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":1000000}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\": 60}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60}\n\n", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60}\r\n", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60,\"x\":1}", null)]
    [InlineData("{\"autoUpdate\":true,\"updateInterval\":60,\"channel\":\"stable\"}", null)]
    [InlineData("{\"autoUpdate\":true,\"updateInterval\":60}", null)]
    [InlineData("{\"updateInterval\":60,\"autoUpdate\":true,\"channel\":\"stable\"}", null)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60}}", null)]
    public void Parse_ReadsTheIntervalOnlyFromTheExactFourKeyForm(string content, string? expected)
    {
        UpdateSettings? parsed = UpdateSettingsStore.Parse(Encoding.UTF8.GetBytes(content));

        Assert.Equal(expected, parsed is null
            ? null
            : (parsed.AutoUpdate ? "true " : "false ") + parsed.Channel + " " + parsed.UpdateInterval);
    }

    [Fact]
    public void Store_WithoutAFile_ReadsInterval30AndTheLongestFileFitsTheCap()
    {
        using TempTokenFolder tokens = new();
        UpdateSettingsStore store = new(Path.Combine(tokens.Path, "support"), UnixOwner.CurrentUser());

        Assert.Equal(30, store.Settings().UpdateInterval);
        Assert.Equal(new UpdateSettings(false, "develop", 720), store.Set(false, "develop", 720, out bool changed));
        Assert.True(changed);
        Assert.Equal("{\"autoUpdate\":false,\"channel\":\"develop\",\"updateInterval\":720}\n", File.ReadAllText(store.Path));
        Assert.True(new FileInfo(store.Path).Length <= UpdateSettingsStore.MaxFileBytes);
        Assert.Equal(new UpdateSettings(false, "develop", 720), store.Settings());
        Assert.Throws<ArgumentException>(() => store.Set(null, null, 45, out _));
        Assert.Equal(720, store.Settings().UpdateInterval);
    }

    [Theory]
    [InlineData(null, 30, "{\"autoUpdate\":false,\"channel\":\"stable\",\"updateInterval\":30}")]
    [InlineData("{\"autoUpdate\":true}", 60, "{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60}")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n", 120, "{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":120}")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\",\"updateInterval\":30}\n", 360, "{\"autoUpdate\":false,\"channel\":\"develop\",\"updateInterval\":360}")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":45}", 720, "{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":720}")]
    public async Task Post_Interval_StoresTheFourKeyFormKeepingTheOtherValues(string? file, int interval, string expected)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        if (file is not null)
        {
            WriteSettings(factory, file);
        }

        using HttpResponseMessage response = await Post(client, "{\"updateInterval\":" + interval + "}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
        Assert.Equal(expected + "\n", File.ReadAllText(SettingsPath(factory)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(SettingsPath(factory)));
        }

        Assert.EndsWith(",\"updateInterval\":" + interval + "}", await client.GetStringAsync(UpdateEndpoint.Route),
            StringComparison.Ordinal);
        Assert.Empty(factory.Update.Requests);
    }

    [Theory]
    [InlineData("{\"updateInterval\":45}")]
    [InlineData("{\"updateInterval\":0}")]
    [InlineData("{\"updateInterval\":-30}")]
    [InlineData("{\"updateInterval\":30.5}")]
    [InlineData("{\"updateInterval\":60.0}")]
    [InlineData("{\"updateInterval\":6e1}")]
    [InlineData("{\"updateInterval\":\"60\"}")]
    [InlineData("{\"updateInterval\":null}")]
    [InlineData("{\"updateInterval\":true}")]
    [InlineData("{\"updateInterval\":[60]}")]
    [InlineData("{\"updateInterval\":99999999999}")]
    [InlineData("{\"updateInterval\":60,\"updateInterval\":60}")]
    [InlineData("{\"updateInterval\":60,\"x\":1}")]
    [InlineData("{\"UpdateInterval\":60}")]
    [InlineData("{\"autoUpdate\":true,\"updateInterval\":45}")]
    public async Task Post_BadInterval_Is400AndLeavesTheFileUnchanged(string body)
    {
        const string Saved = "{\"autoUpdate\":true,\"channel\":\"develop\"}\n";
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        WriteSettings(factory, Saved);

        using HttpResponseMessage response = await Post(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal(Saved, File.ReadAllText(SettingsPath(factory)));
        Assert.Empty(factory.Update.Requests);
    }

    [Fact]
    public async Task Post_IntervalWithARefusedFile_Is500AndDoesNotOverwriteIt()
    {
        const string Saved = "{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":060}\n";
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        WriteSettings(factory, Saved);

        using HttpResponseMessage response = await Post(client, "{\"updateInterval\":60}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("{\"error\":\"settings_store\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal(Saved, File.ReadAllText(SettingsPath(factory)));
        Assert.EndsWith(",\"autoUpdate\":null,\"channel\":null,\"updateInterval\":null}",
            await client.GetStringAsync(UpdateEndpoint.Route), StringComparison.Ordinal);
        Assert.Empty(factory.Update.Requests);
    }

    [Theory]
    [InlineData("{\"autoUpdate\":true}", "true", "stable")]
    [InlineData("{\"autoUpdate\":false}\n", "false", "stable")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\"}\n", "true", "stable")]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\"}", "false", "develop")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n", "true", "develop")]
    public async Task Get_ReportsEveryAllowedFormAndLeavesItAsItIs(string file, string autoUpdate, string channel)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        WriteSettings(factory, file);

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.EndsWith(",\"autoUpdate\":" + autoUpdate + ",\"channel\":\"" + channel + "\",\"updateInterval\":30}", json, StringComparison.Ordinal);
        Assert.Equal(file, File.ReadAllText(SettingsPath(factory)));
    }

    [Theory]
    [InlineData(null, "{\"channel\":\"develop\"}", "{\"autoUpdate\":false,\"channel\":\"develop\"}")]
    [InlineData("{\"autoUpdate\":true}", "{\"channel\":\"develop\"}", "{\"autoUpdate\":true,\"channel\":\"develop\"}")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n", "{\"autoUpdate\":false}", "{\"autoUpdate\":false,\"channel\":\"develop\"}")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}", "{\"channel\":\"stable\"}", "{\"autoUpdate\":true,\"channel\":\"stable\"}")]
    [InlineData("{\"autoUpdate\":false}", "{\"channel\":\"develop\",\"autoUpdate\":true}", "{\"autoUpdate\":true,\"channel\":\"develop\"}")]
    [InlineData("{\"autoUpdate\":false}", "{\"autoUpdate\":true,\"channel\":\"stable\"}", "{\"autoUpdate\":true,\"channel\":\"stable\"}")]
    public async Task Post_PartialBody_KeepsTheOtherValueAndWritesTheFourKeyForm(string? file, string body, string expected)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        if (file is not null)
        {
            WriteSettings(factory, file);
        }

        using HttpResponseMessage response = await Post(client, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string fourKey = expected[..^1] + ",\"updateInterval\":30}";
        Assert.Equal(fourKey, await response.Content.ReadAsStringAsync());
        Assert.Equal(fourKey + "\n", File.ReadAllText(SettingsPath(factory)));
    }

    [Fact]
    public void Store_SetKeepsMissingValuesAndRefusesAnUnknownChannel()
    {
        using TempTokenFolder tokens = new();
        UpdateSettingsStore store = new(Path.Combine(tokens.Path, "support"), UnixOwner.CurrentUser());

        Assert.Equal(new UpdateSettings(false, "stable"), store.Settings());
        Assert.Equal(new UpdateSettings(false, "develop"), store.Set(null, "develop", out bool changed));
        Assert.True(changed);
        Assert.Equal(new UpdateSettings(true, "develop"), store.Set(true, null, out changed));
        Assert.False(changed);
        Assert.Throws<ArgumentException>(() => store.Set(null, "beta"));
        Assert.Equal("{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":30}\n", File.ReadAllText(store.Path));
    }

    [Fact]
    public async Task Post_WithoutTheToken_Is401AndStoresNothing()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Post(client, "{\"autoUpdate\":true}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(File.Exists(SettingsPath(factory)));
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string body)
    {
        return client.PostAsync(UpdateEndpoint.SettingsRoute, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static string SettingsPath(ServerFactory factory)
    {
        return Path.Combine(Path.GetDirectoryName(factory.TokenFile)!, UpdateSettingsStore.FileName);
    }

    private static void WriteSettings(ServerFactory factory, string content)
    {
        string path = SettingsPath(factory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, AccessToken.FileMode);
        }
    }

    /// <summary>Called only after the Windows guard in the bad-store test.</summary>
    [UnsupportedOSPlatform("windows")]
    private static List<string> Snapshot(string folder, string target)
    {
        List<string> entries = [];
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder).Append(target).Where(Path.Exists))
        {
            FileInfo info = new(entry);
            entries.Add(entry + " " + File.GetUnixFileMode(entry) + " " + info.LinkTarget + " "
                + (info.Exists && info.LinkTarget is null ? File.ReadAllText(entry) : ""));
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }
}
