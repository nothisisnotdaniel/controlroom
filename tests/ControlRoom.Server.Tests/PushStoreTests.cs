using System.Net;

namespace ControlRoom.Server.Tests;

/// <summary>The push files: created <c>0600</c> next to the token file, the same key across restarts, and refused
/// (never overwritten) when symlinked or loose-mode.</summary>
public sealed class PushStoreTests
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    public async Task Files_AreCreatedPrivateInTheTokenFolderAndNothingInTheRoot()
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        using TempTokenFolder tokens = new();
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path, tokens.TokenFile);
        using HttpClient client = factory.CreateTokenClient();

        PushInfoBody info = await PushTestData.Info(client);

        string folder = Path.GetDirectoryName(tokens.TokenFile)!;
        string keys = Path.Combine(folder, PushStore.KeysFileName);
        string settings = Path.Combine(folder, PushStore.SettingsFileName);
        Assert.True(File.Exists(keys));
        Assert.True(File.Exists(settings));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(Private, File.GetUnixFileMode(keys));
            Assert.Equal(Private, File.GetUnixFileMode(settings));
        }

        Assert.False(info.NotifyDone);
        Assert.Equal(0, info.Subscribed);
        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task Key_IsTheSameAcrossRestarts()
    {
        using TempFactoryRoot root = new();
        using TempTokenFolder tokens = new();
        string first;
        using (ServerFactory factory = new(root.Path, tokens.TokenFile))
        {
            using HttpClient client = factory.CreateTokenClient();
            first = (await PushTestData.Info(client)).PublicKey;
        }

        using ServerFactory again = new(root.Path, tokens.TokenFile);
        using HttpClient second = again.CreateTokenClient();

        Assert.Equal(first, (await PushTestData.Info(second)).PublicKey);
        Assert.Equal(65, Base64Url.Decode(first)!.Length);
    }

    [Theory]
    [InlineData(PushStore.KeysFileName)]
    [InlineData(PushStore.SettingsFileName)]
    public async Task SymlinkedFile_FailsEveryRouteAndIsLeftAsItIs(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        using TempTokenFolder tokens = new();
        using ServerFactory factory = new(root.Path, tokens.TokenFile);
        using HttpClient client = factory.CreateTokenClient();
        string folder = Path.GetDirectoryName(tokens.TokenFile)!;
        string target = Path.Combine(tokens.Path, "elsewhere.json");
        File.WriteAllText(target, "{}");
        File.SetUnixFileMode(target, Private);
        string link = Path.Combine(folder, name);
        File.CreateSymbolicLink(link, target);

        await AssertEveryRouteFails(client);

        Assert.Equal(target, new FileInfo(link).LinkTarget);
        Assert.Equal("{}", File.ReadAllText(target));
    }

    [Theory]
    [InlineData(PushStore.KeysFileName, UnixFileMode.GroupRead)]
    [InlineData(PushStore.SettingsFileName, UnixFileMode.OtherRead)]
    [InlineData(PushStore.SettingsFileName, UnixFileMode.GroupWrite)]
    public async Task LooseModeFile_FailsEveryRouteAndIsLeftAsItIs(string name, UnixFileMode extra)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        using TempTokenFolder tokens = new();
        using ServerFactory factory = new(root.Path, tokens.TokenFile);
        using HttpClient client = factory.CreateTokenClient();
        await PushTestData.Info(client);
        string file = Path.Combine(Path.GetDirectoryName(tokens.TokenFile)!, name);
        File.SetUnixFileMode(file, Private | extra);
        byte[] bytes = File.ReadAllBytes(file);

        await AssertEveryRouteFails(client);

        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal(Private | extra, File.GetUnixFileMode(file));
    }

    [Fact]
    public async Task InvalidFile_FailsAndIsLeftAsItIs()
    {
        using TempFactoryRoot root = new();
        using TempTokenFolder tokens = new();
        using ServerFactory factory = new(root.Path, tokens.TokenFile);
        using HttpClient client = factory.CreateTokenClient();
        await PushTestData.Info(client);
        string file = Path.Combine(Path.GetDirectoryName(tokens.TokenFile)!, PushStore.KeysFileName);
        File.WriteAllText(file, "{\"publicKey\":\"x\"}");

        await AssertEveryRouteFails(client);

        Assert.Equal("{\"publicKey\":\"x\"}", File.ReadAllText(file));
    }

    [Fact]
    public void Store_KeepsSubscriptionsAndSettingsWithPrivateWrites()
    {
        using TempTokenFolder tokens = new();
        PushStore store = new(tokens.Path);
        using PushReceiver receiver = new();
        PushSubscription subscription = new(
            PushTestData.AppleEndpoint, Base64Url.Encode(receiver.PublicKey), Base64Url.Encode(receiver.Auth));

        Assert.True(store.Subscribe(subscription));
        store.SetNotifyDone(true);

        PushSettings settings = new PushStore(tokens.Path).Settings();
        Assert.True(settings.NotifyDone);
        Assert.Equal([subscription], settings.Subscriptions);
        Assert.Equal(1, store.Remove([PushTestData.AppleEndpoint]));
        Assert.Empty(store.Settings().Subscriptions);
        string[] files = Directory.GetFiles(tokens.Path).Select(path => Path.GetFileName(path)).ToArray();
        Assert.Equal(new[] { PushStore.SettingsFileName }, files);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(Private, File.GetUnixFileMode(store.SettingsPath));
        }
    }

    private static async Task AssertEveryRouteFails(HttpClient client)
    {
        using PushReceiver receiver = new();
        foreach (Func<HttpClient, Task<HttpResponseMessage>> call in PushTestData.EveryRoute(receiver))
        {
            using HttpResponseMessage response = await call(client);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("{\"error\":\"push_store\"}", await response.Content.ReadAsStringAsync());
        }
    }
}
