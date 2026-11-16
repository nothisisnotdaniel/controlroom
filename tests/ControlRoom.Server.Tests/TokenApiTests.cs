using System.Net;

namespace ControlRoom.Server.Tests;

/// <summary>Every <c>/api/</c> path needs <c>Authorization: Bearer &lt;token&gt;</c>; the app shell stays public.</summary>
public sealed class TokenApiTests
{
    private static readonly string[] ApiPaths =
    [
        "/api/backlogs", "/api/story?project=Story&number=1", "/api/board", "/api/job?id=1-a", "/api/x", "/API/board",
        "/api",
    ];

    private static readonly string[] ShellPaths =
    [
        "/", "/index.html", "/app.js", "/board.js", "/token.js", "/app.css", "/sw.js", "/manifest.webmanifest",
        "/icons/apple-touch-icon.png", "/icons/icon-192.png", "/icons/icon-512.png",
    ];

    public static TheoryData<string, string> RefusedRequests()
    {
        TheoryData<string, string> data = [];
        foreach (string path in ApiPaths)
        {
            foreach (string variant in new[]
                     {
                         "none", "wrong", "short", "long", "basic", "bare", "empty", "query-token", "query-access",
                         "two-headers",
                     })
            {
                data.Add(path, variant);
            }
        }

        return data;
    }

    public static TheoryData<string> Api()
    {
        TheoryData<string> data = [];
        foreach (string path in ApiPaths)
        {
            data.Add(path);
        }

        return data;
    }

    public static TheoryData<string> Shell()
    {
        TheoryData<string> data = [];
        foreach (string path in ShellPaths)
        {
            data.Add(path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public async Task Api_WithoutTheRightBearerTokenIsRefusedWithAFixedBody(string path, string variant)
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        string token = factory.ReadToken();
        using HttpRequestMessage request = new(HttpMethod.Get, WithVariantQuery(path, variant, token));
        AddVariantHeader(request, variant, token);

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", string.Join(",", response.Headers.WwwAuthenticate.Select(value => value.ToString())));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("{\"error\":\"unauthorized\"}", body);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Api))]
    public async Task Api_WithTheTokenGivesTheSameResponseAsWithoutTheGate(string path)
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        if (path.StartsWith("/api/x", StringComparison.Ordinal) || path == "/api")
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Api_AcceptsTheSchemeInAnyCase()
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/board");
        request.Headers.TryAddWithoutValidation("Authorization", "bearer " + factory.ReadToken());

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_RefusesEverythingWhenTheTokenFileTurnsUnsafeWhileRunning()
    {
        using TempFactoryRoot root = Samples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/board")).StatusCode);

        File.WriteAllText(factory.TokenFile, "junk\n");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/board")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Shell))]
    public async Task Shell_LoadsWithoutAToken(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static TempFactoryRoot Samples()
    {
        TempFactoryRoot root = TempFactoryRoot.WithSamples();
        string story = root.AddProject("Story", "- [ ] [#1](backlog/001-first.md) First\n");
        TempFactoryRoot.AddStory(story, "001-first.md", "# First\n\nText.\n");
        root.AddJob("1-a", "{\"stage\":\"review\",\"round\":1}");
        return root;
    }

    private static string WithVariantQuery(string path, string variant, string token)
    {
        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return variant switch
        {
            "query-token" => path + separator + "token=" + token,
            "query-access" => path + separator + "access_token=" + token,
            _ => path,
        };
    }

    private static void AddVariantHeader(HttpRequestMessage request, string variant, string token)
    {
        string? value = variant switch
        {
            "wrong" => "Bearer " + (token[0] == 'A' ? "B" : "A") + token[1..],
            "short" => "Bearer " + token[..^1],
            "long" => "Bearer " + token + "A",
            "basic" => "Basic " + token,
            "bare" => token,
            "empty" => "Bearer ",
            _ => null,
        };
        if (value is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", value);
        }

        if (variant == "two-headers")
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }
    }
}
