using System.Net;

namespace ControlRoom.Server.Tests;

/// <summary>The sender: header values, no redirects, which responses remove a subscription, and logs without
/// endpoints, keys or the token.</summary>
public sealed class PushSenderTests : IDisposable
{
    private readonly TempTokenFolder folder = new();

    private readonly PushStore store;

    private readonly FakePushHandler handler = new();

    private readonly LogCapture logs = new();

    private readonly PushReceiver receiver = new();

    private readonly PushSender sender;

    public PushSenderTests()
    {
        store = new PushStore(System.IO.Path.Combine(folder.Path, "push"));
        PushCrypto crypto = new(new FakeTime(), PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, null);
        sender = new PushSender(store, crypto, new PushTransport(handler), logs.For<PushSender>());
        Assert.True(store.Subscribe(new PushSubscription(
            PushTestData.AppleEndpoint, Base64Url.Encode(receiver.PublicKey), Base64Url.Encode(receiver.Auth))));
    }

    public void Dispose()
    {
        receiver.Dispose();
        folder.Dispose();
    }

    [Theory]
    [InlineData(PushPayload.NeedsHuman, "high")]
    [InlineData(PushPayload.MergeConflict, "high")]
    [InlineData(PushPayload.CiFailed, "high")]
    [InlineData(PushPayload.Done, "normal")]
    [InlineData(PushPayload.Approval, "normal")]
    [InlineData(PushPayload.Test, "normal")]
    public async Task Send_UsesTheFixedHeaders(string kind, string urgency)
    {
        int sent = await sender.Send(new PushPayload(kind, null, null, null, 1), CancellationToken.None);

        Assert.Equal(1, sent);
        PushRequest request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(PushTestData.AppleEndpoint), request.Uri);
        Assert.Equal("86400", request.Headers["TTL"]);
        Assert.Equal(urgency, request.Headers["Urgency"]);
        Assert.Equal("aes128gcm", request.Headers["Content-Encoding"]);
        Assert.StartsWith("vapid t=", request.Headers["Authorization"], StringComparison.Ordinal);
        Assert.Contains(", k=" + Base64Url.Encode(store.Keys().PublicKey), request.Headers["Authorization"], StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"" + kind + "\"", receiver.DecryptText(request.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_DoesNotFollowARedirectAndKeepsTheSubscription()
    {
        handler.Respond = _ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("http://127.0.0.1/steal");
            return response;
        };

        int sent = await sender.Send(new PushPayload(PushPayload.Test, null, null, null), CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Single(handler.Requests);
        Assert.Single(store.Settings().Subscriptions);
    }

    [Fact]
    public void DefaultTransport_HasNoRedirectsAndATenSecondTimeout()
    {
        PushTransport transport = PushTransport.CreateDefault();

        SocketsHttpHandler sockets = Assert.IsType<SocketsHttpHandler>(transport.Handler);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseCookies);
        Assert.False(sockets.UseProxy);
        Assert.Equal(TimeSpan.FromSeconds(10), PushSender.Timeout);
        sockets.Dispose();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task Send_404Or410_RemovesTheSubscription(HttpStatusCode status)
    {
        handler.Respond = _ => new HttpResponseMessage(status);

        await sender.Send(new PushPayload(PushPayload.Test, null, null, null), CancellationToken.None);

        Assert.Empty(store.Settings().Subscriptions);
        AssertLogsAreClean(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Send_OtherErrors_KeepTheSubscription(HttpStatusCode status)
    {
        handler.Respond = _ => new HttpResponseMessage(status);

        int sent = await sender.Send(new PushPayload(PushPayload.Test, null, null, null), CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Single(store.Settings().Subscriptions);
        AssertLogsAreClean(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Send_Timeout_KeepsTheSubscription()
    {
        handler.Respond = _ => throw new TaskCanceledException("timeout " + PushTestData.AppleEndpoint, new TimeoutException());

        int sent = await sender.Send(new PushPayload(PushPayload.Test, null, null, null), CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Single(store.Settings().Subscriptions);
        AssertLogsAreClean("timed out");
    }

    [Fact]
    public async Task Send_NetworkError_KeepsTheSubscription()
    {
        handler.Respond = _ => throw new HttpRequestException("no route to " + PushTestData.AppleEndpoint);

        int sent = await sender.Send(new PushPayload(PushPayload.Test, null, null, null), CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Single(store.Settings().Subscriptions);
        AssertLogsAreClean("failed");
    }

    [Fact]
    public async Task ServerLogs_NeverHoldTheEndpointKeysOrToken()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver browser = new();
        factory.Push.Respond = _ => new HttpResponseMessage(HttpStatusCode.Gone);
        (await PushTestData.Subscribe(client, browser, PushTestData.AppleEndpoint)).Dispose();

        (await client.PostAsync(PushEndpoint.TestRoute, null)).Dispose();

        string all = string.Join("\n", factory.Logs.Lines);
        Assert.Contains("410", all, StringComparison.Ordinal);
        Assert.DoesNotContain("QGuQyavXutnMH", all, StringComparison.Ordinal);
        Assert.DoesNotContain(Base64Url.Encode(browser.PublicKey), all, StringComparison.Ordinal);
        Assert.DoesNotContain(Base64Url.Encode(browser.Auth), all, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.ReadToken(), all, StringComparison.Ordinal);
        Assert.Equal(0, (await PushTestData.Info(client)).Subscribed);
    }

    private void AssertLogsAreClean(string expected)
    {
        string all = string.Join("\n", logs.Lines);
        Assert.Contains(expected, all, StringComparison.Ordinal);
        Assert.DoesNotContain("QGuQyavXutnMH", all, StringComparison.Ordinal);
        Assert.DoesNotContain("apple.com", all, StringComparison.Ordinal);
        Assert.DoesNotContain(Base64Url.Encode(receiver.PublicKey), all, StringComparison.Ordinal);
        Assert.DoesNotContain(Base64Url.Encode(receiver.Auth), all, StringComparison.Ordinal);
        Assert.DoesNotContain(Base64Url.Encode(store.Keys().PrivateKey), all, StringComparison.Ordinal);
    }
}
