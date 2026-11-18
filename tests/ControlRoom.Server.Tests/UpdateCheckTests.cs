using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace ControlRoom.Server.Tests;

/// <summary>The update check: <see cref="SemVer"/> precedence, the one fixed GET, what a response must hold to
/// count, the 6 h timer and the <c>update</c> push once per version. <see cref="UpdateChecker.Check"/> is called
/// directly against <see cref="FakeUpdateHandler"/>; nothing reaches the network.</summary>
public sealed partial class UpdateCheckTests : IDisposable
{
    private const string Prefix = "https://github.com/nothisisnotdaniel/controlroom/releases/tag/";

    private readonly TempTokenFolder folder = new();

    private readonly FakeUpdateHandler github = new();

    private readonly FakePushHandler push = new();

    private readonly LogCapture logs = new();

    private readonly PushReceiver receiver = new();

    private readonly PushStore store;

    private readonly UpdateSettingsStore settings;

    public UpdateCheckTests()
    {
        store = new PushStore(Path.Combine(folder.Path, "push"));
        settings = new UpdateSettingsStore(Path.Combine(folder.Path, "push"), UnixOwner.CurrentUser());
        Assert.True(store.Subscribe(new PushSubscription(
            PushTestData.AppleEndpoint, Base64Url.Encode(receiver.PublicKey), Base64Url.Encode(receiver.Auth))));
    }

    public void Dispose()
    {
        receiver.Dispose();
        folder.Dispose();
    }

    [Theory]
    [InlineData("1.2.0", "1.1.9")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.2")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    [InlineData("1.0.0-beta", "1.0.0-alpha.beta")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11")]
    [InlineData("100000000000000000000.0.0", "99999999999999999999.0.0")]
    [InlineData("1.2.4-develop.1", "1.2.3")]
    [InlineData("1.2.4", "1.2.4-develop.9")]
    [InlineData("1.2.4-develop.10", "1.2.4-develop.9")]
    [InlineData("1.2.5-develop.1", "1.2.4-develop.99")]
    public void SemVer_OrdersByPrecedence(string higher, string lower)
    {
        SemVer high = SemVer.Parse(higher)!;
        SemVer low = SemVer.Parse(lower)!;

        Assert.True(high.IsNewerThan(low));
        Assert.False(low.IsNewerThan(high));
        Assert.True(high.CompareTo(low) > 0);
        Assert.True(low.CompareTo(high) < 0);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("0.0.0")]
    [InlineData("1.0.0-rc.1")]
    [InlineData("1.0.0-0A.is.legal")]
    public void SemVer_EqualVersions_AreNotNewer(string text)
    {
        Assert.Equal(0, SemVer.Parse(text)!.CompareTo(SemVer.Parse(text)));
        Assert.False(SemVer.Parse(text)!.IsNewerThan(SemVer.Parse(text)!));
        Assert.Equal(text, SemVer.Parse(text)!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("1.2.3+build")]
    [InlineData("1.2.3-rc.1+build.5")]
    [InlineData("1.2.3\n")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3-rc_1")]
    [InlineData("١.2.3")]
    public void SemVer_RejectsInvalidInput(string? text)
    {
        Assert.Null(SemVer.Parse(text));
    }

    [Fact]
    public async Task Check_IsOneFixedGetWithoutCredentials()
    {
        github.RespondWithTag("v1.2.0");
        UpdateChecker checker = Checker("1.0.0", out _);

        await checker.Check();

        UpdateRequest request = Assert.Single(github.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/nothisisnotdaniel/controlroom/releases/latest", request.Uri.AbsoluteUri);
        Assert.Equal("application/vnd.github+json", request.Headers["Accept"]);
        Assert.Equal("ControlRoom", request.Headers["User-Agent"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.False(request.Headers.ContainsKey("Cookie"));
    }

    [Fact]
    public async Task DevelopChannel_IsOneFixedListGetWithoutCredentials()
    {
        settings.Set(null, UpdateChannel.Develop);
        RespondWithList("[{\"tag_name\":\"v1.2.0\"}]");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);

        await checker.Check();

        UpdateRequest request = Assert.Single(github.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/nothisisnotdaniel/controlroom/releases?per_page=100", request.Uri.AbsoluteUri);
        Assert.Equal("application/vnd.github+json", request.Headers["Accept"]);
        Assert.Equal("ControlRoom", request.Headers["User-Agent"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.False(request.Headers.ContainsKey("Cookie"));
        Assert.Equal(UpdateChannel.Develop, state.Latest?.Channel);
    }

    [Fact]
    public async Task StableChannel_StoredOrRefused_AsksForTheLatestRelease()
    {
        settings.Set(true, UpdateChannel.Stable);
        github.RespondWithTag("v1.2.0");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);
        await checker.Check();
        File.WriteAllText(settings.Path, "{\"autoUpdate\":true,\"channel\":\"beta\"}");
        await checker.Check();

        Assert.Equal(2, github.Requests.Count);
        Assert.All(github.Requests, request => Assert.Equal(UpdateChecker.LatestUrl, request.Uri.AbsoluteUri));
        Assert.Equal(UpdateChannel.Stable, state.Latest?.Channel);
    }

    [Theory]
    [InlineData("[{\"tag_name\":\"v1.2.4-develop.9\"},{\"tag_name\":\"v1.2.4-develop.10\"},{\"tag_name\":\"v1.2.3\"}]", "1.2.4-develop.10")]
    [InlineData("[{\"tag_name\":\"v1.2.4-develop.9\"},{\"tag_name\":\"v1.2.4\"}]", "1.2.4")]
    [InlineData("[{\"tag_name\":\"v1.2.3\"},{\"tag_name\":\"v1.2.4-develop.1\"}]", "1.2.4-develop.1")]
    [InlineData("[{\"tag_name\":\"v9.0.0\",\"draft\":true},{\"tag_name\":\"v1.2.4-develop.1\",\"draft\":false}]", "1.2.4-develop.1")]
    [InlineData("[{\"tag_name\":\"v9.0.0-rc.1\"},{\"tag_name\":\"v9.0.0+b\"},{\"tag_name\":\"9.0.0\"},"
        + "{\"tag_name\":\"v9.0.0-develop.01\"},{\"tag_name\":\"v9.0.0-develop.0\"},{\"tag_name\":\"v09.0.0\"},"
        + "{\"tag_name\":\"v9.0.0-develop.1.2\"},{\"tag_name\":\"v9.0.0-Develop.1\"},{\"tag_name\":9},"
        + "{\"name\":\"v9.0.0\"},\"v9.0.0\",null,{\"tag_name\":\"v1.1.0\"}]", "1.1.0")]
    public async Task DevelopList_KeepsTheHighestValidNonDraftTag(string list, string expected)
    {
        settings.Set(null, UpdateChannel.Develop);
        RespondWithList(list);
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);

        await checker.Check();

        Assert.Equal(expected, state.Latest?.Version.ToString());
        Assert.Equal(Prefix + "v" + expected, state.Latest?.Url);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v9.0.0\"}")]
    [InlineData("[]")]
    [InlineData("[{\"tag_name\":\"v9.0.0\",\"draft\":true}]")]
    [InlineData("[{\"tag_name\":\"v9.0.0-rc.1\"}]")]
    [InlineData("[{\"tag_name\":\"v9.0.0\"} BODY-MARKER")]
    [InlineData("\"v9.0.0\"")]
    public async Task DevelopList_InvalidBody_KeepsTheLastState(string body)
    {
        settings.Set(null, UpdateChannel.Develop);
        RespondWithList("[{\"tag_name\":\"v1.2.0-develop.3\"}]");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);
        await checker.Check();
        UpdateRelease? before = state.Latest;
        RespondWithList(body);

        await checker.Check();

        Assert.NotNull(before);
        Assert.Same(before, state.Latest);
        AssertLogsHoldNoUrlOrBody();
    }

    [Fact]
    public async Task DevelopList_OverOneMebibyte_KeepsTheLastState()
    {
        settings.Set(null, UpdateChannel.Develop);
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);
        RespondWithList("[{\"tag_name\":\"v9.0.0\"}]".PadRight(UpdateChecker.MaxBodyBytes + 1));

        await checker.Check();

        Assert.Null(state.Latest);
    }

    [Theory]
    [InlineData("1.2.3", "v1.2.4-develop.1", true)]
    [InlineData("1.2.4-develop.9", "v1.2.4", true)]
    [InlineData("1.2.4-develop.9", "v1.2.4-develop.10", true)]
    [InlineData("1.2.4-develop.10", "v1.2.4-develop.9", false)]
    [InlineData("1.2.4", "v1.2.4-develop.9", false)]
    public async Task DevelopResult_IsAvailableOnlyWhenNewerAndOnTheSameChannel(string current, string tag, bool available)
    {
        settings.Set(null, UpdateChannel.Develop);
        RespondWithList("[{\"tag_name\":\"" + tag + "\"}]");
        UpdateChecker checker = Checker(current, out UpdateState state);

        await checker.Check();

        UpdateResponse develop = UpdateEndpoint.Describe(state.Latest, current, null, UpdateChannel.Develop);
        Assert.Equal(available, develop.Available);
        if (available)
        {
            Assert.Equal("scripts/launch-agent.sh install --release " + tag, develop.Command);
        }

        Assert.False(UpdateEndpoint.Describe(state.Latest, current, null, UpdateChannel.Stable).Available);
        Assert.Equal(available ? 1 : 0, push.Requests.Count);
    }

    [Fact]
    public async Task DevelopBuild_SendsTheUpdatePushOncePerVersion()
    {
        settings.Set(null, UpdateChannel.Develop);
        RespondWithList("[{\"tag_name\":\"v1.0.1-develop.4\"}]");
        UpdateChecker checker = Checker("1.0.0", out _);

        await checker.Check();
        await checker.Check();

        PushRequest request = Assert.Single(push.Requests);
        Assert.Equal("{\"kind\":\"update\",\"version\":\"1.0.1-develop.4\",\"reason\":\"Update available\"}",
            receiver.DecryptText(request.Body));
    }

    [Fact]
    public void DefaultTransport_HasNoRedirectsCookiesOrProxyAndATenSecondTimeout()
    {
        UpdateTransport transport = UpdateTransport.CreateDefault();

        Assert.Equal(TimeSpan.FromSeconds(10), transport.Timeout);
        SocketsHttpHandler handler = Assert.IsType<SocketsHttpHandler>(transport.Handler);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
    }

    [Fact]
    public async Task ValidTag_KeepsOnlyTheVersionAndARebuiltUrl()
    {
        github.RespondWithTag("v1.2.0");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);

        await checker.Check();

        Assert.NotNull(state.Latest);
        Assert.Equal("1.2.0", state.Latest.Version.ToString());
        Assert.Equal(Prefix + "v1.2.0", state.Latest.Url);
    }

    [Fact]
    public async Task BodyOfExactlyOneMebibyte_Counts()
    {
        string json = "{\"tag_name\":\"v1.3.0\"}";
        github.Respond = (_, _) => Task.FromResult(FakeUpdateHandler.Ok(json.PadRight(UpdateChecker.MaxBodyBytes)));
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);

        await checker.Check();

        Assert.Equal("1.3.0", state.Latest?.Version.ToString());
    }

    public static TheoryData<string> BadResponses()
    {
        return
        [
            "500", "404", "403", "201", "301", "302", "not-json", "array", "no-tag", "tag-number", "tag-no-v", "tag-build",
            "tag-leading-zero", "tag-empty", "too-big", "too-big-lying-length", "network",
        ];
    }

    [Theory]
    [MemberData(nameof(BadResponses))]
    public async Task BadResponse_KeepsTheLastKnownStateAndLogsNoUrlOrBody(string kind)
    {
        github.RespondWithTag("v1.2.0");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);
        await checker.Check();
        UpdateRelease? before = state.Latest;
        github.Respond = (_, _) => kind == "network"
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("BODY-MARKER"))
            : Task.FromResult(Bad(kind));

        await checker.Check();

        Assert.Same(before, state.Latest);
        Assert.Equal(2, github.Requests.Count);
        AssertLogsHoldNoUrlOrBody();
    }

    [Fact]
    public async Task Redirect_IsNotFollowed()
    {
        github.Respond = (_, _) => Task.FromResult(Bad("302"));
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);

        await checker.Check();

        Assert.Single(github.Requests);
        Assert.Null(state.Latest);
        Assert.Contains(logs.Lines, line => line.Contains("Status=302", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hang_IsCancelledByTheTimeoutAndKeepsTheState()
    {
        github.Respond = async (_, cancellation) =>
        {
            await Task.Delay(Timeout.Infinite, cancellation);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        UpdateChecker checker = Checker("1.0.0", out UpdateState state, TimeSpan.FromMilliseconds(50));

        await checker.Check();

        Assert.Null(state.Latest);
        Assert.Contains(logs.Lines, line => line.StartsWith("Update check timed out |", StringComparison.Ordinal));
        AssertLogsHoldNoUrlOrBody();
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("1.0")]
    [InlineData("")]
    [InlineData("1.0.0+abc")]
    public async Task DevOrInvalidRunningVersion_MakesNoRequestAndReportsNothing(string current)
    {
        github.RespondWithTag("v9.0.0");
        UpdateChecker checker = Checker(current, out UpdateState state);

        await checker.Check();

        Assert.Empty(github.Requests);
        Assert.Null(state.Latest);
        Assert.False(UpdateEndpoint.Describe(state.Latest, current, null).Available);
        Assert.Empty(push.Requests);
    }

    [Fact]
    public async Task CheckSafely_NeverThrowsAndLogsAFixedText()
    {
        github.Respond = (_, _) => throw new InvalidOperationException("BODY-MARKER");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);

        await checker.CheckSafely();

        Assert.Null(state.Latest);
        Assert.Contains(logs.Lines, line => line.StartsWith("Update check failed |", StringComparison.Ordinal));
        AssertLogsHoldNoUrlOrBody();
    }

    [Fact]
    public async Task HostedChecker_ChecksOnceAtStartAndSetsASixHourTimer()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.0.0" };
        factory.Update.RespondWithTag("v1.0.0");
        using HttpClient client = factory.CreateClient();

        await factory.Update.WaitForRequests(1);

        Assert.Single(factory.Update.Requests);
        Assert.Contains((TimeSpan.FromHours(6), TimeSpan.FromHours(6)), factory.Time.Timers);
    }

    [Fact]
    public async Task HostedChecker_FailedChecksKeepTheServerRunning()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.0.0" };
        factory.Update.Respond = (_, _) => throw new InvalidOperationException("broken");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Update.WaitForRequests(1);

        await factory.Services.GetRequiredService<UpdateChecker>().CheckSafely();

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NewVersion_SendsOneNormalUpdatePushAndStoresIt()
    {
        github.RespondWithTag("v1.2.0");
        UpdateChecker checker = Checker("1.0.0", out _);

        await checker.Check();
        await checker.Check();

        PushRequest request = Assert.Single(push.Requests);
        Assert.Equal("normal", request.Headers["Urgency"]);
        Assert.Equal("{\"kind\":\"update\",\"version\":\"1.2.0\",\"reason\":\"Update available\"}",
            receiver.DecryptText(request.Body));
        Assert.Equal("1.2.0", store.Settings().UpdateNotified);

        github.RespondWithTag("v1.3.0-rc.1");
        await checker.Check();
        Assert.Equal(2, push.Requests.Count);
        Assert.Equal("{\"kind\":\"update\",\"version\":\"1.3.0-rc.1\",\"reason\":\"Update available\"}",
            receiver.DecryptText(push.Requests[1].Body));
    }

    [Fact]
    public async Task SameOrOlderRelease_SendsNothing()
    {
        github.RespondWithTag("v1.0.0");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);
        await checker.Check();
        github.RespondWithTag("v0.9.0");
        await checker.Check();

        Assert.Empty(push.Requests);
        Assert.Null(store.Settings().UpdateNotified);
        Assert.False(UpdateEndpoint.Describe(state.Latest, "1.0.0", null).Available);
    }

    [Fact]
    public async Task NotifyUpdateOff_SendsNothingButStoresTheVersion()
    {
        store.SetFlags(null, false);
        github.RespondWithTag("v1.2.0");
        UpdateChecker checker = Checker("1.0.0", out _);

        await checker.Check();
        store.SetFlags(null, true);
        await checker.Check();

        Assert.Empty(push.Requests);
        Assert.Equal("1.2.0", store.Settings().UpdateNotified);
    }

    [Fact]
    public async Task RefusedPushFolder_SendsAndStoresNothingButTheBannerStillWorks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        byte[] before = File.ReadAllBytes(store.SettingsPath);
        File.SetUnixFileMode(store.Folder, AccessToken.FolderMode | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        github.RespondWithTag("v1.2.0");
        UpdateChecker checker = Checker("1.0.0", out UpdateState state);

        try
        {
            await checker.Check();
        }
        finally
        {
            File.SetUnixFileMode(store.Folder, AccessToken.FolderMode);
        }

        Assert.Empty(push.Requests);
        Assert.Equal(before, File.ReadAllBytes(store.SettingsPath));
        Assert.True(UpdateEndpoint.Describe(state.Latest, "1.0.0", null).Available);
    }

    [Fact]
    public async Task NewServerOnTheSamePushFile_DoesNotSendTheSameVersionAgain()
    {
        using TempFactoryRoot root = new();
        using TempTokenFolder tokens = new();
        using PushReceiver device = new();
        PushStore shared = new(Path.GetDirectoryName(tokens.TokenFile)!);
        Assert.True(shared.Subscribe(new PushSubscription(
            PushTestData.AppleEndpoint, Base64Url.Encode(device.PublicKey), Base64Url.Encode(device.Auth))));
        List<PushRequest> sent = [];
        using (ServerFactory first = new(root.Path, tokens.TokenFile) { CurrentVersion = "1.0.0" })
        {
            first.Update.RespondWithTag("v1.2.0");
            using HttpClient client = first.CreateClient();
            UpdateChecker checker = first.Services.GetRequiredService<UpdateChecker>();
            await checker.Check();
            await checker.Check();
            sent.AddRange(first.Push.Requests);
        }

        using (ServerFactory second = new(root.Path, tokens.TokenFile) { CurrentVersion = "1.0.0" })
        {
            second.Update.RespondWithTag("v1.2.0");
            using HttpClient client = second.CreateClient();
            await second.Services.GetRequiredService<UpdateChecker>().Check();
            Assert.Equal(2, second.Update.Requests.Count);
            sent.AddRange(second.Push.Requests);
        }

        PushRequest only = Assert.Single(sent);
        Assert.Equal("{\"kind\":\"update\",\"version\":\"1.2.0\",\"reason\":\"Update available\"}",
            device.DecryptText(only.Body));
    }

    [Fact]
    public void RepoSlug_MatchesTheInstallerAndClaudeMdNamesTheHostAndField()
    {
        string script = File.ReadAllText(Path.Combine(RepoFolder(), "scripts", "launch-agent.sh"));
        Match releases = ReleasesLine().Match(script);
        Assert.True(releases.Success);
        Assert.Equal(UpdateChecker.RepoSlug, releases.Groups[1].Value);
        Assert.Equal("https://api.github.com/repos/" + UpdateChecker.RepoSlug + "/releases/latest", UpdateChecker.LatestUrl);
        Assert.Equal("https://github.com/" + UpdateChecker.RepoSlug + "/releases/tag/", UpdateChecker.ReleasePrefix);
        Assert.Equal("https://api.github.com/repos/" + UpdateChecker.RepoSlug + "/releases?per_page=100", UpdateChecker.ListUrl);

        string claude = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int outbound = claude.IndexOf("  - The only outbound calls are", StringComparison.Ordinal);
        Assert.True(outbound >= 0);
        int end = claude.IndexOf("\n- **", outbound, StringComparison.Ordinal);
        string rule = Whitespace().Replace(claude[outbound..end], " ");
        Assert.Contains("api.github.com", rule, StringComparison.Ordinal);
        Assert.Contains("A payload holds only `kind`, `project`, `job`, `stage`, `version`,", rule, StringComparison.Ordinal);
        Assert.Contains("`UpdateCheck`", claude, StringComparison.Ordinal);
        Assert.Contains("`UpdateEndpoint`", claude, StringComparison.Ordinal);
    }

    private UpdateChecker Checker(string current, out UpdateState state, TimeSpan? timeout = null)
    {
        state = new UpdateState();
        PushCrypto crypto = new(new FakeTime(), PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, null);
        PushSender sender = new(store, crypto, new PushTransport(push), logs.For<PushSender>());
        UpdateTransport transport = timeout is null ? new UpdateTransport(github) : new UpdateTransport(github, timeout.Value);
        return new UpdateChecker(
            state, new RunningVersion(current), transport, settings, store, sender, new FakeTime(), logs.For<UpdateChecker>());
    }

    private void RespondWithList(string body)
    {
        github.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri == UpdateChecker.ListUrl
            ? FakeUpdateHandler.Ok(body)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private void AssertLogsHoldNoUrlOrBody()
    {
        foreach (string line in logs.Lines)
        {
            Assert.DoesNotContain("/repos/", line, StringComparison.Ordinal);
            Assert.DoesNotContain("api.github.com", line, StringComparison.Ordinal);
            Assert.DoesNotContain("BODY-MARKER", line, StringComparison.Ordinal);
            Assert.DoesNotContain("evil.example", line, StringComparison.Ordinal);
        }
    }

    private static HttpResponseMessage Bad(string kind)
    {
        switch (kind)
        {
            case "301":
            case "302":
                HttpResponseMessage redirect = new((HttpStatusCode)int.Parse(kind, System.Globalization.CultureInfo.InvariantCulture));
                redirect.Headers.Location = new Uri("https://evil.example/BODY-MARKER");
                redirect.Content = new StringContent("{\"tag_name\":\"v9.0.0\"}", Encoding.UTF8, "application/json");
                return redirect;
            case "not-json":
                return FakeUpdateHandler.Ok("{\"tag_name\":\"v9.0.0\" BODY-MARKER");
            case "array":
                return FakeUpdateHandler.Ok("[{\"tag_name\":\"v9.0.0\"}]");
            case "no-tag":
                return FakeUpdateHandler.Ok("{\"name\":\"v9.0.0\",\"body\":\"BODY-MARKER\"}");
            case "tag-number":
                return FakeUpdateHandler.Ok("{\"tag_name\":9}");
            case "tag-no-v":
                return FakeUpdateHandler.Ok("{\"tag_name\":\"9.0.0\"}");
            case "tag-build":
                return FakeUpdateHandler.Ok("{\"tag_name\":\"v9.0.0+BODY-MARKER\"}");
            case "tag-leading-zero":
                return FakeUpdateHandler.Ok("{\"tag_name\":\"v09.0.0\"}");
            case "tag-empty":
                return FakeUpdateHandler.Ok("{\"tag_name\":\"v\"}");
            case "too-big":
                return FakeUpdateHandler.Ok("{\"tag_name\":\"v9.0.0\"}".PadRight(UpdateChecker.MaxBodyBytes + 1));
            case "too-big-lying-length":
                HttpResponseMessage lying = FakeUpdateHandler.Ok("{\"tag_name\":\"v9.0.0\"}".PadRight(UpdateChecker.MaxBodyBytes + 1));
                lying.Content.Headers.ContentLength = 20;
                return lying;
            default:
                HttpResponseMessage response = new((HttpStatusCode)int.Parse(kind, System.Globalization.CultureInfo.InvariantCulture));
                response.Content = new StringContent("{\"tag_name\":\"v9.0.0\",\"body\":\"BODY-MARKER\"}", Encoding.UTF8, "application/json");
                return response;
        }
    }

    private static string RepoFolder()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ControlRoom.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current.FullName;
    }

    [GeneratedRegex("^RELEASES=\"https://github\\.com/([^/\"]+/[^/\"]+)/releases\"$", RegexOptions.Multiline)]
    private static partial Regex ReleasesLine();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
