using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The watcher: <see cref="PushWatcher.Scan"/> is called directly (no timer, no waiting); each test changes
/// <c>state.json</c> and <c>backlog.md</c> between scans and decrypts what the fake push service got.</summary>
public sealed class PushWatcherTests : IDisposable
{
    private const string Marker = "SECRET-MARKER-7f3a";

    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "kind", "project", "job", "stage", "reason", "count",
    };

    private readonly TempFactoryRoot root = new();

    private readonly TempTokenFolder folder = new();

    private readonly PushStore store;

    private readonly FakePushHandler handler = new();

    private readonly PushReceiver receiver = new();

    private readonly PushWatcher watcher;

    public PushWatcherTests()
    {
        root.AddProject("Times", "- [ ] [#1](backlog/001-a.md) Open one\n- [?] [#2](backlog/002-b.md) Proposal\n");
        store = new PushStore(System.IO.Path.Combine(folder.Path, "push"));
        Assert.True(store.Subscribe(new PushSubscription(
            PushTestData.AppleEndpoint, Base64Url.Encode(receiver.PublicKey), Base64Url.Encode(receiver.Auth))));
        LogCapture logs = new();
        PushCrypto crypto = new(new FakeTime(), PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, null);
        PushSender sender = new(store, crypto, new PushTransport(handler), logs.For<PushSender>());
        watcher = new PushWatcher(new FactoryRoot(root.Path), store, sender, new FakeTime(), logs.For<PushWatcher>());
    }

    public void Dispose()
    {
        watcher.Dispose();
        receiver.Dispose();
        folder.Dispose();
        root.Dispose();
    }

    [Fact]
    public async Task Baseline_SendsNothingForWhatAlreadyExists()
    {
        store.SetNotifyDone(true);
        root.AddJob("1-wait", State("needs-human", "merge conflict in x"));
        root.AddJob("2-ci", State("done", null, "failure"));

        await watcher.Scan();
        await watcher.Scan();

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NeedsHuman_IsSentOnceWithOnlyFixedFields()
    {
        root.AddJob("5-feature", State("build", null));
        await watcher.Scan();
        root.AddJob("5-feature", State("needs-human", "Blocked: " + Marker));

        await watcher.Scan();
        await watcher.Scan();

        Assert.Equal(
            "{\"kind\":\"needs-human\",\"project\":\"Times\",\"job\":\"5-feature\",\"stage\":\"needs-human\",\"reason\":\"Needs you\"}",
            Assert.Single(Payloads()));
    }

    [Fact]
    public async Task NeedsHumanWithMergeConflictNote_IsSentAsMergeConflictWithoutTheNote()
    {
        root.AddJob("6-merge", State("approve", null));
        await watcher.Scan();
        root.AddJob("6-merge", State("needs-human", "merge conflict in backlog.md <b>" + Marker + "</b>"));

        await watcher.Scan();

        string payload = Assert.Single(Payloads());
        Assert.Equal(
            "{\"kind\":\"merge-conflict\",\"project\":\"Times\",\"job\":\"6-merge\",\"stage\":\"needs-human\",\"reason\":\"Merge conflict\"}",
            payload);
        Assert.DoesNotContain(Marker, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CiFailure_IsSentOnce()
    {
        root.AddJob("7-ci", State("done", null, "pending"));
        await watcher.Scan();
        root.AddJob("7-ci", State("done", null, "failure"));

        await watcher.Scan();
        await watcher.Scan();

        Assert.Equal(
            "{\"kind\":\"ci-failed\",\"project\":\"Times\",\"job\":\"7-ci\",\"stage\":\"done\",\"reason\":\"CI failed after merge\"}",
            Assert.Single(Payloads()));
    }

    [Fact]
    public async Task Done_IsSentOnlyWithNotifyDone()
    {
        root.AddJob("8-a", State("approve", null));
        root.AddJob("9-b", State("approve", null));
        await watcher.Scan();
        root.AddJob("8-a", State("done", null));
        await watcher.Scan();
        Assert.Empty(handler.Requests);

        store.SetNotifyDone(true);
        root.AddJob("9-b", State("done", null));
        await watcher.Scan();
        await watcher.Scan();

        Assert.Equal(
            "{\"kind\":\"done\",\"project\":\"Times\",\"job\":\"9-b\",\"stage\":\"done\",\"reason\":\"Done\"}",
            Assert.Single(Payloads()));
    }

    [Fact]
    public async Task NewApprovalLines_SendOneApprovalWithTheCount()
    {
        await watcher.Scan();
        WriteBacklog(
            "- [ ] [#1](backlog/001-a.md) Open one\n- [?] [#2](backlog/002-b.md) Proposal\n"
            + "- [?] [#3](backlog/003-c.md) " + Marker + " new\n- [?] Old style: " + Marker + "\n");

        await watcher.Scan();
        await watcher.Scan();

        string payload = Assert.Single(Payloads());
        Assert.Equal(
            "{\"kind\":\"approval\",\"project\":\"Times\",\"reason\":\"New items to approve\",\"count\":2}", payload);
        Assert.DoesNotContain(Marker, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovalLineRemovedAndAddedAgain_IsNotSentTwice()
    {
        await watcher.Scan();
        WriteBacklog("- [ ] [#1](backlog/001-a.md) Open one\n- [?] [#3](backlog/003-c.md) New\n");
        await watcher.Scan();
        WriteBacklog("- [ ] [#1](backlog/001-a.md) Open one\n");
        await watcher.Scan();
        WriteBacklog("- [ ] [#1](backlog/001-a.md) Open one\n- [?] [#3](backlog/003-c.md) New\n");

        await watcher.Scan();

        Assert.Single(Payloads());
    }

    [Fact]
    public async Task JobAppearingAfterBaseline_IsSentAndUnknownProjectOrStageIsLeftOut()
    {
        await watcher.Scan();
        root.AddJob("10-new", "{\"project\":\"" + Marker + "\",\"stage\":\"needs-human\",\"title\":\"" + Marker + "\"}");
        root.AddJob("11-odd", "{\"project\":\"Times\",\"stage\":\"" + Marker + "\",\"ci\":{\"status\":\"failure\"}}");

        await watcher.Scan();

        IReadOnlyList<string> payloads = Payloads();
        Assert.Equal(2, payloads.Count);
        Assert.Contains("{\"kind\":\"needs-human\",\"job\":\"10-new\",\"stage\":\"needs-human\",\"reason\":\"Needs you\"}", payloads);
        Assert.Contains("{\"kind\":\"ci-failed\",\"project\":\"Times\",\"job\":\"11-odd\",\"reason\":\"CI failed after merge\"}", payloads);
        Assert.All(payloads, payload => Assert.DoesNotContain(Marker, payload, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreadableStateInBetween_DoesNotSendAgain()
    {
        root.AddJob("12-x", State("build", null));
        await watcher.Scan();
        root.AddJob("12-x", State("needs-human", null));
        await watcher.Scan();
        File.WriteAllText(Path.Combine(root.JobsPath, "12-x", "state.json"), "not json");
        await watcher.Scan();
        root.AddJob("12-x", State("needs-human", null));

        await watcher.Scan();

        Assert.Single(Payloads());
    }

    [Fact]
    public async Task NeedsHumanAgainAfterMovingOn_IsANewEvent()
    {
        root.AddJob("13-y", State("build", null));
        await watcher.Scan();
        root.AddJob("13-y", State("needs-human", null));
        await watcher.Scan();
        root.AddJob("13-y", State("build", null));
        await watcher.Scan();
        root.AddJob("13-y", State("needs-human", null));

        await watcher.Scan();

        Assert.Equal(2, Payloads().Count);
    }

    [Fact]
    public async Task EveryPayload_HasOnlyTheAllowedFieldsAndAFixedReason()
    {
        store.SetNotifyDone(true);
        root.AddJob("14-a", State("build", null));
        root.AddJob("15-b", State("build", null));
        root.AddJob("16-c", State("review", null));
        await watcher.Scan();
        root.AddJob("14-a", State("needs-human", "merge conflict " + Marker));
        root.AddJob("15-b", State("needs-human", Marker));
        root.AddJob("16-c", State("done", Marker, "failure"));
        WriteBacklog("- [?] [#9](backlog/009-z.md) " + Marker + "\n");

        await watcher.Scan();

        IReadOnlyList<string> payloads = Payloads();
        Assert.Equal(5, payloads.Count);
        foreach (string payload in payloads)
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            string kind = document.RootElement.GetProperty("kind").GetString()!;
            Assert.All(document.RootElement.EnumerateObject(), field => Assert.Contains(field.Name, AllowedFields));
            Assert.Equal(PushPayload.Reasons[kind], document.RootElement.GetProperty("reason").GetString());
            Assert.Equal(kind == PushPayload.Approval, document.RootElement.TryGetProperty("count", out _));
            Assert.DoesNotContain(Marker, payload, StringComparison.Ordinal);
        }

        Assert.Equal(
            new[] { "approval", "ci-failed", "done", "merge-conflict", "needs-human" },
            payloads.Select(payload => JsonDocument.Parse(payload).RootElement.GetProperty("kind").GetString())
                .Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ReasonTable_IsFixed()
    {
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["needs-human"] = "Needs you",
                ["merge-conflict"] = "Merge conflict",
                ["ci-failed"] = "CI failed after merge",
                ["done"] = "Done",
                ["approval"] = "New items to approve",
                ["test"] = "Test notification",
                ["update"] = "Update available",
                ["updated"] = "Updated",
            },
            PushPayload.Reasons.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private IReadOnlyList<string> Payloads()
    {
        return handler.Requests.Select(request => receiver.DecryptText(request.Body)).ToList();
    }

    private void WriteBacklog(string text)
    {
        File.WriteAllText(Path.Combine(root.Path, "Times", "backlog.md"), text);
    }

    private static string State(string stage, string? note, string? ci = null)
    {
        string json = "{\"project\":\"Times\",\"title\":\"Title " + Marker + "\",\"stage\":\"" + stage + "\"";
        if (note is not null)
        {
            json += ",\"note\":" + JsonSerializer.Serialize(note);
        }

        if (ci is not null)
        {
            json += ",\"ci\":{\"status\":\"" + ci + "\"}";
        }

        return json + "}";
    }
}
