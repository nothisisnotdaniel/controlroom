using System.Net;
using System.Text;

namespace ControlRoom.Server.Tests;

/// <summary><c>settings.json</c> through <c>GET /api/update</c> and <c>POST /api/update/settings</c>, and the store's
/// file rules.</summary>
public sealed class UpdateSettingsTests
{
    private const string OnFile = "{\"autoUpdate\":true}\n";

    [Fact]
    public async Task Get_WithoutASettingsFile_IsOff()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.EndsWith(",\"autoUpdate\":false}", json, StringComparison.Ordinal);
        Assert.False(File.Exists(SettingsPath(factory)));
    }

    [Theory]
    [InlineData(true, "{\"autoUpdate\":true}\n")]
    [InlineData(false, "{\"autoUpdate\":false}\n")]
    public async Task Post_StoresTheExactFileAndReturnsTheValue(bool value, string file)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await Post(client, "{\"autoUpdate\":" + (value ? "true" : "false") + "}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("{\"autoUpdate\":" + (value ? "true" : "false") + "}", await response.Content.ReadAsStringAsync());
        Assert.Equal(file, File.ReadAllText(SettingsPath(factory)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(SettingsPath(factory)));
        }

        Assert.EndsWith(",\"autoUpdate\":" + (value ? "true" : "false") + "}",
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
        Assert.EndsWith(",\"autoUpdate\":null}", json, StringComparison.Ordinal);
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
    [InlineData("{\"autoUpdate\":true}", true)]
    [InlineData("{\"autoUpdate\":true}\n", true)]
    [InlineData("{\"autoUpdate\":false}", false)]
    [InlineData("{\"autoUpdate\":false}\n", false)]
    [InlineData("{\"autoUpdate\":true}\n\n", null)]
    [InlineData("{\"autoUpdate\": true}", null)]
    [InlineData("{\"autoUpdate\":true}\r\n", null)]
    [InlineData(" {\"autoUpdate\":true}", null)]
    [InlineData("", null)]
    public void Parse_AcceptsOnlyTheExactFiles(string content, bool? expected)
    {
        Assert.Equal(expected, UpdateSettingsStore.Parse(Encoding.UTF8.GetBytes(content)));
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
