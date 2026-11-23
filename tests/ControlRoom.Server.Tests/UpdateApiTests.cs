using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/update</c> through the hosted server: each test awaits the first check on the resolved
/// <see cref="UpdateChecker"/> before asking.</summary>
public sealed class UpdateApiTests
{
    [Fact]
    public async Task NewerRelease_IsAvailableWithTheInstallCommand()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.0.0" };
        factory.Update.RespondWithTag("v1.2.0");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        AssertAvailable(await response.Content.ReadAsStringAsync(), root.Path, "1.0.0", "1.2.0");
    }

    [Fact]
    public async Task WithATailnetName_TheCommandAddsTheTailnetHost()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path, null, "mac.tail1234.ts.net") { CurrentVersion = "1.0.0-rc.1" };
        factory.Update.RespondWithTag("v1.0.0");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        AssertAvailable(json, root.Path, "1.0.0-rc.1", "1.0.0", tailnet: "mac.tail1234.ts.net");
    }

    [Fact]
    public async Task WindowsCopiedCommand_PassesTheOriginalRootTagAndHostToTheInstaller()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot fixture = new();
        string root = Directory.CreateDirectory(Path.Combine(fixture.Path, "O'Brien’s ‘factory‛ ‚local‚ & %TEMP%")).FullName;
        const string Host = "desk.example.ts.net";
        using ServerFactory factory = new(root, null, Host) { CurrentVersion = "1.0.0" };
        factory.Update.RespondWithTag("v1.2.0");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();
        using JsonDocument reply = JsonDocument.Parse(await client.GetStringAsync(UpdateEndpoint.Route));
        string command = reply.RootElement.GetProperty("command").GetString()!;

        File.WriteAllText(Path.Combine(fixture.Path, "launch-agent.ps1"),
            "param($Action,$Release,$FactoryRoot,$TailnetHost)\n"
            + "[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'received.txt'), [string[]]($Action,$Release,$FactoryRoot,$TailnetHost))");
        string run = Path.Combine(fixture.Path, "copy.ps1");
        File.WriteAllText(run, command, new UTF8Encoding(true));
        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            WorkingDirectory = fixture.Path, UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", run },
        };
        using Process process = Process.Start(start)!;
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(1));
        await process.WaitForExitAsync(deadline.Token);
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(["install", "v1.2.0", root, Host], File.ReadAllLines(Path.Combine(fixture.Path, "received.txt")));
    }

    [Theory]
    [InlineData("1.2.0", "v1.2.0")]
    [InlineData("1.2.0", "v1.1.9")]
    [InlineData("1.2.0", "nope")]
    [InlineData("dev", "v9.0.0")]
    public async Task NoNewerRelease_IsNotAvailableAndHasNoLatestUrlOrCommand(string current, string tag)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path, null, "mac.tail1234.ts.net") { CurrentVersion = current };
        factory.Update.RespondWithTag(tag);
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.Equal(
            "{\"current\":\"" + current + "\",\"available\":false,\"latest\":null,\"url\":null,\"command\":null,\"autoUpdate\":false,\"channel\":\"stable\",\"updateInterval\":30}", json);
    }

    [Fact]
    public async Task DevelopChannel_ReportsADevelopPrereleaseWithItsInstallCommand()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.2.3" };
        WriteSettings(factory, "{\"autoUpdate\":true,\"channel\":\"develop\"}\n");
        RespondPerUrl(factory, "v1.2.3", "[{\"tag_name\":\"v1.2.4-develop.1\"},{\"tag_name\":\"v1.2.3\"}]");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        AssertAvailable(json, root.Path, "1.2.3", "1.2.4-develop.1", autoUpdate: true, channel: "develop");
        Assert.All(factory.Update.Requests, request => Assert.Equal(UpdateChecker.ListUrl, request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task ResultFromTheOtherChannel_IsNotAvailable()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.0.0" };
        RespondPerUrl(factory, "v1.2.0", "[{\"tag_name\":\"v1.2.1-develop.4\"}]");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();
        Assert.Contains("\"available\":true", await client.GetStringAsync(UpdateEndpoint.Route), StringComparison.Ordinal);

        WriteSettings(factory, "{\"autoUpdate\":false,\"channel\":\"develop\"}");
        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.Equal(
            "{\"current\":\"1.0.0\",\"available\":false,\"latest\":null,\"url\":null,\"command\":null,"
            + "\"autoUpdate\":false,\"channel\":\"develop\",\"updateInterval\":30}",
            json);
    }

    [Fact]
    public async Task PostThatChangesTheChannel_StartsOneCheckOnTheNewChannel()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.2.3" };
        RespondPerUrl(factory, "v1.2.3", "[{\"tag_name\":\"v1.2.4-develop.2\"}]");
        using HttpClient client = factory.CreateTokenClient();
        UpdateChecker checker = factory.Services.GetRequiredService<UpdateChecker>();
        await factory.Update.WaitForRequests(1);
        await checker.Check();
        Assert.Equal(2, factory.Update.Requests.Count);

        using HttpResponseMessage response = await Post(client, "{\"channel\":\"develop\"}");
        Assert.Equal("{\"autoUpdate\":false,\"channel\":\"develop\",\"updateInterval\":30}", await response.Content.ReadAsStringAsync());
        await factory.Update.WaitForRequests(3);

        Assert.Equal(UpdateChecker.ListUrl, factory.Update.Requests[2].Uri.AbsoluteUri);
        // Check waits for the started check to finish (one check at a time), then asks again.
        await checker.Check();
        Assert.Contains("\"latest\":\"1.2.4-develop.2\"", await client.GetStringAsync(UpdateEndpoint.Route), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostThatKeepsTheChannel_StartsNoCheck()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.2.3" };
        RespondPerUrl(factory, "v1.2.3", "[]");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Update.WaitForRequests(1);

        using HttpResponseMessage first = await Post(client, "{\"autoUpdate\":true}");
        using HttpResponseMessage second = await Post(client, "{\"channel\":\"stable\"}");
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, factory.Update.Requests.Count);
        Assert.All(factory.Update.Requests, request => Assert.Equal(UpdateChecker.LatestUrl, request.Uri.AbsoluteUri));
    }

    [Theory]
    [InlineData("{\"updateInterval\":720}", HttpStatusCode.OK)]
    [InlineData("{\"updateInterval\":60,\"autoUpdate\":true}", HttpStatusCode.OK)]
    [InlineData("{\"updateInterval\":45}", HttpStatusCode.BadRequest)]
    public async Task PostOfTheInterval_StartsNoCheck(string body, HttpStatusCode expected)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.2.3" };
        RespondPerUrl(factory, "v1.2.3", "[]");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Update.WaitForRequests(1);

        using HttpResponseMessage response = await Post(client, body);
        // Check waits for any started check (one at a time), so a check started by the POST would show here.
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(2, factory.Update.Requests.Count);
        Assert.All(factory.Update.Requests, request => Assert.Equal(UpdateChecker.LatestUrl, request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_IsRefused()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync(UpdateEndpoint.Route, null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>The stable URL answers <paramref name="latestTag"/>, the develop list URL
    /// <paramref name="list"/>.</summary>
    private static void RespondPerUrl(ServerFactory factory, string latestTag, string list)
    {
        factory.Update.Respond = (request, _) => Task.FromResult(FakeUpdateHandler.Ok(
            request.RequestUri!.AbsoluteUri == UpdateChecker.ListUrl ? list : "{\"tag_name\":\"" + latestTag + "\"}"));
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string body)
    {
        return client.PostAsync(UpdateEndpoint.SettingsRoute, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static void AssertAvailable(string json, string root, string current, string latest,
        string? tailnet = null, bool autoUpdate = false, string channel = "stable")
    {
        string tag = "v" + latest;
        string command = OperatingSystem.IsWindows()
            ? @".\launch-agent.ps1 install -Release " + tag + " -FactoryRoot " + RoomContext.PowerShellLiteral(root)
                + (tailnet is null ? "" : " -TailnetHost " + tailnet)
            : "scripts/launch-agent.sh install --release " + tag + (tailnet is null ? "" : " --tailnet-host " + tailnet);
        string url = "https://github.com/nothisisnotdaniel/controlroom/releases/tag/" + tag;
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(command, parsed.RootElement.GetProperty("command").GetString());
        Assert.Equal(JsonSerializer.Serialize(new { current, available = true, latest, url, command, autoUpdate,
            channel, updateInterval = 30 }, new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions), json);
    }

    private static void WriteSettings(ServerFactory factory, string content)
    {
        string path = Path.Combine(Path.GetDirectoryName(factory.TokenFile)!, UpdateSettingsStore.FileName);
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            // Written before the server first touches the folder, so it gets the mode the store requires.
            File.SetUnixFileMode(folder, AccessToken.FolderMode);
            File.SetUnixFileMode(path, AccessToken.FileMode);
        }
    }
}
