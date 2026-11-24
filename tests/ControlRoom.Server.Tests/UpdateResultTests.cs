using System.Text;

namespace ControlRoom.Server.Tests;

/// <summary><c>update-result.json</c>: the reader's file rules and schema, and the watcher sending each new run once
/// (<see cref="PushWatcher.Scan"/> is called directly; no timer, no waiting).</summary>
public sealed class UpdateResultTests : IDisposable
{
    private const string UpdatedJson =
        "{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"}\n";

    private readonly TempFactoryRoot root = new();

    private readonly TempTokenFolder folder = new();

    private readonly PushStore store;

    private readonly FakePushHandler handler = new();

    private readonly PushReceiver receiver = new();

    private readonly PushWatcher watcher;

    public UpdateResultTests()
    {
        store = new PushStore(System.IO.Path.Combine(folder.Path, "push"));
        Assert.True(store.Subscribe(new PushSubscription(
            PushTestData.AppleEndpoint, Base64Url.Encode(receiver.PublicKey), Base64Url.Encode(receiver.Auth))));
        LogCapture logs = new();
        PushCrypto crypto = new(new FakeTime(), PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, null);
        PushSender sender = new(store, crypto, new PushTransport(handler), logs.For<PushSender>());
        watcher = new PushWatcher(new FactoryRoot(root.Path), store, sender, new FakeTime(), logs.For<PushWatcher>());
    }

    private string ResultPath => System.IO.Path.Combine(store.Folder, UpdateResultReader.FileName);

    public void Dispose()
    {
        watcher.Dispose();
        receiver.Dispose();
        folder.Dispose();
        root.Dispose();
    }

    [Theory]
    [InlineData(UpdatedJson)]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"check\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"update_failed\",\"version\":\"1.3.0-rc.1\",\"reason\":\"rollback_failed\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"bootstrap_failed\"}")]
    public void Parse_AcceptsTheSchema(string json)
    {
        Assert.NotNull(UpdateResult.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"1.3.0\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"1.3.0\",\"reason\":\"check\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\",\"x\":\"y\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\",\"previous\":\"1.2.0\"}")]
    [InlineData("{\"run\":\"2026-11-18T20:15:00Z\",\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"v1.3.0\",\"previous\":\"1.2.0\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"<b>\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"other\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"update_failed\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"}")]
    [InlineData("{\"run\":\"20261118T201500Z\",\"result\":\"installed\",\"version\":\"1.3.0\",\"reason\":\"check\"}")]
    [InlineData("{\"run\":20261118,\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"}")]
    public void Parse_RefusesAnythingElse(string json)
    {
        Assert.Null(UpdateResult.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Reader_ReadsAPrivateFile()
    {
        WriteResult(UpdatedJson);

        Assert.Equal(
            new UpdateResult("20261118T201500Z", UpdateResult.Updated, "1.3.0", "1.2.0", null), Reader().Read());
    }

    [Fact]
    public void Reader_IgnoresAMissingFileOrFolder()
    {
        Assert.Null(Reader().Read());
        Assert.Null(new UpdateResultReader(System.IO.Path.Combine(folder.Path, "none"), UnixOwner.CurrentUser()).Read());
        Assert.False(Directory.Exists(System.IO.Path.Combine(folder.Path, "none")));
    }

    [Fact]
    public void Reader_IgnoresALooseModeFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteResult(UpdatedJson);
        File.SetUnixFileMode(ResultPath, AccessToken.FileMode | UnixFileMode.OtherRead);

        Assert.Null(Reader().Read());
        Assert.Equal(UpdatedJson, File.ReadAllText(ResultPath));
    }

    [Fact]
    public void Reader_IgnoresASymlink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string target = System.IO.Path.Combine(folder.Path, "elsewhere.json");
        File.WriteAllText(target, UpdatedJson);
        File.SetUnixFileMode(target, AccessToken.FileMode);
        File.CreateSymbolicLink(ResultPath, target);

        Assert.Null(Reader().Read());
    }

    [Fact]
    public void Reader_IgnoresAnOversizeFile()
    {
        WriteResult(UpdatedJson.TrimEnd('\n') + new string(' ', (int)UpdateResultReader.MaxFileBytes));

        Assert.Null(Reader().Read());
    }

    [Fact]
    public void Reader_IgnoresAFileOfAnotherOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteResult(UpdatedJson);
        // The folder check passes only for its owner, so a reader for another uid refuses both.
        Assert.Null(new UpdateResultReader(store.Folder, UnixOwner.CurrentUser() + 1).Read());
        Assert.Null(new UpdateResultReader(store.Folder, null).Read());
    }

    [Fact]
    public void Reader_IgnoresALooseModeFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteResult(UpdatedJson);
        File.SetUnixFileMode(store.Folder, AccessToken.FolderMode | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        Assert.Null(Reader().Read());
    }

    [Fact]
    public async Task UpdatedResult_IsSentOnTheFirstScanOnceWithTheExactPayload()
    {
        WriteResult(UpdatedJson);

        await watcher.Scan();
        await watcher.Scan();

        Assert.Equal(
            "{\"kind\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\",\"reason\":\"Updated\"}",
            Assert.Single(Payloads()));
        Assert.Equal("20261118T201500Z", store.Settings().UpdateResultNotified);
        Assert.Equal(UpdatedJson, File.ReadAllText(ResultPath));
    }

    [Theory]
    [InlineData("check")]
    [InlineData("no_answer")]
    [InlineData("swap")]
    [InlineData("bootstrap_failed")]
    [InlineData("rollback_failed")]
    public async Task FailedResult_IsSentWithItsCode(string reason)
    {
        await watcher.Scan();
        WriteResult("{\"run\":\"20261118T201500Z\",\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\""
            + reason + "\"}\n");

        await watcher.Scan();
        await watcher.Scan();

        Assert.Equal(
            "{\"kind\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"" + reason + "\"}",
            Assert.Single(Payloads()));
    }

    [Fact]
    public async Task ANewRun_IsSentAgain()
    {
        WriteResult(UpdatedJson);
        await watcher.Scan();

        WriteResult("{\"run\":\"20261119T021500Z\",\"result\":\"update_failed\",\"version\":\"1.4.0\",\"reason\":\"no_answer\"}\n");
        await watcher.Scan();

        Assert.Equal(2, Payloads().Count);
        Assert.Equal("20261119T021500Z", store.Settings().UpdateResultNotified);
    }

    [Fact]
    public async Task WithNotifyUpdateOff_NothingIsSentButTheRunIsStored()
    {
        store.SetFlags(null, false);
        WriteResult(UpdatedJson);

        await watcher.Scan();
        store.SetFlags(null, true);
        await watcher.Scan();

        Assert.Empty(handler.Requests);
        Assert.Equal("20261118T201500Z", store.Settings().UpdateResultNotified);
    }

    [Fact]
    public async Task AnInvalidResult_IsIgnored()
    {
        WriteResult("{\"run\":\"20261118T201500Z\",\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"<script>\"}");

        await watcher.Scan();

        Assert.Empty(handler.Requests);
        Assert.Null(store.Settings().UpdateResultNotified);
    }

    private UpdateResultReader Reader()
    {
        return new UpdateResultReader(store.Folder, UnixOwner.CurrentUser());
    }

    private void WriteResult(string json)
    {
        TempTokenFolder.WritePrivateText(ResultPath, json);
    }

    private IReadOnlyList<string> Payloads()
    {
        return handler.Requests.Select(request => receiver.DecryptText(request.Body)).ToList();
    }
}
