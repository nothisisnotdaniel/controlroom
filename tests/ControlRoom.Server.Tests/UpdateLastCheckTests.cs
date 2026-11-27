using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ControlRoom.Server.Tests;

/// <summary><c>lastCheck</c> in <c>GET /api/update</c> through the pinned RoomKit package: the updater's
/// <c>last-check</c> stamp next to the token file, read by RoomKit and never written by the server. Each test hosts its
/// own server with its own temp folders and sets the fake clock to a fixed instant.</summary>
public sealed class UpdateLastCheckTests
{
    private const string StampName = "last-check";

    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidStamp_IsReportedAsItsUtcSecond()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = Now;
        using HttpClient client = factory.CreateTokenClient();
        DateTimeOffset checkedAt = new(2026, 3, 1, 8, 15, 42, TimeSpan.Zero);
        TempTokenFolder.WritePrivateText(StampPath(factory), checkedAt.ToUnixTimeSeconds() + "\n");

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2026-03-01T08:15:42Z", parsed.RootElement.GetProperty("lastCheck").GetString());
    }

    [Fact]
    public async Task NoStamp_IsNullAndEveryOtherFieldStaysTheSame()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = Now;
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Assert.EndsWith(",\"updateInterval\":30,\"lastCheck\":null}", json, StringComparison.Ordinal);
        JsonObject fields = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(
            ["current", "available", "latest", "url", "command", "autoUpdate", "channel", "updateInterval", "lastCheck"],
            fields.Select(field => field.Key));
    }

    [Fact]
    public async Task RefusedStamp_IsNullAndEveryOtherFieldEqualsTheNoStampResponse()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = Now;
        using HttpClient client = factory.CreateTokenClient();
        string noStamp = await client.GetStringAsync(UpdateEndpoint.Route);
        string path = StampPath(factory);
        TempTokenFolder.WritePrivateText(path, Now.AddMinutes(-5).ToUnixTimeSeconds() + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
            | UnixFileMode.OtherRead);

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        JsonObject refused = JsonNode.Parse(json)!.AsObject();
        Assert.True(refused.ContainsKey("lastCheck"));
        Assert.Null(refused["lastCheck"]);
        Assert.Equal(WithoutLastCheck(noStamp), WithoutLastCheck(json));
    }

    [Fact]
    public async Task GetAndPostWithoutAStamp_WriteNoStamp()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = Now;
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage get = await client.GetAsync(UpdateEndpoint.Route);
        using HttpResponseMessage post = await client.PostAsync(UpdateEndpoint.SettingsRoute,
            new StringContent("{\"autoUpdate\":true,\"updateInterval\":60}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.False(File.Exists(StampPath(factory)));
        Assert.False(Directory.Exists(StampPath(factory)));
    }

    private static string StampPath(ServerFactory factory)
    {
        return Path.Combine(Path.GetDirectoryName(factory.TokenFile)!, StampName);
    }

    private static string WithoutLastCheck(string json)
    {
        JsonObject fields = JsonNode.Parse(json)!.AsObject();
        fields.Remove("lastCheck");
        return fields.ToJsonString();
    }
}
