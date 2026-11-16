using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ControlRoom.Server.Tests;

/// <summary>The push routes: subscription checks, replace and remove, the done setting, the test send and the VAPID
/// header the server signs.</summary>
public sealed class PushApiTests
{
    [Theory]
    [InlineData("https://web.push.apple.com/QGuQyavXutnMH")]
    [InlineData("https://api.push.apple.com/x")]
    [InlineData("https://web.push.apple.com:443/x")]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc:def")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAA")]
    public async Task Subscribe_AllowedEndpoint_IsStored(string endpoint)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver receiver = new();

        using HttpResponseMessage response = await PushTestData.Subscribe(client, receiver, endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await PushTestData.Info(client)).Subscribed);
        string stored = File.ReadAllText(Path.Combine(Path.GetDirectoryName(factory.TokenFile)!, PushStore.SettingsFileName));
        Assert.DoesNotContain("expirationTime", stored, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://web.push.apple.com/x")]
    [InlineData("https://web.push.apple.com:8443/x")]
    [InlineData("https://user:pw@web.push.apple.com/x")]
    [InlineData("https://user@fcm.googleapis.com/x")]
    [InlineData("https://push.apple.com/x")]
    [InlineData("https://web.push.apple.com.evil.example/x")]
    [InlineData("https://evilpush.apple.com/x")]
    [InlineData("https://fcm.googleapis.com.evil.example/x")]
    [InlineData("https://x.fcm.googleapis.com/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://localhost/x")]
    [InlineData("wss://web.push.apple.com/x")]
    [InlineData("/relative")]
    [InlineData("")]
    public async Task Subscribe_OtherEndpoint_IsRefused(string endpoint)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver receiver = new();

        using HttpResponseMessage response = await PushTestData.Subscribe(client, receiver, endpoint);

        await AssertInvalid(response);
        Assert.Equal(0, (await PushTestData.Info(client)).Subscribed);
    }

    public static TheoryData<string> BadBodies()
    {
        using PushReceiver receiver = new();
        string key = Base64Url.Encode(receiver.PublicKey);
        string auth = Base64Url.Encode(receiver.Auth);
        byte[] offCurve = (byte[])receiver.PublicKey.Clone();
        offCurve[64] ^= 1;
        string endpoint = "\"endpoint\":\"" + PushTestData.AppleEndpoint + "\"";
        return
        [
            "{" + endpoint + "}",
            "{" + endpoint + ",\"keys\":{\"p256dh\":\"" + key + "\"}}",
            "{" + endpoint + ",\"keys\":{\"auth\":\"" + auth + "\"}}",
            "{" + endpoint + ",\"keys\":{\"p256dh\":\"" + key[..^2] + "\",\"auth\":\"" + auth + "\"}}",
            "{" + endpoint + ",\"keys\":{\"p256dh\":\"" + Base64Url.Encode(offCurve) + "\",\"auth\":\"" + auth + "\"}}",
            "{" + endpoint + ",\"keys\":{\"p256dh\":\"" + key + "\",\"auth\":\"" + Base64Url.Encode(new byte[15]) + "\"}}",
            "{" + endpoint + ",\"keys\":{\"p256dh\":\"" + key + "\",\"auth\":\"" + Base64Url.Encode(new byte[17]) + "\"}}",
            "{" + endpoint + ",\"keys\":{\"p256dh\":\"" + key + "\",\"auth\":\"a*b\"}}",
            "{" + endpoint + ",\"keys\":{\"p256dh\":5,\"auth\":\"" + auth + "\"}}",
            "{\"endpoint\":7,\"keys\":{\"p256dh\":\"" + key + "\",\"auth\":\"" + auth + "\"}}",
            "[]",
            "not json",
            "",
        ];
    }

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task Subscribe_BadBody_IsRefused(string body)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync(PushEndpoint.SubscribeRoute, PushTestData.Json(body));

        await AssertInvalid(response);
    }

    [Fact]
    public async Task Subscribe_BodyOver4KbOrNotJson_IsRefused()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver receiver = new();
        string json = receiver.SubscriptionJson(PushTestData.AppleEndpoint);
        string large = json[..^1] + ",\"pad\":\"" + new string('x', PushEndpoint.MaxBodyBytes) + "\"}";

        using HttpResponseMessage tooLarge = await client.PostAsync(PushEndpoint.SubscribeRoute, PushTestData.Json(large));
        using HttpResponseMessage plain = await client.PostAsync(
            PushEndpoint.SubscribeRoute, new StringContent(json, System.Text.Encoding.UTF8, "text/plain"));

        await AssertInvalid(tooLarge);
        await AssertInvalid(plain);
        Assert.Equal(0, (await PushTestData.Info(client)).Subscribed);
    }

    [Fact]
    public async Task Subscribe_ElevenEndpoints_RefusesTheEleventhButReplacesAStoredOne()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver receiver = new();
        for (int index = 0; index < PushStore.MaxSubscriptions; index++)
        {
            using HttpResponseMessage ok = await PushTestData.Subscribe(client, receiver, PushTestData.AppleEndpoint + index);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using HttpResponseMessage eleventh = await PushTestData.Subscribe(client, receiver, PushTestData.AppleEndpoint + "x");
        using HttpResponseMessage again = await PushTestData.Subscribe(client, receiver, PushTestData.AppleEndpoint + "3");

        Assert.Equal(HttpStatusCode.BadRequest, eleventh.StatusCode);
        Assert.Equal("{\"error\":\"too_many\"}", await eleventh.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(PushStore.MaxSubscriptions, (await PushTestData.Info(client)).Subscribed);
    }

    [Fact]
    public async Task Subscribe_SameEndpointAgain_ReplacesItAndUnsubscribeRemovesIt()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver first = new();
        using PushReceiver second = new();
        (await PushTestData.Subscribe(client, first, PushTestData.AppleEndpoint)).Dispose();
        (await PushTestData.Subscribe(client, second, PushTestData.AppleEndpoint)).Dispose();
        Assert.Equal(1, (await PushTestData.Info(client)).Subscribed);

        using HttpResponseMessage test = await client.PostAsync(PushEndpoint.TestRoute, null);
        Assert.Equal("{\"sent\":1}", await test.Content.ReadAsStringAsync());
        Assert.Contains("\"kind\":\"test\"", second.DecryptText(Assert.Single(factory.Push.Requests).Body), StringComparison.Ordinal);

        using HttpResponseMessage removed = await client.PostAsync(
            PushEndpoint.UnsubscribeRoute, PushTestData.Json("{\"endpoint\":\"" + PushTestData.AppleEndpoint + "\"}"));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(0, (await PushTestData.Info(client)).Subscribed);

        using HttpResponseMessage bad = await client.PostAsync(PushEndpoint.UnsubscribeRoute, PushTestData.Json("{}"));
        await AssertInvalid(bad);
    }

    [Fact]
    public async Task Settings_NotifyDone_PersistsAndShowsInGet()
    {
        using TempFactoryRoot root = new();
        using TempTokenFolder tokens = new();
        using (ServerFactory factory = new(root.Path, tokens.TokenFile))
        {
            using HttpClient client = factory.CreateTokenClient();
            using HttpResponseMessage response = await client.PostAsync(
                PushEndpoint.SettingsRoute, PushTestData.Json("{\"notifyDone\":true}"));
            Assert.Equal("{\"notifyDone\":true}", await response.Content.ReadAsStringAsync());
            Assert.True((await PushTestData.Info(client)).NotifyDone);
            using HttpResponseMessage bad = await client.PostAsync(
                PushEndpoint.SettingsRoute, PushTestData.Json("{\"notifyDone\":\"yes\"}"));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using ServerFactory again = new(root.Path, tokens.TokenFile);
        using HttpClient second = again.CreateTokenClient();
        Assert.True((await PushTestData.Info(second)).NotifyDone);
    }

    [Fact]
    public async Task Test_SendsOneTestPayloadToEachSubscription()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver apple = new();
        using PushReceiver google = new();
        (await PushTestData.Subscribe(client, apple, PushTestData.AppleEndpoint)).Dispose();
        (await PushTestData.Subscribe(client, google, "https://fcm.googleapis.com/fcm/send/abc")).Dispose();

        using HttpResponseMessage response = await client.PostAsync(PushEndpoint.TestRoute, null);

        Assert.Equal("{\"sent\":2}", await response.Content.ReadAsStringAsync());
        IReadOnlyList<PushRequest> requests = factory.Push.Requests;
        Assert.Equal(2, requests.Count);
        PushRequest toApple = requests.Single(request => request.Uri.Host == "web.push.apple.com");
        PushRequest toGoogle = requests.Single(request => request.Uri.Host == "fcm.googleapis.com");
        Assert.Equal("{\"kind\":\"test\",\"reason\":\"Test notification\"}", apple.DecryptText(toApple.Body));
        Assert.Equal("{\"kind\":\"test\",\"reason\":\"Test notification\"}", google.DecryptText(toGoogle.Body));
    }

    [Fact]
    public async Task Test_SecondCallInsideTenSeconds_Returns429AndSendsNothing()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver apple = new();
        (await PushTestData.Subscribe(client, apple, PushTestData.AppleEndpoint)).Dispose();

        using HttpResponseMessage first = await client.PostAsync(PushEndpoint.TestRoute, null);
        factory.Time.Now += TimeSpan.FromSeconds(9.999);
        using HttpResponseMessage tooSoon = await client.PostAsync(PushEndpoint.TestRoute, null);
        int sentBeforeWindowEnds = factory.Push.Requests.Count;
        factory.Time.Now += TimeSpan.FromMilliseconds(1);
        using HttpResponseMessage again = await client.PostAsync(PushEndpoint.TestRoute, null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((HttpStatusCode)429, tooSoon.StatusCode);
        Assert.Equal("{\"error\":\"too_many_requests\"}", await tooSoon.Content.ReadAsStringAsync());
        Assert.Equal(1, sentBeforeWindowEnds);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, factory.Push.Requests.Count);
    }

    [Fact]
    public async Task Test_VapidJwtVerifiesWithThePublicKeyFromGet()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver receiver = new();
        (await PushTestData.Subscribe(client, receiver, PushTestData.AppleEndpoint)).Dispose();
        string publicKey = (await PushTestData.Info(client)).PublicKey;

        (await client.PostAsync(PushEndpoint.TestRoute, null)).Dispose();

        string authorization = Assert.Single(factory.Push.Requests).Headers["Authorization"];
        string[] parts = authorization["vapid ".Length..].Split(", ");
        Assert.StartsWith("vapid t=", authorization, StringComparison.Ordinal);
        Assert.Equal("k=" + publicKey, parts[1]);
        JwtParts jwt = JwtParts.Parse(parts[0]["t=".Length..]);
        Assert.Equal("{\"typ\":\"JWT\",\"alg\":\"ES256\"}", jwt.Header);
        using JsonDocument claims = JsonDocument.Parse(jwt.Claims);
        Assert.Equal("https://web.push.apple.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(FakeTime.Start.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        byte[] point = Base64Url.Decode(publicKey)!;
        Assert.Equal(65, point.Length);
        Assert.True(jwt.Verifies(point));
    }

    [Theory]
    [InlineData("GET", PushEndpoint.SubscribeRoute)]
    [InlineData("GET", PushEndpoint.TestRoute)]
    [InlineData("POST", PushEndpoint.Route)]
    [InlineData("PUT", PushEndpoint.SubscribeRoute)]
    [InlineData("DELETE", PushEndpoint.UnsubscribeRoute)]
    [InlineData("POST", "/api/push/subscribe/")]
    [InlineData("POST", "/api/push/other")]
    public async Task OtherMethodsOrPaths_AreRefused(string method, string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
        Assert.Empty(factory.Push.Requests);
    }

    [Fact]
    public async Task EveryRoute_NeedsTheToken()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using PushReceiver receiver = new();

        foreach (Func<HttpClient, Task<HttpResponseMessage>> call in PushTestData.EveryRoute(receiver))
        {
            using HttpResponseMessage response = await call(client);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(factory.TokenFile)!, PushStore.KeysFileName)));
    }

    [Fact]
    public async Task HostedWatcher_RecordsTheBaselineAtStartAndSendsLaterEvents()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Times", "- [?] Old: one\n");
        root.AddJob("1-a", "{\"project\":\"Times\",\"stage\":\"needs-human\"}");
        root.AddJob("2-b", "{\"project\":\"Times\",\"stage\":\"build\"}");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver receiver = new();
        (await PushTestData.Subscribe(client, receiver, PushTestData.AppleEndpoint)).Dispose();
        PushWatcher watcher = factory.Services.GetRequiredService<PushWatcher>();

        await watcher.Scan();
        Assert.Empty(factory.Push.Requests);
        root.AddJob("2-b", "{\"project\":\"Times\",\"stage\":\"needs-human\"}");
        await watcher.Scan();

        Assert.Equal(
            "{\"kind\":\"needs-human\",\"project\":\"Times\",\"job\":\"2-b\",\"stage\":\"needs-human\",\"reason\":\"Needs you\"}",
            receiver.DecryptText(Assert.Single(factory.Push.Requests).Body));
    }

    private static async Task AssertInvalid(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid_subscription\"}", await response.Content.ReadAsStringAsync());
    }
}
